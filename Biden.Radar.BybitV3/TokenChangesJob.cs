using Biden.Radar.Common.Telegrams;
using Bybit.Net.Enums;
using Quartz;

namespace Biden.Radar.BybitV3;

public class TokenChangesJob : IJob
{
    private readonly ITeleMessage _teleMessage;

    public TokenChangesJob(ITeleMessage teleMessage)
    {
        _teleMessage = teleMessage;
    }
    public async Task Execute(IJobExecutionContext context)
    {
        var currentPerpSymbols = await SharedObjects.GetPerpTradingSymbols();
        if (currentPerpSymbols.Count != SharedObjects.PerpSymbols.Count)
        {
            var newTokensAdded = currentPerpSymbols.Select(x => x.Name).Except(SharedObjects.PerpSymbols.Select(s => s.Name)).ToList();
            if (newTokensAdded.Any())
            {
                await _teleMessage.SendMessage($"👀 NEW DERIVATIVES TOKEN ADDED: {string.Join(",", newTokensAdded)}");
                await Task.Delay(1000);
                Environment.Exit(0);
            }
        }
    }
}