using Biden.Radar.Common.Telegrams;
using Microsoft.Extensions.Hosting;
using OKX.Api.Common;

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
            foreach (var symbol in totalsymbols)
            {
                SubscribeSymbol(symbol);
            }
        }


        private void SubscribeSymbol(string symbol)
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
                        var filterTP = isPerp ? 0.8M : isMargin ? 0.3M : 1M;
                        var vipVol = isPerp ? 400000 : isMargin ? 60000 : 20000;
                        var vipElastic = isPerp ? 50 : isMargin ? 65 : 75;
                        if (tradeData.TradingVolume > filterVol && longPercent < -filterTP && longElastic >= 20)
                        {
                            var isVip = tradeData.TradingVolume >= vipVol && longElastic >= vipElastic;
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
                            var teleMessage = (isPerp ? "💥🔺 " : isMargin ? "✅🔺 " : "") + $"{symbol}: {Math.Round(shortPercent, 2)}%, E: {Math.Round(shortElastic, 2)}%, VOL: ${tradeData.TradingVolume.FormatNumber()}";
                            if (isVip)
                            {
                                teleMessage = $"#vip {teleMessage}";
                            }
                            await _teleMessage.SendMessage(teleMessage);
                        }
                    }
                }
            }, symbol, OkxPeriod.OneSecond);
        }
    }
}
