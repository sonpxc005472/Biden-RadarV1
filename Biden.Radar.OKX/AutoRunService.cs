using Biden.Radar.Common.Telegrams;
using Microsoft.Extensions.Hosting;
using OKX.Api;
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
            // Set the timer to trigger after 5 mins
            TimeSpan delay = TimeSpan.FromMinutes(5);
            var symbolCheck = new System.Timers.Timer(delay.TotalMilliseconds);
            symbolCheck.Elapsed += (source, e) =>
            {                
                ExecuteCheckingJob();
            };
            await RunRadar();
            await Task.Delay(30000);
            symbolCheck.AutoReset = true;
            symbolCheck.Enabled = true;
        }        

        private async void ExecuteCheckingJob()
        {
            var currentSymbols = await GetTradingSymbols();

            if (currentSymbols.Count != _tradingSymbols.Count)
            {
                var oldSymbols = _tradingSymbols.Select(x => x.InstrumentId).ToList();
                var newTokensAdded = currentSymbols.Where(x => !oldSymbols.Any(o => o == x.InstrumentId)).Select(x=>x.InstrumentId).ToList();
                if (newTokensAdded.Any())
                {
                    await _teleMessage.SendMessage($"👀 NEW TOKEN ADDED: {string.Join(",", newTokensAdded)}");
                    await Task.Delay(1000);
                    Environment.Exit(0);
                }
            }
        }

        public async Task<List<OkxPublicInstrument>> GetTradingSymbols()
        {
            try
            {
                var publicApi = new OkxRestApiClient();
                var spotSymbolsData = await publicApi.Public.GetInstrumentsAsync(OkxInstrumentType.Spot);
                var spotSymbols = spotSymbolsData.Data.Where(s => s.State == OkxInstrumentState.Live && s.QuoteCurrency == "USDT").ToList();
                var marginSymbolsData = await publicApi.Public.GetInstrumentsAsync(OkxInstrumentType.Margin);
                var marginSymbols = marginSymbolsData.Data.Where(s => s.State == OkxInstrumentState.Live && s.QuoteCurrency == "USDT").ToList();
                var swapSymbolsData = await publicApi.Public.GetInstrumentsAsync(OkxInstrumentType.Swap);
                var swapSymbols = swapSymbolsData.Data.Where(s => s.State == OkxInstrumentState.Live && s.SettlementCurrency == "USDT").ToList();
                var symbolInfos = spotSymbols.Concat(marginSymbols).Concat(swapSymbols);
                return symbolInfos.ToList();
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                throw;
            }
        }

        private static List<OkxPublicInstrument> _tradingSymbols = new List<OkxPublicInstrument>();
        private static OKXWebSocketApiClient _websocketApiClient = new OKXWebSocketApiClient();

        private async Task RunRadar()
        {
            _tradingSymbols = await GetTradingSymbols();
            var totalsymbols = _tradingSymbols.Select(c => c.InstrumentId).Distinct().ToList();
            Console.WriteLine($"Total symbol to scan: {totalsymbols.Count}");
            foreach (var symbol in totalsymbols)
            {
                SubscribeSymbol(symbol);
            }
        }


        private void SubscribeSymbol(string symbol)
        {
            _ = _websocketApiClient.Public.SubscribeToCandlesticksAsync(async tradeData =>
            {
                if (tradeData != null)
                {
                    var symbol = tradeData.InstrumentId;
                    var instruments = _tradingSymbols.Where(r => r.InstrumentId == symbol);
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
                        var vipVol = isPerp ? 600000 : isMargin ? 80000 : 20000;
                        var vipElastic = isPerp ? 50 : isMargin ? 70 : 80;
                        if (tradeData.TradingVolume > filterVol && longPercent < -filterTP && longElastic >= 20)
                        {
                            var isVip = tradeData.TradingVolume >= vipVol && longElastic >= vipElastic;
                            var teleMessage = (isPerp ? "💥🔻 " : isMargin ? "✅🔻 " : "") + $"{symbol}: {Math.Round(longPercent, 2)}%, TP: {Math.Round(longElastic, 2)}%, VOL: ${tradeData.TradingVolume.FormatNumber()}";
                            if(isVip)
                            {
                                teleMessage = $"🥇 {teleMessage} #vip";
                            }    
                            await _teleMessage.SendMessage(teleMessage);
                        }
                        if (tradeData.TradingVolume > filterVol && shortPercent > filterTP && shortElastic >= 20 && (isPerp || isMargin))
                        {
                            var isVip = tradeData.TradingVolume >= vipVol && shortElastic >= vipElastic;
                            var teleMessage = (isPerp ? "💥🔺 " : isMargin ? "✅🔺 " : "") + $"{symbol}: {Math.Round(shortPercent, 2)}%, TP: {Math.Round(shortElastic, 2)}%, VOL: ${tradeData.TradingVolume.FormatNumber()}";
                            if (isVip)
                            {
                                teleMessage = $"🥇 {teleMessage} #vip";
                            }
                            await _teleMessage.SendMessage(teleMessage);
                        }
                    }
                }
            }, symbol, OkxPeriod.OneSecond);
        }
    }
}
