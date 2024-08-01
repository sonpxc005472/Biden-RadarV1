using Biden.Radar.Common.Telegrams;
using Bitget.Net.Clients;
using Bitget.Net.Enums;
using Bitget.Net.Objects.Models.V2;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace Biden.Radar.Bitget
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

        public async Task<List<BitgetSymbol>> GetSpotTradingSymbols()
        {
            try
            {
                var publicApi = new BitgetRestClient();
                var spotSymbolsData = await publicApi.SpotApiV2.ExchangeData.GetSymbolsAsync();
                var spotSymbols = spotSymbolsData.Data.Where(s => s.QuoteAsset == "USDT").ToList();
                return spotSymbols;
            }
            catch
            {
                return new List<BitgetSymbol>();
            }
        }


        public async Task<List<BitgetContract>> GetPerpTradingSymbols()
        {
            try
            {
                var publicApi = new BitgetRestClient();                
                var swapSymbolsData = await publicApi.FuturesApiV2.ExchangeData.GetContractsAsync(BitgetProductTypeV2.UsdtFutures);
                var swapSymbols = swapSymbolsData.Data.Where(s => s.QuoteAsset == "USDT").ToList();
                return swapSymbols;
            }
            catch
            {
                return new List<BitgetContract>();
            }
        }

        private static ConcurrentDictionary<string, Candle> _candles = new ConcurrentDictionary<string, Candle>();
        private static ConcurrentDictionary<string, long> _candle1s = new ConcurrentDictionary<string, long>();


        private async Task RunRadar()
        {
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
