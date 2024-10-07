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
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return new List<OkxInstrument>();
            }
        }

        private static List<OkxInstrument> _tradingSymbols = new List<OkxInstrument>();
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

            _ = _websocketApiClient.Public.SubscribeToInstrumentsAsync(async data =>
            {
                if (data != null)
                {
                    if (data.QuoteCurrency == "USDT" && data.State == OkxInstrumentState.Live)
                    {
                        if (!_tradingSymbols.Any(x => x.InstrumentType == OkxInstrumentType.Spot && x.InstrumentId == data.InstrumentId))
                        {
                            _tradingSymbols.Add(data);
                            await _teleMessage.SendMessage($"👀 NEW TOKEN ADDED FOR SPOT: {data.InstrumentId}");
                            SubscribeSymbol(data.InstrumentId);
                        }
                    }
                }
            }, OkxInstrumentType.Spot);

            _ = _websocketApiClient.Public.SubscribeToInstrumentsAsync(async data =>
            {
                if (data != null)
                {
                    if (data.QuoteCurrency == "USDT" && data.State == OkxInstrumentState.Live)
                    {
                        if (!_tradingSymbols.Any(x => x.InstrumentType == OkxInstrumentType.Margin && x.InstrumentId == data.InstrumentId))
                        {
                            _tradingSymbols.Add(data);
                            await _teleMessage.SendMessage($"👀 NEW TOKEN ADDED FOR MARGIN: {data.InstrumentId}");
                            SubscribeSymbol(data.InstrumentId);
                        }
                    }
                }
            }, OkxInstrumentType.Margin);

            _ = _websocketApiClient.Public.SubscribeToInstrumentsAsync(async data =>
            {
                if (data != null)
                {
                    if (data.SettlementCurrency == "USDT" && data.State == OkxInstrumentState.Live)
                    {
                        if (!_tradingSymbols.Any(x => x.InstrumentType == OkxInstrumentType.Swap && x.InstrumentId == data.InstrumentId))
                        {
                            _tradingSymbols.Add(data);
                            await _teleMessage.SendMessage($"👀 NEW TOKEN ADDED FOR SWAP: {data.InstrumentId}");
                            SubscribeSymbol(data.InstrumentId);
                        }
                    }
                }
            }, OkxInstrumentType.Swap);
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
                        
                        var filterVol = isPerp ? 10000 : isMargin ? 3000 : 500;
                        var filterTP = isPerp ? 0.8M : isMargin ? 0.5M : 1M;
                        var vipVol = isPerp ? 600000 : isMargin ? 80000 : 20000;
                        var vipElastic = isPerp ? 50 : isMargin ? 70 : 80;
                        if (tradeData.QuoteVolume > filterVol && longPercent < -filterTP && longElastic >= 20)
                        {
                            var isVip = tradeData.QuoteVolume >= vipVol && longElastic >= vipElastic;
                            var teleMessage = (isPerp ? "💥 " : isMargin ? "✅ " : "") + $"{symbol}: {Math.Round(longPercent, 2)}%, TP: {Math.Round(longElastic, 2)}%, VOL: ${tradeData.QuoteVolume.FormatNumber()}";
                            if(isVip)
                            {
                                teleMessage = $"🥇 {teleMessage}";
                            }    
                            await _teleMessage.SendMessage(teleMessage);
                        }
                        if (tradeData.QuoteVolume > filterVol && shortPercent > filterTP && shortElastic >= 20 && (isPerp || isMargin))
                        {
                            var isVip = tradeData.QuoteVolume >= vipVol && shortElastic >= vipElastic;
                            var teleMessage = (isPerp ? "💥 " : isMargin ? "✅ " : "") + $"{symbol}: {Math.Round(shortPercent, 2)}%, TP: {Math.Round(shortElastic, 2)}%, VOL: ${tradeData.QuoteVolume.FormatNumber()}";
                            if (isVip)
                            {
                                teleMessage = $"🥇 {teleMessage}";
                            }
                            await _teleMessage.SendMessage(teleMessage);
                        }
                    }
                }
            }, symbol, OkxPeriod.OneSecond);
        }
    }
}
