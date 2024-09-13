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

        private DateTime _startTimeSpot = DateTime.Now;
        private DateTime _startTimePerp = DateTime.Now;
        private static List<string> _spotSymbols = new List<string>();
        private static List<string> _perpSymbols = new List<string>();
        private async Task RunRadar()
        {
            var spotSymbols = await GetSpotTradingSymbols();
            var perpSymbols = await GetPerpTradingSymbols();
            _spotSymbols = spotSymbols.Select(s => s.Name).ToList();
            _perpSymbols = perpSymbols.Select(s => s.Name).ToList();
            
            var batches = _spotSymbols.Select((x, i) => new { Index = i, Value = x })
                              .GroupBy(x => x.Index / 10)
                              .Select(x => x.Select(v => v.Value).ToList())
                              .ToList();
            
            var socketClient = new BybitSocketClient();
            long preTimestamp = 0;
            _spotCandles.Clear();
            foreach (var symbols in batches)
            {
                _ = socketClient.V5SpotApi.SubscribeToTradeUpdatesAsync(symbols, async data =>
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

            batches = _perpSymbols.Select((x, i) => new { Index = i, Value = x })
                              .GroupBy(x => x.Index / 10)
                              .Select(x => x.Select(v => v.Value).ToList())
                              .ToList();
            foreach (var symbols in batches)
            {
                _ = socketClient.V5LinearApi.SubscribeToTradeUpdatesAsync(symbols, async data =>
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
                var filterVol = candle.CandleType == CandleType.Margin ? 5000 : 1000;
                if (candle.Volume > filterVol && longPercent < -0.5M && longElastic >= 30)
                {                    
                    var teleMessage = (candle.CandleType == CandleType.Margin ? "✅ " : "") + $"{symbol}: {Math.Round(longPercent, 2)}%, TP: {Math.Round(longElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    Console.WriteLine(teleMessage);
                    await _teleMessage.SendMessage(teleMessage);
                }
                if (candle.Volume > filterVol && shortPercent > 0.5M && shortElastic >= 30 && candle.CandleType == CandleType.Margin)
                {
                    var teleMessage = $"✅ {symbol}: {Math.Round(shortPercent, 2)}%, TP: {Math.Round(shortElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    Console.WriteLine(teleMessage);
                    await _teleMessage.SendMessage(teleMessage);
                }
            }
            var currentTime = DateTime.Now;
            if((currentTime -  _startTimeSpot).TotalMinutes >= 5)
            {
                _startTimeSpot = currentTime;
                //5p get symbols again to check new listings
                var currentSymbols = await GetSpotTradingSymbols();
                if(currentSymbols.Count != _spotSymbols.Count)
                {
                    var newTokensAdded = currentSymbols.Select(x => x.Name).Except(_spotSymbols).ToList();
                    if (newTokensAdded.Any())
                    {
                        await _teleMessage.SendMessage($"👀 NEW TOKEN ADDED SPOT/MARGIN: {string.Join(",", newTokensAdded)}");
                        await Task.Delay(1000);
                        Environment.Exit(0);
                    }
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
                if (candle.Volume > 15000 && longPercent < -0.8M && longElastic >= 20)
                {
                    var teleMessage = $"💥 {symbol}: {Math.Round(longPercent, 2)}%, TP: {Math.Round(longElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    Console.WriteLine(teleMessage);
                    await _teleMessage.SendMessage(teleMessage);
                }
                if (candle.Volume > 15000 && shortPercent > 0.8M && shortElastic >= 20)
                {
                    var teleMessage = $"💥 {symbol}: {Math.Round(shortPercent, 2)}%, TP: {Math.Round(shortElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    Console.WriteLine(teleMessage);
                    await _teleMessage.SendMessage(teleMessage);
                }
            }

            var currentTime = DateTime.Now;
            if ((currentTime - _startTimePerp).TotalMinutes >= 5)
            {
                _startTimePerp = currentTime;
                //5p get symbols again to check new listings
                var currentSymbols = await GetPerpTradingSymbols();
                if (currentSymbols.Count != _perpSymbols.Count)
                {
                    var newTokensAdded = currentSymbols.Select(x=>x.Name).Except(_perpSymbols).ToList();
                    if (newTokensAdded.Any())
                    {
                        await _teleMessage.SendMessage($"👀 NEW TOKEN ADDED FOR FURTURE: {string.Join(",", newTokensAdded)}");
                        await Task.Delay(1000);
                        Environment.Exit(0);
                    }                    
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
