using Biden.Radar.Common;
using Biden.Radar.Common.Telegrams;
using Microsoft.Extensions.Hosting;
using OKX.Api;
using OKX.Api.Common.Enums;
using OKX.Api.Public.Enums;
using OKX.Api.Public.Models;
using System.Collections.Concurrent;

namespace Biden.Radar.OKX
{
    public class AutoRunService : BackgroundService
    {
        private readonly ITeleMessage _teleMessage;

        public AutoRunService(ITeleMessage teleMessage)
        {
            _teleMessage = teleMessage;
        }
        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("Running...");

            await RunRadar();
        }

        public async Task<List<OkxInstrument>> GetTradingSymbols()
        {
            try
            {
                var publicApi = new OkxRestApiClient();
                var spotSymbolsData = await publicApi.Public.GetInstrumentsAsync(OkxInstrumentType.Spot);
                var spotSymbols = spotSymbolsData.Data.Where(s => s.State == OkxInstrumentState.Live && s.QuoteCurrency == "USDT").ToList();
                var marginSymbolsData = await publicApi.Public.GetInstrumentsAsync(OkxInstrumentType.Margin);
                var marginSymbols = marginSymbolsData.Data.Where(s => s.State == OkxInstrumentState.Live && s.QuoteCurrency == "USDT").ToList();
                var swapSymbolsData = await publicApi.Public.GetInstrumentsAsync(OkxInstrumentType.Swap);
                var swapSymbols = swapSymbolsData.Data.Where(s => s.State == OkxInstrumentState.Live && s.SettlementCurrency == "USDT").ToList();
                var symbolInfos = spotSymbols.Concat(marginSymbols).Concat(swapSymbols);
                return symbolInfos.ToList();
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return new List<OkxInstrument>();
            }
        }

        private static List<OkxInstrument> _tradingSymbols = new List<OkxInstrument>();
        private static OKXWebSocketApiClient _websocketApiClient = new OKXWebSocketApiClient();
        private static ConcurrentDictionary<string, Candle> _tradeCandles = new ConcurrentDictionary<string, Candle>();

        private async Task RunRadar()
        {
            _tradingSymbols = await GetTradingSymbols();
            var totalsymbols = _tradingSymbols.Select(c => c.InstrumentId).Distinct().ToList();
            Console.WriteLine($"Total symbol to scan: {totalsymbols.Count}");
            foreach (var symbol in totalsymbols)
            {
                SubscribeSymbol(symbol);
            }

            _ = _websocketApiClient.Public.SubscribeToInstrumentsAsync(async data =>
            {
                if (data != null)
                {
                    if (data.QuoteCurrency == "USDT" && data.State == OkxInstrumentState.Live)
                    {
                        if (!_tradingSymbols.Any(x => x.InstrumentType == OkxInstrumentType.Spot && x.InstrumentId == data.InstrumentId))
                        {
                            _tradingSymbols.Add(data);
                            await _teleMessage.SendMessage($"👀 NEW TOKEN ADDED FOR SPOT: {data.InstrumentId}");
                            SubscribeSymbol(data.InstrumentId);
                        }
                    }
                }
            }, OkxInstrumentType.Spot);

            _ = _websocketApiClient.Public.SubscribeToInstrumentsAsync(async data =>
            {
                if (data != null)
                {
                    if (data.QuoteCurrency == "USDT" && data.State == OkxInstrumentState.Live)
                    {
                        if (!_tradingSymbols.Any(x => x.InstrumentType == OkxInstrumentType.Margin && x.InstrumentId == data.InstrumentId))
                        {
                            _tradingSymbols.Add(data);
                            await _teleMessage.SendMessage($"👀 NEW TOKEN ADDED FOR MARGIN: {data.InstrumentId}");
                            SubscribeSymbol(data.InstrumentId);
                        }
                    }
                }
            }, OkxInstrumentType.Margin);

            _ = _websocketApiClient.Public.SubscribeToInstrumentsAsync(async data =>
            {
                if (data != null)
                {
                    if (data.SettlementCurrency == "USDT" && data.State == OkxInstrumentState.Live)
                    {
                        if (!_tradingSymbols.Any(x => x.InstrumentType == OkxInstrumentType.Swap && x.InstrumentId == data.InstrumentId))
                        {
                            _tradingSymbols.Add(data);
                            await _teleMessage.SendMessage($"👀 NEW TOKEN ADDED FOR SWAP: {data.InstrumentId}");
                            SubscribeSymbol(data.InstrumentId);
                        }
                    }
                }
            }, OkxInstrumentType.Swap);
        }

        long preTimestamp = 0;

