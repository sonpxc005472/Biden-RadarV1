using System.Collections.Concurrent;
using Biden.Radar.Common.Telegrams;
using Microsoft.Extensions.Hosting;
using OKX.Api.Common;
using OKX.Api.Public;

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
        
        private async Task RunRadar()
        {
            SharedObjects.TradingSymbols = await SharedObjects.GetTradingSymbols();
            var totalsymbols = SharedObjects.TradingSymbols.Select(c => c.InstrumentId).Distinct().ToList();
            Console.WriteLine($"Total symbol to scan: {totalsymbols.Count}");
            SubscribeSymbol(totalsymbols);
            //SubscribeOrderBook(totalsymbols);
        }

        private static ConcurrentDictionary<decimal, decimal> bidOrders = new();
        private static ConcurrentDictionary<decimal, decimal> askOrders = new();

        private void SubscribeSymbol(List<string> symbols)
        {
            _ = SharedObjects.WebsocketApiClient.Public.SubscribeToCandlesticksAsync(async tradeData =>
            {
                if (tradeData != null)
                {
                    var symbol = tradeData.InstrumentId;
                    var instruments = SharedObjects.TradingSymbols.Where(r => r.InstrumentId == symbol);
                    var isPerp = instruments.Any(r => r.InstrumentType == OkxInstrumentType.Swap);
                    var isMargin = instruments.Any(r => r.InstrumentType == OkxInstrumentType.Margin);
                    if (tradeData.Confirm)
                    {
                        var longPercent = (tradeData.Low - tradeData.Open) / tradeData.Open * 100;
                        var shortPercent = (tradeData.High - tradeData.Open) / tradeData.Open * 100;
                        var longElastic = longPercent == 0 ? 0 : (longPercent - ((tradeData.Close - tradeData.Open) / tradeData.Open * 100)) / longPercent * 100;
                        var shortElastic = shortPercent == 0 ? 0 : (shortPercent - ((tradeData.Close - tradeData.Open) / tradeData.Open * 100)) / shortPercent * 100;
                        
                        var filterVol = isPerp ? 20000 : isMargin ? 5000 : 800;
                        var filterTP = isPerp ? 0.4M : isMargin ? 0.3M : 1M;
                        var vipVol = isPerp ? 400000 : isMargin ? 60000 : 20000;
                        var vipElastic = isPerp ? 50 : isMargin ? 65 : 75;
                        if (tradeData.TradingVolume > filterVol && longPercent < -filterTP && longElastic >= 20)
                        {
                            var isVip = tradeData.TradingVolume >= vipVol && longElastic >= vipElastic;
                            if (isPerp && longPercent > -0.7M && !isVip)
                            {
                                return;
                            }
                            var teleMessage = (isPerp ? "💥🔻 " : isMargin ? "✅🔻 " : "") + $"{symbol}: {Math.Round(longPercent, 2)}%, E: {Math.Round(longElastic, 2)}%, VOL: ${tradeData.TradingVolume.FormatNumber()}";
                            if(isVip)
                            {
                                teleMessage = $"#vip {teleMessage}";
                            }    
                            await _teleMessage.SendMessage(teleMessage);
                        }
                        if (tradeData.TradingVolume > filterVol && shortPercent > filterTP && shortElastic >= 20 && (isPerp || isMargin))
                        {
                            var isVip = tradeData.TradingVolume >= vipVol && shortElastic >= vipElastic;
                            if (isPerp && shortPercent < 0.7M && !isVip)
                            {
                                return;
                            }
                            var teleMessage = (isPerp ? "💥🔺 " : isMargin ? "✅🔺 " : "") + $"{symbol}: {Math.Round(shortPercent, 2)}%, E: {Math.Round(shortElastic, 2)}%, VOL: ${tradeData.TradingVolume.FormatNumber()}";
                            if (isVip)
                            {
                                teleMessage = $"#vip {teleMessage}";
                            }
                            await _teleMessage.SendMessage(teleMessage);
                        }
                    }
                }
            }, symbols, OkxPeriod.OneSecond);
        }

        private void SubscribeOrderBook(List<string> symbols)
        {
           var rss = SharedObjects.WebsocketApiClient.Public.SubscribeToOrderBookAsync(async data =>
            {
                var symbol = data.InstrumentId;
                var asks = data.Asks;
                var bids = data.Bids;
                await AnalyzeOrderBook(symbol, asks, bids);
            }, symbols, OkxOrderBookType.OrderBook);
        }
        
        private async Task AnalyzeOrderBook(string symbol, List<OkxPublicOrderBookRow> orderBookAsk, List<OkxPublicOrderBookRow> orderBookBid)
        {
            decimal totalBidVolume = orderBookBid.Sum(x=>x.Quantity);
            decimal totalAskVolume = orderBookAsk.Sum(x=>x.Quantity);

            decimal imbalance = totalBidVolume - totalAskVolume;
            decimal ocEstimate = Math.Abs(imbalance) / (totalBidVolume + totalAskVolume) * 100;
            
            if (ocEstimate > 50) // Ngưỡng có thể chỉnh
            {
                Console.WriteLine($"⚠️ Cảnh báo: {symbol} Có thể sắp có râu nến giật mạnh!");
               // await _teleMessage.SendMessage($"⚠️ Cảnh báo: {symbol} Có thể sắp có râu nến giật mạnh!");
            }
        }
    }
}
