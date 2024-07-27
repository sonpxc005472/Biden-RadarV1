using Biden.Radar.Common.Telegrams;
using Microsoft.Extensions.Hosting;
using OKX.Api;
using OKX.Api.Common.Enums;
using OKX.Api.Public.Enums;
using OKX.Api.Public.Models;

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

        public async Task<List<OkxInstrument>> GetTradingSymbols()
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
            catch
            {
                return new List<OkxInstrument>();
            }
        }

        private async Task RunRadar()
        {
            var tradingSymbols = await GetTradingSymbols();
            var totalsymbols = tradingSymbols.Select(c => c.InstrumentId).Distinct().ToList();
            var okxSocketClient = new OKXWebSocketApiClient();
            foreach (var symbol in totalsymbols)
            {
                var subResult = await okxSocketClient.Public.SubscribeToCandlesticksAsync(tradeData =>
                {
                    if (tradeData != null)
                    {
                        var symbol = tradeData.InstrumentId;
                        long converttimestamp = (long)(tradeData.Time.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                        var timestamp = tradeData.Timestamp / 1000;
                        //Console.WriteLine($"Time: {timestamp}, Confirm: {tradeData.Confirm}");
                        
                        if (tradeData.Confirm)
                        {
                            var longPercent = (tradeData.Low - tradeData.Open) / tradeData.Open * 100;
                            var shortPercent = (tradeData.High - tradeData.Open) / tradeData.Open * 100;
                            var longElastic = longPercent == 0 ? 0 : (longPercent - ((tradeData.Close - tradeData.Open) / tradeData.Open * 100)) / longPercent * 100;
                            var shortElastic = shortPercent == 0 ? 0 : (shortPercent - ((tradeData.Close - tradeData.Open) / tradeData.Open * 100)) / shortPercent * 100;
                            var instruments = tradingSymbols.Where(r => r.InstrumentId == symbol);
                            var isPerp = instruments.Any(r => r.InstrumentType == OkxInstrumentType.Swap);
                            var isMargin = instruments.Any(r => r.InstrumentType == OkxInstrumentType.Margin);

                            var filterVol = isPerp ? 20000 : isMargin ? 10000 : 5000;

                            if (tradeData.QuoteVolume > filterVol && longPercent < -0.8M && longElastic >= 40)
                            {
                                var teleMessage = (isPerp ? "💥 " : isMargin ? "✅ " : "") + $"{symbol}: {Math.Round(longPercent,2)}%, TP: {Math.Round(longElastic, 2)}%, VOL: ${tradeData.QuoteVolume.FormatNumber()}";
                                _teleMessage.SendMessage(teleMessage);
                            }
                            if (tradeData.QuoteVolume > filterVol && shortPercent > 0.8M && shortElastic >= 40)
                            {
                                var teleMessage = (isPerp ? "💥 " : isMargin ? "✅ " : "") + $"{symbol}: {Math.Round(shortPercent, 2)}%, TP: {Math.Round(shortElastic, 2)}%, VOL: ${tradeData.QuoteVolume.FormatNumber()}";
                                _teleMessage.SendMessage(teleMessage);
                            }
                        }
                        
                    }
                }, symbol, OkxPeriod.OneSecond);
            }

        }

    }
}
