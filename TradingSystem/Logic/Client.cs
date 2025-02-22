using Microsoft.Extensions.Options;
using TradingSystem.Data;

namespace TradingSystem.Logic;

public interface IClient
{
    public List<StockOptions> GetStockOptions();
}

public class Client(IOptions<TradingOptions> tradingOptions) : IClient
{
    private readonly TradingOptions _tradingOptions = tradingOptions.Value;

    public List<StockOptions> GetStockOptions()
    {
        var stockOptions = 
            _tradingOptions.Stocks.Select(name => new StockOptions { InstrumentId = name, EnableLivePrices = false })
            .ToList();
        return stockOptions;
    }
}