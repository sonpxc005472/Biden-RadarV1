using Biden.Radar.Common.Telegrams;
using Gate.IO.Api;
using Io.Gate.GateApi.Api;
using Io.Gate.GateApi.Client;
using Io.Gate.GateApi.Model;
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

        public async Task<List<CurrencyPair>> GetSpotTradingSymbols()
        {
            try
            {
                Configuration config = new Configuration();
                config.BasePath = "https://api.gateio.ws/api/v4";
                //config.SetGateApiV4KeyPair("YOUR_API_KEY", "YOUR_API_SECRET");

                var apiInstance = new SpotApi(config);
                //var publicApi = new GateRestApiClient();
                var spotSymbolsData = await apiInstance.ListCurrencyPairsAsync();
                var spotSymbols = spotSymbolsData.Where(s => s.Quote == "USDT").ToList();
                return spotSymbols;
            }
            catch
            {
                return new List<CurrencyPair>();
            }
        }
        
        public async Task<List<UniCurrencyPair>> GetMarginTradingSymbols()
        {
            try
            {
                Configuration config = new Configuration();
                config.BasePath = "https://api.gateio.ws/api/v4";
                var apiInstance = new MarginUniApi(config);
                var symbols = await apiInstance.ListUniCurrencyPairsAsync();
                return symbols;
            }
            catch
            {
                return new List<UniCurrencyPair>();
            }
        }

        public async Task<List<Contract>> GetPerpTradingSymbols()
        {
            try
            {
                Configuration config = new Configuration();
                config.BasePath = "https://api.gateio.ws/api/v4";
                var apiInstance = new FuturesApi(config);
                var symbols = await apiInstance.ListFuturesContractsAsync("USDT");
                return symbols;
            }
            catch
            {
                return new List<Contract>();
            }
        }

        private static ConcurrentDictionary<string, Candle> _candles = new ConcurrentDictionary<string, Candle>();
        private static ConcurrentDictionary<string, long> _candle1s = new ConcurrentDictionary<string, long>();


        private async Task RunRadar()
        {
            //var spotSymbols = await GetSpotTradingSymbols();
            //var marginSymbols = await GetMarginTradingSymbols();
            //var perpSymbols = await GetPerpTradingSymbols();
            //var spotSymbolNames = spotSymbols.Select(s => s.Symbol).ToList();
            //var marginSymbolNames = marginSymbols.Select(s => s.Symbol).ToList();

            //var marginBatches = marginSymbolNames.Select((x, i) => new { Index = i, Value = x })
            //                  .GroupBy(x => x.Index / 10)
            //                  .Select(x => x.Select(v => v.Value).ToList())
            //                  .ToList();

            //var socketClient = new GateStreamClient();
            //long preTimestamp = 0;
            //_candle1s.Clear();
            //_candles.Clear();
            //foreach (var symbols in marginBatches)
            //{
            //    var subResult = await socketClient.Spot.SubscribeToTradesAsync(symbols, async tradeData =>
            //    {
            //        if (tradeData != null)
            //        {
            //            var symbol = tradeData.Data.Symbol;
            //            var symbolType = CandleType.Margin;
                        
            //            long converttimestamp = (long)(tradeData.Timestamp.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            //            var timestamp = converttimestamp / 1000;
            //            var tick = new TickData
            //            {
            //                Timestamp = converttimestamp,
            //                Price = tradeData.Data.Price,
            //                Amount = tradeData.Data.Price * tradeData.Data.Amount
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
            //                await ProcessBufferedData();
            //            }
            //        }
            //    });
            //    if(subResult.Success)
            //    {

            //    }    
            //}

            await Task.CompletedTask;
        }

        private async Task ProcessBufferedData()
        {
            // Copy the current buffer for processing and clear the original buffer
            var dataToProcess = new ConcurrentDictionary<string, Candle>(_candles);
            _candles.Clear();
            _candle1s.Clear();

            foreach (var kvp in dataToProcess)
            {
                var symbol = kvp.Key;
                var candle = kvp.Value;

                var longPercent = (candle.Low - candle.Open) / candle.Open * 100;
                var shortPercent = (candle.High - candle.Open) / candle.Open * 100;
                var longElastic = longPercent == 0 ? 0 : (longPercent - ((candle.Close - candle.Open) / candle.Open * 100)) / longPercent * 100;
                var shortElastic = shortPercent == 0 ? 0 : (shortPercent - ((candle.Close - candle.Open) / candle.Open * 100)) / shortPercent * 100;
                if (candle.Volume > 5000 && ((longPercent < -0.8M && longPercent >= -1.2M && longElastic >= 50) || (longPercent < -1.2M && longElastic >= 40)))
                {                    
                    var teleMessage = (candle.CandleType == CandleType.Perp ? "💥 " : candle.CandleType == CandleType.Margin ? "✅ " : "") + $"{symbol}: {Math.Round(longPercent, 2)}%, TP: {Math.Round(longElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
                    await _teleMessage.SendMessage(teleMessage);
                }
                if (candle.Volume > 5000 && ((shortPercent > 0.8M && shortPercent <= 1.2M && shortElastic >= 50) || (shortPercent > 1.2M && shortElastic >= 40)))
                {
                    var teleMessage = (candle.CandleType == CandleType.Perp ? "💥 " : candle.CandleType == CandleType.Margin ? "✅ " : "") + $"{symbol}: {Math.Round(shortPercent, 2)}%, TP: {Math.Round(shortElastic, 2)}%, VOL: ${candle.Volume.FormatNumber()}";
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
