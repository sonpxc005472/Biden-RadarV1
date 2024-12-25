using Biden.Radar.Common;
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
            // Set the timer to trigger after 5 days
            TimeSpan delay = TimeSpan.FromDays(5);
            Timer timer = new Timer(ExecuteJob, null, delay, Timeout.InfiniteTimeSpan);
            await RunRadar();
        }
        private void ExecuteJob(object? state)
        {
            Environment.Exit(0);
        }

        private static ConcurrentDictionary<string, Candle> _spotCandles = new();
        private static ConcurrentDictionary<string, Candle> _perpCandles = new();

        private static List<string> _spotSymbols = new();
        private static List<string> _perpSymbols = new();
        private async Task RunRadar()
        {
            SharedObjects.SpotSymbols = await SharedObjects.GetSpotTradingSymbols();
            SharedObjects.PerpSymbols = await SharedObjects.GetPerpTradingSymbols();
            _spotSymbols = SharedObjects.SpotSymbols.Select(s => s.Name).ToList();
            _perpSymbols = SharedObjects.PerpSymbols.Select(s => s.Name).ToList();
            
            var batches = _spotSymbols.Select((x, i) => new { Index = i, Value = x })
                              .GroupBy(x => x.Index / 10)
                              .Select(x => x.Select(v => v.Value).ToList())
                              .ToList();
            
            var socketClient = new BybitSocketClient();
            long preTimestamp = 0;
            _spotCandles.Clear();
            foreach (var symbols in batches)
            {
                _ = SharedObjects.WebsocketApiClient.V5SpotApi.SubscribeToTradeUpdatesAsync(symbols, async data =>
                {
                    if (data != null)
                    {
                        var tradeDatas = data.Data;
                        foreach (var tradeData in tradeDatas)
                        {
                            var symbol = tradeData.Symbol;
                            var symbolType = CandleType.Spot;
                            var symbolInfo = SharedObjects.SpotSymbols.FirstOrDefault(x => x.Name == symbol);
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

            batches = _perpSymbols.Select((x, i) => new { Index = i, Value = x })
                              .GroupBy(x => x.Index / 10)
                              .Select(x => x.Select(v => v.Value).ToList())
                              .ToList();
            foreach (var symbols in batches)
            {
                _ = SharedObjects.WebsocketApiClient.V5LinearApi.SubscribeToTradeUpdatesAsync(symbols, async data =>
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
                var filterVol = candle.CandleType == CandleType.Margin ? 5000 : 50000;
                if (candle.Volume > filterVol && longPercent < -0.3M && longElastic >= 30)
                {
                    var isVip = candle.Volume >= 50000 && longElastic >= 70 && (candle.CandleType == CandleType.Margin || longPercent <= -2);
                    var teleMessage = (candle.CandleType == CandleType.Margin ? "✅🔻 " : "") + $"{symbol}: {Math.Round(longPercent, 2)}%, E: {Math.Round(longElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    if (isVip)
                    {
                        teleMessage = $"#vip {teleMessage}";
                    }
                    Console.WriteLine(teleMessage);
                    await _teleMessage.SendMessage(teleMessage);
                }
                if (candle.Volume > filterVol && shortPercent > 0.3M && shortElastic >= 30 && candle.CandleType == CandleType.Margin)
                {
                    var isVip = candle.Volume >= 50000 && shortElastic >= 70 && (candle.CandleType == CandleType.Margin || shortPercent >= 2);
                    var teleMessage = $"✅🔺 {symbol}: {Math.Round(shortPercent, 2)}%, E: {Math.Round(shortElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    if (isVip)
                    {
                        teleMessage = $"#vip {teleMessage}";
                    }
                    Console.WriteLine(teleMessage);
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
                if (candle.Volume > 40000 && longPercent < -0.8M && longElastic >= 25)
                {
                    var isVip = candle.Volume >= 100000 && longElastic >= 50 && longPercent <= -1.5M;
                    var teleMessage = $"💥🔻 {symbol}: {Math.Round(longPercent, 2)}%, E: {Math.Round(longElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    if (isVip)
                    {
                        teleMessage = $"#vip {teleMessage}";
                    }
                    Console.WriteLine(teleMessage);
                    await _teleMessage.SendMessage(teleMessage);
                }
                if (candle.Volume > 40000 && shortPercent > 0.8M && shortElastic >= 25)
                {
                    var isVip = candle.Volume >= 100000 && shortElastic >= 50 && shortPercent >= 1.5M;
                    var teleMessage = $"💥🔺 {symbol}: {Math.Round(shortPercent, 2)}%, E: {Math.Round(shortElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    if (isVip)
                    {
                        teleMessage = $"#vip {teleMessage}";
                    }
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
}
