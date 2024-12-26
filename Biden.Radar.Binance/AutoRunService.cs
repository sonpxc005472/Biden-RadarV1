using Biden.Radar.Common;
using Biden.Radar.Common.Telegrams;
using Binance.Net.Enums;
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
            // Set the timer to trigger after 5 days
            TimeSpan delay = TimeSpan.FromDays(5);
            Timer timer = new Timer(ExecuteJob, null, delay, Timeout.InfiniteTimeSpan);
            await RunRadar();
        }
        private void ExecuteJob(object? state)
        {
            Environment.Exit(0);
        }

        private static ConcurrentDictionary<string, Candle> _perpCandles = new();

        private async Task RunRadar()
        {
            SharedObjects.PerpSymbols = await SharedObjects.GetPerpTradingSymbols();
            SharedObjects.SpotSymbols = await SharedObjects.GetSpotTradingSymbols();
            var perpSymbolNames = SharedObjects.PerpSymbols.Select(s => s.Name).ToList();
            var spotSymbolNames = SharedObjects.SpotSymbols.Select(s => s.Name).ToList();
            long preTimestamp = 0;

            var spotBatches = spotSymbolNames.Select((x, i) => new { Index = i, Value = x })
                              .GroupBy(x => x.Index / 100)
                              .Select(x => x.Select(v => v.Value).ToList())
                              .ToList();

            foreach (var symbols in spotBatches)
            {
                var rs = await SharedObjects.WebsocketApiClient.SpotApi.ExchangeData.SubscribeToKlineUpdatesAsync(symbols, KlineInterval.OneSecond, async data =>
                {
                    var tradeData = data.Data?.Data;
                    if (tradeData != null)
                    {
                        var symbol = data.Symbol;

                        if (tradeData.Final)
                        {
                            var longPercent = (tradeData.LowPrice - tradeData.OpenPrice) / tradeData.OpenPrice * 100;
                            var shortPercent = (tradeData.HighPrice - tradeData.OpenPrice) / tradeData.OpenPrice * 100;
                            var longElastic = longPercent == 0 ? 0 : (longPercent - ((tradeData.ClosePrice - tradeData.OpenPrice) / tradeData.OpenPrice * 100)) / longPercent * 100;
                            var shortElastic = shortPercent == 0 ? 0 : (shortPercent - ((tradeData.ClosePrice - tradeData.OpenPrice) / tradeData.OpenPrice * 100)) / shortPercent * 100;
                            var instruments = SharedObjects.SpotSymbols.Where(r => r.Name == symbol);
                            var isMargin = instruments.Any(r => r.IsMarginTradingAllowed);

                            var filterVol = isMargin ? 20000 : 10000;
                            var filterTP = isMargin ? 0.3M : 1M;
                            var vipVol = isMargin ? 100000 : 30000;
                            var vipElastic = isMargin ? 65 : 70;
                            
                            if (tradeData.QuoteVolume > filterVol && longPercent < -filterTP && longElastic >= 30)
                            {
                                var isVip = tradeData.QuoteVolume >= vipVol && longElastic >= vipElastic;
                                var teleMessage = (isMargin ? "✅🔻 " : "") + $"{symbol}: {Math.Round(longPercent, 2)}%, E: {Math.Round(longElastic, 2)}%, VOL: ${tradeData.QuoteVolume.FormatNumber()}";
                                if(isVip)
                                {
                                    teleMessage = $"#vip {teleMessage}";
                                }
                                await _teleMessage.SendMessage(teleMessage);
                            }
                            if (tradeData.QuoteVolume > filterVol && shortPercent > filterTP && shortElastic >= 30 && isMargin)
                            {
                                var isVip = tradeData.QuoteVolume >= vipVol && shortElastic >= vipElastic;

                                var teleMessage = (isMargin ? "✅🔺 " : "") + $"{symbol}: {Math.Round(shortPercent, 2)}%, E: {Math.Round(shortElastic, 2)}%, VOL: ${tradeData.QuoteVolume.FormatNumber()}";
                                if(isVip)
                                {
                                    teleMessage = $"#vip {teleMessage}";
                                }
                                await _teleMessage.SendMessage(teleMessage);
                            }                            
                        }
                    }
                });
            }

            var batches = perpSymbolNames.Select((x, i) => new { Index = i, Value = x })
                              .GroupBy(x => x.Index / 50)
                              .Select(x => x.Select(v => v.Value).ToList())
                              .ToList();
            
            foreach (var symbols in batches)
            {
                _ = SharedObjects.WebsocketApiClient.UsdFuturesApi.SubscribeToTradeUpdatesAsync(symbols, async data =>
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
                if (candle.Volume > 15000 && longPercent < -1 && longElastic >= 25)
                {
                    var isVip = candle.Volume >= 500000 && longElastic >= 60;
                    var teleMessage = $"💥 {symbol}: {Math.Round(longPercent, 2)}%, E: {Math.Round(longElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    if(isVip)
                    {
                        teleMessage = $"#vip {teleMessage}";
                    }
                    Console.WriteLine(teleMessage);
                    await _teleMessage.SendMessage(teleMessage);
                }
                if (candle.Volume > 15000 && shortPercent > 1 && shortElastic >= 25)
                {
                    var isVip = candle.Volume >= 500000 && shortElastic >= 60;
                    var teleMessage = $"💥 {symbol}: {Math.Round(shortPercent, 2)}%, E: {Math.Round(shortElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    if(isVip)
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
