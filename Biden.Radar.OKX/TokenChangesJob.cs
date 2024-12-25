using Biden.Radar.Common.Telegrams;
using OKX.Api.Common;
using Quartz;

namespace Biden.Radar.OKX;

public class TokenChangesJob : IJob
{
    private readonly ITeleMessage _teleMessage;

    public TokenChangesJob(ITeleMessage teleMessage)
    {
        _teleMessage = teleMessage;
    }
    public async Task Execute(IJobExecutionContext context)
    {
        var currentSymbols = await SharedObjects.GetTradingSymbols();
        if (currentSymbols.Count != SharedObjects.TradingSymbols.Count)
        {
            var newTokensAdded = currentSymbols.Select(x => x.InstrumentId).Except(SharedObjects.TradingSymbols.Select(s => s.InstrumentId)).ToList();
            if (newTokensAdded.Any())
            {
                await _teleMessage.SendMessage($"👀 NEW TOKEN ADDED: {string.Join(",", newTokensAdded)}");
                await Task.Delay(1000);
                Environment.Exit(0);
            }
            else
            {
                var newMarginTokensAdded = currentSymbols.Where(x=> x.InstrumentType == OkxInstrumentType.Margin).Select(x => x.InstrumentId).Except(SharedObjects.TradingSymbols.Where(x=> x.InstrumentType == OkxInstrumentType.Margin).Select(s => s.InstrumentId)).ToList();
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