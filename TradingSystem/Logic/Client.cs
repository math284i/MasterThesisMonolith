using Microsoft.Extensions.Options;
using TradingSystem.Data;

namespace TradingSystem.Logic;

public interface IClient
{
    public HashSet<StockOptions> GetStockOptions();
}

public class Client(IOptions<TradingOptions> tradingOptions) : IClient
{
    private readonly TradingOptions _tradingOptions = tradingOptions.Value;
    public const string CLIENT_STREAM_PRICE = "client-stream-price";

    public HashSet<StockOptions> GetStockOptions()
    {
        var stockOptions = 
            _tradingOptions.Stocks.Select(name => new StockOptions { InstrumentId = name, EnableLivePrices = false })
                .ToHashSet();
        return stockOptions;
    }
}