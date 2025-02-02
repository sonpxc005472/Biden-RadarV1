using Biden.Radar.Common;
using Biden.Radar.Common.Telegrams;
using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace Biden.Radar.BybitV3
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

        private static List<string> _perpSymbols = new();
        private async Task RunRadar()
        {
            SharedObjects.PerpSymbols = await SharedObjects.GetPerpTradingSymbols();
            _perpSymbols = SharedObjects.PerpSymbols.Select(s => s.Name).ToList();
            
            
            var socketClient = new BybitSocketClient();
            long preTimestamp = 0;

            var batches = _perpSymbols.Select((x, i) => new { Index = i, Value = x })
                              .GroupBy(x => x.Index / 10)
                              .Select(x => x.Select(v => v.Value).ToList())
                              .ToList();
            foreach (var symbols in batches)
            {
                _ = SharedObjects.WebsocketApiClient.V5LinearApi.SubscribeToKlineUpdatesAsync(symbols, KlineInterval.OneMinute, async data =>
                {
                    if (data != null)
                    {
                        var tradeDatas = data.Data;
                        var symbol = data.Symbol;
                        foreach (var tradeData in tradeDatas)
                        {
                            if (tradeData.Confirm)
                            {
                                var longPercent = (tradeData.LowPrice - tradeData.OpenPrice) / tradeData.OpenPrice * 100;
                                var shortPercent = (tradeData.HighPrice - tradeData.OpenPrice) / tradeData.OpenPrice * 100;
                                var longElastic = longPercent == 0 ? 0 : (longPercent - ((tradeData.ClosePrice - tradeData.OpenPrice) / tradeData.OpenPrice * 100)) / longPercent * 100;
                                var shortElastic = shortPercent == 0 ? 0 : (shortPercent - ((tradeData.ClosePrice - tradeData.OpenPrice) / tradeData.OpenPrice * 100)) / shortPercent * 100;
                                if (tradeData.Turnover > 40000 && longPercent < -1.2M && longElastic >= 25)
                                {
                                    var isVip = tradeData.Turnover >= 100000 && longElastic >= 50 && longPercent <= -1.5M;
                                    var teleMessage = $"(1m) 🔻 {symbol}: {Math.Round(longPercent, 2)}%, E: {Math.Round(longElastic, 2)}%, VOL: ${tradeData.Turnover.FormatNumber()}";
                                    if (isVip)
                                    {
                                        teleMessage = $"#vip {teleMessage}";
                                    }
                                    await _teleMessage.SendMessage(teleMessage);
                                }
                                if (tradeData.Turnover > 40000 && shortPercent > 1.2M && shortElastic >= 25)
                                {
                                    var isVip = tradeData.Turnover >= 100000 && shortElastic >= 50 && shortPercent >= 1.5M;
                                    var teleMessage = $"(1m) 🔺 {symbol}: {Math.Round(shortPercent, 2)}%, E: {Math.Round(shortElastic, 2)}%, VOL: ${tradeData.Turnover.FormatNumber()}";
                                    if (isVip)
                                    {
                                        teleMessage = $"#vip {teleMessage}";
                                    }
                                    await _teleMessage.SendMessage(teleMessage);
                                }
                            }
                        }
                    }
                });
                
                _ = SharedObjects.WebsocketApiClient.V5LinearApi.SubscribeToKlineUpdatesAsync(symbols, KlineInterval.FiveMinutes, async data =>
                {
                    if (data != null)
                    {
                        var tradeDatas = data.Data;
                        var symbol = data.Symbol;
                        foreach (var tradeData in tradeDatas)
                        {
                            if (tradeData.Confirm)
                            {
                                var longPercent = (tradeData.LowPrice - tradeData.OpenPrice) / tradeData.OpenPrice * 100;
                                var shortPercent = (tradeData.HighPrice - tradeData.OpenPrice) / tradeData.OpenPrice * 100;
                                var longElastic = longPercent == 0 ? 0 : (longPercent - ((tradeData.ClosePrice - tradeData.OpenPrice) / tradeData.OpenPrice * 100)) / longPercent * 100;
                                var shortElastic = shortPercent == 0 ? 0 : (shortPercent - ((tradeData.ClosePrice - tradeData.OpenPrice) / tradeData.OpenPrice * 100)) / shortPercent * 100;
                                if (tradeData.Turnover > 100000 && longPercent < -2M && longElastic >= 25)
                                {
                                    var isVip = tradeData.Turnover >= 200000 && longElastic >= 50 && longPercent <= -2.5M;
                                    var teleMessage = $"(5m) 🔻 {symbol}: {Math.Round(longPercent, 2)}%, E: {Math.Round(longElastic, 2)}%, VOL: ${tradeData.Turnover.FormatNumber()}";
                                    if (isVip)
                                    {
                                        teleMessage = $"#vip {teleMessage}";
                                    }
                                    await _teleMessage.SendMessage(teleMessage);
                                }
                                if (tradeData.Turnover > 100000 && shortPercent > 2M && shortElastic >= 25)
                                {
                                    var isVip = tradeData.Turnover >= 200000 && shortElastic >= 50 && shortPercent >= 2.5M;
                                    var teleMessage = $"(5m) 🔺 {symbol}: {Math.Round(shortPercent, 2)}%, E: {Math.Round(shortElastic, 2)}%, VOL: ${tradeData.Turnover.FormatNumber()}";
                                    if (isVip)
                                    {
                                        teleMessage = $"#vip {teleMessage}";
                                    }
                                    await _teleMessage.SendMessage(teleMessage);
                                }
                            }
                        }
                    }
                });
            }
        }
    }
}
