using Biden.Radar.Common.Telegrams;
using CryptoExchange.Net.CommonObjects;
using Gate.IO.Api;
using Gate.IO.Api.Enums;
using Gate.IO.Api.Models.RestApi.Futures;
using Gate.IO.Api.Models.RestApi.Margin;
using Gate.IO.Api.Models.RestApi.Spot;
using GateIo.Net.Clients;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace Biden.Radar.Gate
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

        public async Task<List<SpotMarket>> GetSpotTradingSymbols()
        {
            try
            {
                var restClient = new GateRestApiClient();
                var result = await restClient.Spot.GetAllPairsAsync();
                var spotSymbolsData = result.Data.ToList();
                var spotSymbols = spotSymbolsData.Where(s => s.Quote == "USDT" && s.Status == SpotMarketStatus.Tradable).ToList();
                return spotSymbols;
            }
            catch
            {
                return new List<SpotMarket>();
            }
        }

        public async Task<List<MarginMarket>> GetMarginTradingSymbols()
        {
            try
            {
                var restClient = new GateRestApiClient();
                var result = await restClient.Margin.Isolated.GetAllPairsAsync();
                var symbolsData = result.Data.ToList();
                var symbols = symbolsData.Where(s => s.Quote == "USDT" && s.Status == MarginMarketStatus.Enabled).ToList();
                return symbols;
            }
            catch
            {
                return new List<MarginMarket>();
            }
        }

        public async Task<List<PerpetualContract>> GetPerpTradingSymbols()
        {
            try
            {
                var restClient = new GateRestApiClient();
                var result = await restClient.Futures.Perpetual.USDT.GetContractsAsync();
                return result.Data.ToList();
            }
            catch
            {
                return new List<PerpetualContract>();
            }
        }

        private static ConcurrentDictionary<string, Candle> _candles = new ConcurrentDictionary<string, Candle>();
        private static ConcurrentDictionary<string, long> _candle1s = new ConcurrentDictionary<string, long>();
        private static ConcurrentDictionary<string, Candle> _perpCandles = new ConcurrentDictionary<string, Candle>();


        private async Task RunRadar()
        {
            var spotSymbols = await GetSpotTradingSymbols();
            var marginSymbols = await GetMarginTradingSymbols();
            var perpSymbols = await GetPerpTradingSymbols();
            var spotSymbolNames = spotSymbols.Select(s => s.Symbol).ToList();
            var marginSymbolNames = marginSymbols.Select(s => s.Symbol).ToList();
            var perpSymbolNames = perpSymbols.Select(s => s.Contract).ToList();

            var spotBatches = spotSymbolNames.Select((x, i) => new { Index = i, Value = x })
                              .GroupBy(x => x.Index / 100)
                              .Select(x => x.Select(v => v.Value).ToList())
                              .ToList();
            var perpBatches = perpSymbolNames.Select((x, i) => new { Index = i, Value = x })
                              .GroupBy(x => x.Index / 100)
                              .Select(x => x.Select(v => v.Value).ToList())
                              .ToList();
            
            var socketClient = new GateIoSocketClient();
            //var tickerSubscriptionResult = await socketClient.SpotApi.SubscribeToTradeUpdatesAsync("ETH_USDT", data =>
            //{
            //    Console.WriteLine(data.Timestamp);
            //    Console.WriteLine($"Amount: {data.Data.Quantity}, Price: {data.Data.Price}");
            //});

            long preTimestamp = 0;
            _candle1s.Clear();
            _candles.Clear();
            foreach (var symbols in spotBatches)
            {
                var subResult = await socketClient.SpotApi.SubscribeToTradeUpdatesAsync(symbols, async tradeData =>
                {
                    if (tradeData != null)
                    {
                        var symbol = tradeData.Data.Symbol;
                        var symbolType = CandleType.Spot;
                        if (marginSymbolNames.Contains(symbol))
                        {
                            symbolType = CandleType.Margin;
                        }

                        long converttimestamp = (long)(tradeData.Timestamp.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                        var timestamp = converttimestamp / 1000;
                        var tick = new TickData
                        {
                            Timestamp = converttimestamp,
                            Price = tradeData.Data.Price,
                            Amount = tradeData.Data.Price * tradeData.Data.Quantity
                        };
                        _candles.AddOrUpdate(symbol,
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
                            await ProcessBufferedData();
                        }
                    }
                });
            }

            //_perpCandles.Clear();
            //foreach (var contract in perpSymbolNames)
            //{
            //    var subResult = await socketClient.PerpetualFuturesApi.SubscribeToTradeUpdatesAsync("", contract, async tradeData =>
            //    {
            //        if (tradeData != null)
            //        {
            //            var data = tradeData.Data.First();
            //            var symbol = data.Contract;
            //            var symbolType = CandleType.Perp;

            //            long converttimestamp = (long)(tradeData.Timestamp.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            //            var timestamp = converttimestamp / 1000;
            //            var tick = new TickData
            //            {
            //                Timestamp = converttimestamp,
            //                Price = data.Price,
            //                Amount = data.Price * data.Quantity
            //            };
            //            _candles.AddOrUpdate(symbol,
            //                    (ts) => new Candle // Tạo nến mới nếu chưa có
            //                    {
            //                        Open = tick.Price,
            //                        High = tick.Price,
            //                        Low = tick.Price,
            //                        Close = tick.Price,
            //                        Volume = tick.Amount,
            //                        CandleType = symbolType
            //                    },
            //                    (ts, existingCandle) => // Cập nhật nến hiện tại
            //                    {
            //                        existingCandle.High = Math.Max(existingCandle.High, tick.Price);
            //                        existingCandle.Low = Math.Min(existingCandle.Low, tick.Price);
            //                        existingCandle.Close = tick.Price;
            //                        existingCandle.Volume += tick.Amount;
            //                        existingCandle.CandleType = symbolType;
            //                        return existingCandle;
            //                    });
            //            if (preTimestamp == 0)
            //            {
            //                preTimestamp = timestamp;
            //            }
            //            else if (timestamp > preTimestamp)
            //            {
            //                preTimestamp = timestamp;
            //                await ProcessPerpBufferedData();
            //            }
            //        }
            //    });
            //}
        }

        private async Task ProcessBufferedData()
        {
            // Copy the current buffer for processing and clear the original buffer
            var dataToProcess = new ConcurrentDictionary<string, Candle>(_candles);
            _candles.Clear();

            foreach (var kvp in dataToProcess)
            {
                var symbol = kvp.Key;
                var candle = kvp.Value;

                var longPercent = (candle.Low - candle.Open) / candle.Open * 100;
                var shortPercent = (candle.High - candle.Open) / candle.Open * 100;
                var longElastic = longPercent == 0 ? 0 : (longPercent - ((candle.Close - candle.Open) / candle.Open * 100)) / longPercent * 100;
                var shortElastic = shortPercent == 0 ? 0 : (shortPercent - ((candle.Close - candle.Open) / candle.Open * 100)) / shortPercent * 100;
                if (longPercent < -1.2M && longElastic >= 60 && candle.Volume > 200)
                {
                    var teleMessage = (candle.CandleType == CandleType.Margin ? "✅ " : "") + $"{symbol}: {Math.Round(longPercent, 2)}%, TP: {Math.Round(longElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    await _teleMessage.SendMessage(teleMessage);
                }
                if (shortPercent > 1.2M && shortElastic >= 60 && candle.CandleType == CandleType.Margin && candle.Volume > 200)
                {
                    var teleMessage = $"✅ {symbol}: {Math.Round(shortPercent, 2)}%, TP: {Math.Round(shortElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    await _teleMessage.SendMessage(teleMessage);
                }
            }
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
                if (longPercent < -1.2M && longElastic >= 50)
                {
                    var teleMessage = $"💥 {symbol}: {Math.Round(longPercent, 2)}%, TP: {Math.Round(longElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    await _teleMessage.SendMessage(teleMessage);
                }
                if (shortPercent > 1.2M && shortElastic >= 50)
                {
                    var teleMessage = $"💥 {symbol}: {Math.Round(shortPercent, 2)}%, TP: {Math.Round(shortElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
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
