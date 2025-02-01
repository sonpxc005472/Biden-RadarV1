using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;

namespace Biden.Radar.BybitV3;

public static class SharedObjects
{
    public static List<BybitSpotSymbol> SpotSymbols = new List<BybitSpotSymbol>();
    public static List<BybitLinearInverseSymbol> PerpSymbols = new List<BybitLinearInverseSymbol>();
    public static BybitSocketClient WebsocketApiClient = new BybitSocketClient();
    public static BybitRestClient RestClient = new BybitRestClient();
    
    public static async Task<List<BybitSpotSymbol>> GetSpotTradingSymbols()
    {
        try
        {
            var spotSymbolsData = await RestClient.V5Api.ExchangeData.GetSpotSymbolsAsync();
            var spotSymbols = spotSymbolsData.Data.List.Where(s => s.QuoteAsset == "USDT").ToList();
            return spotSymbols;
        }
        catch
        {
            return new List<BybitSpotSymbol>();
        }
    }


    public static async Task<List<BybitLinearInverseSymbol>> GetPerpTradingSymbols()
    {
        try
        {
            var swapSymbolsData = await RestClient.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(Category.Linear, limit: 1000);
            var swapSymbols = swapSymbolsData.Data.List.Where(s => s.SettleAsset == "USDT").ToList();
            return swapSymbols;
        }
        catch
        {
            return new List<BybitLinearInverseSymbol>();
        }
    }
}