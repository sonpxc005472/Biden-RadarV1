using Biden.Radar.Common.Telegrams;
using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Objects.Models.Futures;
using Binance.Net.Objects.Models.Spot;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace Biden.Radar.Binance
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

        public async Task<List<BinanceSymbol>> GetSpotTradingSymbols()
        {
            try
            {
                var publicApi = new BinanceRestClient();
                var spotSymbolsData = await publicApi.SpotApi.ExchangeData.GetExchangeInfoAsync();
                var spotSymbols = spotSymbolsData.Data.Symbols.Where(s => s.QuoteAsset == "USDT" && s.Status == SymbolStatus.Trading).ToList();
                return spotSymbols;
            }
            catch
            {
                return new List<BinanceSymbol>();
            }
        }


        public async Task<List<BinanceFuturesUsdtSymbol>> GetPerpTradingSymbols()
        {
            try
            {
                var publicApi = new BinanceRestClient();
                var divSymbolsData = await publicApi.UsdFuturesApi.ExchangeData.GetExchangeInfoAsync();
                var swapSymbols = divSymbolsData.Data.Symbols.Where(s => s.QuoteAsset == "USDT" && s.Status == SymbolStatus.Trading).ToList();
                return swapSymbols;
            }
            catch
            {
                return new List<BinanceFuturesUsdtSymbol>();
            }
        }

        private static ConcurrentDictionary<string, Candle> _perpCandles = new ConcurrentDictionary<string, Candle>();
        private DateTime _startTimeSpot = DateTime.Now;
        private DateTime _startTimePerp = DateTime.Now;
        private static List<BinanceSymbol> _spotSymbols = new List<BinanceSymbol>();
        private static List<BinanceFuturesUsdtSymbol> _perpSymbols = new List<BinanceFuturesUsdtSymbol>();
        private static BinanceSocketClient _websocketApiClient = new BinanceSocketClient();

        private async Task RunRadar()
        {
            //_spotSymbols = await GetSpotTradingSymbols();
            _perpSymbols = await GetPerpTradingSymbols();
            //var spotSymbolNames = _spotSymbols.Select(s => s.Name).ToList();
            var perpSymbolNames = _perpSymbols.Select(s => s.Name).ToList();
            long preTimestamp = 0;

            //var batches = spotSymbolNames.Select((x, i) => new { Index = i, Value = x })
            //                  .GroupBy(x => x.Index / 10)
            //                  .Select(x => x.Select(v => v.Value).ToList())
            //                  .ToList();

            //foreach (var symbols in batches)
            //{
            //    var subResult = await _websocketApiClient.SpotApi.ExchangeData.SubscribeToKlineUpdatesAsync(symbols, KlineInterval.OneSecond, async data =>
            //    {
            //        var tradeData = data.Data?.Data;
            //        if (tradeData != null)
            //        {
            //            var symbol = data.Symbol;

            //            if (tradeData.Final)
            //            {
            //                var longPercent = (tradeData.LowPrice - tradeData.OpenPrice) / tradeData.OpenPrice * 100;
            //                var shortPercent = (tradeData.HighPrice - tradeData.OpenPrice) / tradeData.OpenPrice * 100;
            //                var longElastic = longPercent == 0 ? 0 : (longPercent - ((tradeData.ClosePrice - tradeData.OpenPrice) / tradeData.OpenPrice * 100)) / longPercent * 100;
            //                var shortElastic = shortPercent == 0 ? 0 : (shortPercent - ((tradeData.ClosePrice - tradeData.OpenPrice) / tradeData.OpenPrice * 100)) / shortPercent * 100;
            //                var instruments = _spotSymbols.Where(r => r.Name == symbol);
            //                var isMargin = instruments.Any(r => r.IsMarginTradingAllowed);

            //                var filterVol = isMargin ? 3000 : 500;
            //                var filterTP = isMargin ? 0.5M : 1.2M;

            //                if (tradeData.QuoteVolume > filterVol && longPercent < -filterTP && longElastic >= 40)
            //                {
            //                    var teleMessage = (isMargin ? "✅ " : "") + $"{symbol}: {Math.Round(longPercent, 2)}%, TP: {Math.Round(longElastic, 2)}%, VOL: ${tradeData.QuoteVolume.FormatNumber()}";
            //                    await _teleMessage.SendMessage(teleMessage);
            //                }
            //                if (tradeData.QuoteVolume > filterVol && shortPercent > filterTP && shortElastic >= 40 && (isMargin))
            //                {
            //                    var teleMessage = (isMargin ? "✅ " : "") + $"{symbol}: {Math.Round(shortPercent, 2)}%, TP: {Math.Round(shortElastic, 2)}%, VOL: ${tradeData.QuoteVolume.FormatNumber()}";
            //                    await _teleMessage.SendMessage(teleMessage);
            //                }                            
            //            }
            //        }
            //    });
            //}

            var batches = perpSymbolNames.Select((x, i) => new { Index = i, Value = x })
                              .GroupBy(x => x.Index / 50)
                              .Select(x => x.Select(v => v.Value).ToList())
                              .ToList();
            foreach (var symbols in batches)
            {
                _ = _websocketApiClient.UsdFuturesApi.SubscribeToTradeUpdatesAsync(symbols, async data =>
                {
                    if (data != null)
                    {
                        var tradeData = data.Data;
                        var symbol = tradeData.Symbol;
                        var symbolType = CandleType.Perp;

                        long converttimestamp = (long)(tradeData.TradeTime.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                        var timestamp = converttimestamp / 1000;
                        var tick = new TickData
                        {
                            Timestamp = converttimestamp,
                            Price = tradeData.Price,
                            Amount = tradeData.Price * tradeData.Quantity
                        };
                        _perpCandles.AddOrUpdate(symbol,
                                (ts) => new Candle // Tạo nến mới nếu chưa có
                                {
                                    Open = tick.Price,
                                    High = tick.Price,
                                    Low = tick.Price,
                                    Close = tick.Price,
                                    Volume = tick.Amount,
                                    CandleType = symbolType
                                },
                                (ts, existingCandle) => // Cập nhật nến hiện tại
                                {
                                    existingCandle.High = Math.Max(existingCandle.High, tick.Price);
                                    existingCandle.Low = Math.Min(existingCandle.Low, tick.Price);
                                    existingCandle.Close = tick.Price;
                                    existingCandle.Volume += tick.Amount;
                                    existingCandle.CandleType = symbolType;
                                    return existingCandle;
                                });
                        if (preTimestamp == 0)
                        {
                            preTimestamp = timestamp;
                        }
                        else if (timestamp > preTimestamp)
                        {
                            preTimestamp = timestamp;
                            await ProcessPerpBufferedData();
                        }
                    }
                });
            }

            await _websocketApiClient.SpotApi.ExchangeData.SubscribeToKlineUpdatesAsync("BTCUSDT", KlineInterval.ThreeMinutes, async data =>
            {
                var currentTime = DateTime.Now;
                if ((currentTime - _startTimeSpot).TotalMinutes >= 5)
                {
                    _startTimeSpot = currentTime;
                    //5p get symbols again to check new listings
                    //var currentSpotSymbols = await GetSpotTradingSymbols();
                    //if (currentSpotSymbols.Count != _spotSymbols.Count)
                    //{
                    //    var newTokensAdded = currentSpotSymbols.Select(x => x.Name).Except(_spotSymbols.Select(s => s.Name)).ToList();
                    //    if (newTokensAdded.Any())
                    //    {
                    //        await _teleMessage.SendMessage($"👀 NEW TOKEN ADDED SPOT/MARGIN: {string.Join(",", newTokensAdded)}");
                    //        await Task.Delay(1000);
                    //        Environment.Exit(0);
                    //    }
                    //}

                    var currentPerpSymbols = await GetPerpTradingSymbols();
                    if (currentPerpSymbols.Count != _perpSymbols.Count)
                    {
                        var newTokensAdded = currentPerpSymbols.Select(x => x.Name).Except(_perpSymbols.Select(s => s.Name)).ToList();
                        if (newTokensAdded.Any())
                        {
                            await _teleMessage.SendMessage($"👀 NEW DERIVATIVES TOKEN ADDED: {string.Join(",", newTokensAdded)}");
                            await Task.Delay(1000);
                            Environment.Exit(0);
                        }
                    }
                }
            });
        }

        private async Task ProcessPerpBufferedData()
        {
            // Copy the current buffer for processing and clear the original buffer
            var dataToProcess = new ConcurrentDictionary<string, Candle>(_perpCandles);
            _perpCandles.Clear();

            foreach (var kvp in dataToProcess)
            {
                var symbol = kvp.Key;
                var candle = kvp.Value;

                var longPercent = (candle.Low - candle.Open) / candle.Open * 100;
                var shortPercent = (candle.High - candle.Open) / candle.Open * 100;
                var longElastic = longPercent == 0 ? 0 : (longPercent - ((candle.Close - candle.Open) / candle.Open * 100)) / longPercent * 100;
                var shortElastic = shortPercent == 0 ? 0 : (shortPercent - ((candle.Close - candle.Open) / candle.Open * 100)) / shortPercent * 100;
                if (candle.Volume > 15000 && longPercent < -1 && longElastic >= 30)
                {
                    var teleMessage = $"💥 {symbol}: {Math.Round(longPercent, 2)}%, TP: {Math.Round(longElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    Console.WriteLine(teleMessage);
                    await _teleMessage.SendMessage(teleMessage);
                }
                if (candle.Volume > 15000 && shortPercent > 1 && shortElastic >= 30)
                {
                    var teleMessage = $"💥 {symbol}: {Math.Round(shortPercent, 2)}%, TP: {Math.Round(shortElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    Console.WriteLine(teleMessage);
                    await _teleMessage.SendMessage(teleMessage);
                }
            }            
        }

    }

    public class SubscribeObject
    {
        public SubscribeData[] data { get; set; }
    }

    public class SubscribeData
    {
        public string i { get; set; }
        public long T { get; set; }
        public string p { get; set; }
        public string v { get; set; }
        public string s { get; set; }
    }

    public class TickData
    {
        public long Timestamp { get; set; }
        public decimal Price { get; set; }
        public decimal Amount { get; set; }
    }

    public class Candle
    {
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public bool Confirmed { get; set; }
        public CandleType CandleType { get; set; }
    }

    public enum CandleType
    {
        Spot = 0,
        Margin = 1,
        Perp = 2
    }
}
