using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Objects.Models.Futures;
using Binance.Net.Objects.Models.Spot;

namespace Biden.Radar.Binance;

public static class SharedObjects
{
    public static List<BinanceSymbol> SpotSymbols = new List<BinanceSymbol>();
    public static List<BinanceFuturesUsdtSymbol> PerpSymbols = new List<BinanceFuturesUsdtSymbol>();
    public static BinanceSocketClient WebsocketApiClient = new BinanceSocketClient();
    public static BinanceRestClient RestClient = new BinanceRestClient();
    
    public static async Task<List<BinanceSymbol>> GetSpotTradingSymbols()
    {
        try
        {
            var spotSymbolsData = await RestClient.SpotApi.ExchangeData.GetExchangeInfoAsync();
            var spotSymbols = spotSymbolsData.Data.Symbols.Where(s => s.QuoteAsset == "USDT" && s.Status == SymbolStatus.Trading).ToList();
            return spotSymbols;
        }
        catch
        {
            return new List<BinanceSymbol>();
        }
    }


    public static async Task<List<BinanceFuturesUsdtSymbol>> GetPerpTradingSymbols()
    {
        try
        {
            var divSymbolsData = await RestClient.UsdFuturesApi.ExchangeData.GetExchangeInfoAsync();
            var swapSymbols = divSymbolsData.Data.Symbols.Where(s => s.QuoteAsset == "USDT" && s.Status == SymbolStatus.Trading).ToList();
            return swapSymbols;
        }
        catch
        {
            return new List<BinanceFuturesUsdtSymbol>();
        }
    }
}