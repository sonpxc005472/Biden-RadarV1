using Biden.Radar.Common.Telegrams;
using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace Biden.Radar.Bybit
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

        public async Task<List<BybitSpotSymbol>> GetSpotTradingSymbols()
        {
            try
            {
                var publicApi = new BybitRestClient();
                var spotSymbolsData = await publicApi.V5Api.ExchangeData.GetSpotSymbolsAsync();
                var spotSymbols = spotSymbolsData.Data.List.Where(s => s.QuoteAsset == "USDT").ToList();
                return spotSymbols;
            }
            catch
            {
                return new List<BybitSpotSymbol>();
            }
        }


        public async Task<List<BybitLinearInverseSymbol>> GetPerpTradingSymbols()
        {
            try
            {
                var publicApi = new BybitRestClient();                
                var swapSymbolsData = await publicApi.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(Category.Linear);
                var swapSymbols = swapSymbolsData.Data.List.Where(s => s.SettleAsset == "USDT").ToList();
                return swapSymbols;
            }
            catch
            {
                return new List<BybitLinearInverseSymbol>();
            }
        }

        private static ConcurrentDictionary<string, Candle> _spotCandles = new ConcurrentDictionary<string, Candle>();
        private static ConcurrentDictionary<string, Candle> _perpCandles = new ConcurrentDictionary<string, Candle>();


        private async Task RunRadar()
        {
            var spotSymbols = await GetSpotTradingSymbols();
            var perpSymbols = await GetPerpTradingSymbols();
            var spotSymbolNames = spotSymbols.Select(s => s.Name).ToList();
            var perpSymbolNames = perpSymbols.Select(s => s.Name).ToList();
            
            var batches = spotSymbolNames.Select((x, i) => new { Index = i, Value = x })
                              .GroupBy(x => x.Index / 10)
                              .Select(x => x.Select(v => v.Value).ToList())
                              .ToList();
            
            var socketClient = new BybitSocketClient();
            long preTimestamp = 0;
            _spotCandles.Clear();
            foreach (var symbols in batches)
            {
                var subResult = await socketClient.V5SpotApi.SubscribeToTradeUpdatesAsync(symbols, async data =>
                {
                    if (data != null)
                    {
                        var tradeDatas = data.Data;
                        foreach (var tradeData in tradeDatas)
                        {
                            var symbol = tradeData.Symbol;
                            var symbolType = CandleType.Spot;
                            var symbolInfo = spotSymbols.FirstOrDefault(x => x.Name == symbol);
                            if (symbolInfo != null && (symbolInfo.MarginTrading == MarginTrading.Both || symbolInfo.MarginTrading == MarginTrading.UtaOnly))
                            {
                                symbolType = CandleType.Margin;
                            }
                            long converttimestamp = (long)(tradeData.Timestamp.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                            var timestamp = converttimestamp / 1000;
                            var tick = new TickData
                            {
                                Timestamp = converttimestamp,
                                Price = tradeData.Price,
                                Amount = tradeData.Price * tradeData.Quantity
                            };
                            _spotCandles.AddOrUpdate(symbol,
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
                                await ProcessSpotBufferedData();
                            }
                        }
                    }
                });
            }

            batches = perpSymbolNames.Select((x, i) => new { Index = i, Value = x })
                              .GroupBy(x => x.Index / 10)
                              .Select(x => x.Select(v => v.Value).ToList())
                              .ToList();
            foreach (var symbols in batches)
            {
                var subResult = await socketClient.V5LinearApi.SubscribeToTradeUpdatesAsync(symbols, async data =>
                {
                    if (data != null)
                    {
                        var tradeDatas = data.Data;
                        foreach (var tradeData in tradeDatas)
                        {
                            var symbol = tradeData.Symbol;
                            var symbolType = CandleType.Perp;
                            
                            long converttimestamp = (long)(tradeData.Timestamp.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
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
                    }
                });
            }
        }

        private async Task ProcessSpotBufferedData()
        {
            // Copy the current buffer for processing and clear the original buffer
            var dataToProcess = new ConcurrentDictionary<string, Candle>(_spotCandles);
            _spotCandles.Clear();

            foreach (var kvp in dataToProcess)
            {
                var symbol = kvp.Key;
                var candle = kvp.Value;

                var longPercent = (candle.Low - candle.Open) / candle.Open * 100;
                var shortPercent = (candle.High - candle.Open) / candle.Open * 100;
                var longElastic = longPercent == 0 ? 0 : (longPercent - ((candle.Close - candle.Open) / candle.Open * 100)) / longPercent * 100;
                var shortElastic = shortPercent == 0 ? 0 : (shortPercent - ((candle.Close - candle.Open) / candle.Open * 100)) / shortPercent * 100;
                var filterVol = candle.CandleType == CandleType.Margin ? 8000 : 5000;
                if (candle.Volume > filterVol && longPercent < -0.8M && longElastic >= 40)
                {                    
                    var teleMessage = (candle.CandleType == CandleType.Margin ? "✅ " : "") + $"{symbol}: {Math.Round(longPercent, 2)}%, TP: {Math.Round(longElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    await _teleMessage.SendMessage(teleMessage);
                }
                if (candle.Volume > filterVol && shortPercent > 0.8M && shortElastic >= 40)
                {
                    var teleMessage = (candle.CandleType == CandleType.Margin ? "✅ " : "") + $"{symbol}: {Math.Round(shortPercent, 2)}%, TP: {Math.Round(shortElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
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
                if (candle.Volume > 15000 && longPercent < -0.8M && longElastic >= 40)
                {
                    var teleMessage = $"💥 {symbol}: {Math.Round(longPercent, 2)}%, TP: {Math.Round(longElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    await _teleMessage.SendMessage(teleMessage);
                }
                if (candle.Volume > 15000 && shortPercent > 0.8M && shortElastic >= 40)
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
