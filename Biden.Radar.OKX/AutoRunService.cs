using Biden.Radar.Common.Telegrams;
using Microsoft.Extensions.Hosting;
using OKX.Api;
using OKX.Api.Common.Enums;
using OKX.Api.Public.Enums;
using OKX.Api.Public.Models;

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
            catch
            {
                return new List<OkxInstrument>();
            }
        }

        private static List<OkxInstrument> _tradingSymbols = new List<OkxInstrument>();
        private static OKXWebSocketApiClient _websocketApiClient = new OKXWebSocketApiClient();
        private async Task RunRadar()
        {
            _tradingSymbols = await GetTradingSymbols();
            var totalsymbols = _tradingSymbols.Select(c => c.InstrumentId).Distinct().ToList();
            foreach (var symbol in totalsymbols)
            {
                await SubscribeSymbol(symbol);
            }

            await _websocketApiClient.Public.SubscribeToInstrumentsAsync(async data =>
            {
                if (data != null)
                {
                    if (data.QuoteCurrency == "USDT" && data.State == OkxInstrumentState.Live)
                    {
                        if (!_tradingSymbols.Any(x => x.InstrumentType == OkxInstrumentType.Spot && x.InstrumentId == data.InstrumentId))
                        {
                            _tradingSymbols.Add(data);
                            await _teleMessage.SendMessage($"👀 NEW TOKEN ADDED FOR SPOT: {data.InstrumentId}");
                            await SubscribeSymbol(data.InstrumentId);
                        }
                    }
                }
            }, OkxInstrumentType.Spot);

            await _websocketApiClient.Public.SubscribeToInstrumentsAsync(async data =>
            {
                if (data != null)
                {
                    if (data.QuoteCurrency == "USDT" && data.State == OkxInstrumentState.Live)
                    {
                        if (!_tradingSymbols.Any(x => x.InstrumentType == OkxInstrumentType.Margin && x.InstrumentId == data.InstrumentId))
                        {
                            _tradingSymbols.Add(data);
                            await _teleMessage.SendMessage($"👀 NEW TOKEN ADDED FOR MARGIN: {data.InstrumentId}");
                            await SubscribeSymbol(data.InstrumentId);
                        }
                    }
                }
            }, OkxInstrumentType.Margin);

            await _websocketApiClient.Public.SubscribeToInstrumentsAsync(async data =>
            {
                if (data != null)
                {
                    if (data.SettlementCurrency == "USDT" && data.State == OkxInstrumentState.Live)
                    {
                        if (!_tradingSymbols.Any(x => x.InstrumentType == OkxInstrumentType.Swap && x.InstrumentId == data.InstrumentId))
                        {
                            _tradingSymbols.Add(data);
                            await _teleMessage.SendMessage($"👀 NEW TOKEN ADDED FOR SWAP: {data.InstrumentId}");
                            await SubscribeSymbol(data.InstrumentId);
                        }
                    }
                }
            }, OkxInstrumentType.Swap);
        }

        private async Task SubscribeSymbol(string symbol)
        {
            var subResult = await _websocketApiClient.Public.SubscribeToCandlesticksAsync(tradeData =>
            {
                if (tradeData != null)
                {
                    var symbol = tradeData.InstrumentId;
                    long converttimestamp = (long)(tradeData.Time.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                    var timestamp = tradeData.Timestamp / 1000;
                    //Console.WriteLine($"Time: {timestamp}, Confirm: {tradeData.Confirm}");

                    if (tradeData.Confirm)
                    {
                        var longPercent = (tradeData.Low - tradeData.Open) / tradeData.Open * 100;
                        var shortPercent = (tradeData.High - tradeData.Open) / tradeData.Open * 100;
                        var longElastic = longPercent == 0 ? 0 : (longPercent - ((tradeData.Close - tradeData.Open) / tradeData.Open * 100)) / longPercent * 100;
                        var shortElastic = shortPercent == 0 ? 0 : (shortPercent - ((tradeData.Close - tradeData.Open) / tradeData.Open * 100)) / shortPercent * 100;
                        var instruments = _tradingSymbols.Where(r => r.InstrumentId == symbol);
                        var isPerp = instruments.Any(r => r.InstrumentType == OkxInstrumentType.Swap);
                        var isMargin = instruments.Any(r => r.InstrumentType == OkxInstrumentType.Margin);

                        var filterVol = isPerp ? 50000 : isMargin ? 10000 : 8000;
                        var filterTP = isPerp ? 1 : isMargin ? 0.6M : 1.5M;

                        if (tradeData.QuoteVolume > filterVol && longPercent < -filterTP && longElastic >= 50)
                        {
                            var teleMessage = (isPerp ? "💥 " : isMargin ? "✅ " : "") + $"{symbol}: {Math.Round(longPercent, 2)}%, TP: {Math.Round(longElastic, 2)}%, VOL: ${tradeData.QuoteVolume.FormatNumber()}";
                            _teleMessage.SendMessage(teleMessage);
                        }
                        if (tradeData.QuoteVolume > filterVol && shortPercent > filterTP && shortElastic >= 50 && (isPerp || isMargin))
                        {
                            var teleMessage = (isPerp ? "💥 " : isMargin ? "✅ " : "") + $"{symbol}: {Math.Round(shortPercent, 2)}%, TP: {Math.Round(shortElastic, 2)}%, VOL: ${tradeData.QuoteVolume.FormatNumber()}";
                            _teleMessage.SendMessage(teleMessage);
                        }
                    }

                }
            }, symbol, OkxPeriod.OneSecond);
        }
    }
}
