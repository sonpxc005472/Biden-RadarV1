using Biden.Radar.Common.Telegrams;
using Quartz;

namespace Biden.Radar.Binance;

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
        
        var currentSpotSymbols = await SharedObjects.GetSpotTradingSymbols();
        if (currentSpotSymbols.Count != SharedObjects.SpotSymbols.Count)
        {
            var newTokensAdded = currentSpotSymbols.Select(x => x.Name).Except(SharedObjects.SpotSymbols.Select(s => s.Name)).ToList();
            if (newTokensAdded.Any())
            {
                await _teleMessage.SendMessage($"👀 NEW SPOT/MARGIN TOKEN ADDED: {string.Join(",", newTokensAdded)}");
                await Task.Delay(1000);
                Environment.Exit(0);
            }
            else
            {
                var newMarginTokensAdded = currentSpotSymbols.Where(x=> x.IsMarginTradingAllowed).Select(x => x.Name).Except(SharedObjects.SpotSymbols.Where(x=> x.IsMarginTradingAllowed).Select(s => s.Name)).ToList();
                if (newMarginTokensAdded.Any())
                {
                    await _teleMessage.SendMessage($"👀 NEW MARGIN TOKEN ADDED: {string.Join(",", newMarginTokensAdded)}");
                    await Task.Delay(1000);
                    Environment.Exit(0);
                }
            }
        }
    }
}