        private void SubscribeSymbol(string symbol)
        {
            _ = _websocketApiClient.Public.SubscribeToCandlesticksAsync(async tradeData =>
            {
                if (tradeData != null)
                {
                    var symbol = tradeData.InstrumentId;
                    var instruments = _tradingSymbols.Where(r => r.InstrumentId == symbol);
                    var isPerp = instruments.Any(r => r.InstrumentType == OkxInstrumentType.Swap);
                    var isMargin = instruments.Any(r => r.InstrumentType == OkxInstrumentType.Margin);
                    if (tradeData.Confirm)
                    {
                        var longPercent = (tradeData.Low - tradeData.Open) / tradeData.Open * 100;
                        var shortPercent = (tradeData.High - tradeData.Open) / tradeData.Open * 100;
                        var longElastic = longPercent == 0 ? 0 : (longPercent - ((tradeData.Close - tradeData.Open) / tradeData.Open * 100)) / longPercent * 100;
                        var shortElastic = shortPercent == 0 ? 0 : (shortPercent - ((tradeData.Close - tradeData.Open) / tradeData.Open * 100)) / shortPercent * 100;
                        
                        var filterVol = isPerp ? 10000 : isMargin ? 3000 : 500;
                        var filterTP = isPerp ? 0.8M : isMargin ? 0.5M : 1M;

                        if (tradeData.QuoteVolume > filterVol && longPercent < -filterTP && longElastic >= 20)
                        {
                            var teleMessage = (isPerp ? "💥 " : isMargin ? "✅ " : "") + $"{symbol}: {Math.Round(longPercent, 2)}%, TP: {Math.Round(longElastic, 2)}%, VOL: ${tradeData.QuoteVolume.FormatNumber()}";
                            await _teleMessage.SendMessage(teleMessage);
                        }
                        if (tradeData.QuoteVolume > filterVol && shortPercent > filterTP && shortElastic >= 20 && (isPerp || isMargin))
                        {
                            var teleMessage = (isPerp ? "💥 " : isMargin ? "✅ " : "") + $"{symbol}: {Math.Round(shortPercent, 2)}%, TP: {Math.Round(shortElastic, 2)}%, VOL: ${tradeData.QuoteVolume.FormatNumber()}";
                            await _teleMessage.SendMessage(teleMessage);
                        }
                    }
                    var symbolType = isPerp ? CandleType.Perp : (isMargin ? CandleType.Margin : CandleType.Spot);

                    long converttimestamp = (long)(tradeData.Time.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

                    var timestamp = converttimestamp / 2000;
                    if (preTimestamp == 0)
                    {
                        preTimestamp = timestamp;
                    }
                    else if (timestamp > preTimestamp)
                    {
                        preTimestamp = timestamp;
                        await ProcessBufferedData();
                    }
                    _tradeCandles.AddOrUpdate(symbol,
                            (ts) => new Candle // Tạo nến mới nếu chưa có
                            {
                                Open = tradeData.Open,
                                High = tradeData.High,
                                Low = tradeData.Low,
                                Close = tradeData.Close,
                                Volume = tradeData.QuoteVolume,
                                CandleType = symbolType
                            },
                            (ts, existingCandle) => // Cập nhật nến hiện tại
                            {
                                existingCandle.High = Math.Max(existingCandle.High, tradeData.High);
                                existingCandle.Low = Math.Min(existingCandle.Low, tradeData.Low);
                                existingCandle.Close = tradeData.Close;
                                existingCandle.Volume += tradeData.QuoteVolume;
                                existingCandle.CandleType = symbolType;
                                return existingCandle;
                            });
                    
                }
            }, symbol, OkxPeriod.OneSecond);
        }

        private async Task ProcessBufferedData()
        {
            // Copy the current buffer for processing and clear the original buffer
            var dataToProcess = new ConcurrentDictionary<string, Candle>(_tradeCandles);
            _tradeCandles.Clear();

            foreach (var kvp in dataToProcess)
            {
                var symbol = kvp.Key;
                var candle = kvp.Value;

                var longPercent = (candle.Low - candle.Open) / candle.Open * 100;
                var shortPercent = (candle.High - candle.Open) / candle.Open * 100;
                var longElastic = longPercent == 0 ? 0 : (longPercent - ((candle.Close - candle.Open) / candle.Open * 100)) / longPercent * 100;
                var shortElastic = shortPercent == 0 ? 0 : (shortPercent - ((candle.Close - candle.Open) / candle.Open * 100)) / shortPercent * 100;
                var filterVol = candle.CandleType == CandleType.Perp ? 40000 : candle.CandleType == CandleType.Margin ? 8000 : 800;
                var filterTP = candle.CandleType == CandleType.Perp ? 0.8M : candle.CandleType == CandleType.Margin ? 0.5M : 1M;

                if (candle.Volume > filterVol && longPercent < -filterTP && longElastic >= 30)
                {
                    var teleMessage = (candle.CandleType == CandleType.Perp ? "💥 " : candle.CandleType == CandleType.Margin ? "✅ " : "") + $"{symbol}: {Math.Round(longPercent, 2)}%, TP: {Math.Round(longElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    await _teleMessage.SendMessage(teleMessage);
                }
                if (candle.Volume > filterVol && shortPercent > filterTP && shortElastic >= 30 && (candle.CandleType != CandleType.Spot))
                {
                    var teleMessage = (candle.CandleType == CandleType.Perp ? "💥 " : candle.CandleType == CandleType.Margin ? "✅ " : "") + $"{symbol}: {Math.Round(shortPercent, 2)}%, TP: {Math.Round(shortElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    await _teleMessage.SendMessage(teleMessage);
                }
            }            
        }

    }
}
