
using OKX.Api;
using OKX.Api.Common;
using OKX.Api.Public;

namespace Biden.Radar.OKX;

public static class SharedObjects
{
    public static List<OkxPublicInstrument> TradingSymbols = new();
    public static OKXWebSocketApiClient WebsocketApiClient = new();
    public static OkxRestApiClient RestClient = new();
    
    public static async Task<List<OkxPublicInstrument>> GetTradingSymbols()
    {
        try
        {
            var spotSymbolsData = await RestClient.Public.GetInstrumentsAsync(OkxInstrumentType.Spot);
            var spotSymbols = spotSymbolsData.Data.Where(s => s.State == OkxInstrumentState.Live && s.QuoteCurrency == "USDT").ToList();
            var marginSymbolsData = await RestClient.Public.GetInstrumentsAsync(OkxInstrumentType.Margin);
            var marginSymbols = marginSymbolsData.Data.Where(s => s.State == OkxInstrumentState.Live && s.QuoteCurrency == "USDT").ToList();
            var swapSymbolsData = await RestClient.Public.GetInstrumentsAsync(OkxInstrumentType.Swap);
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
}