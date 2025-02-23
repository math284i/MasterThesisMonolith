using TradingSystem.Data;
using TradingSystem.Setup;

namespace TradingSystem.Logic;

public interface IPricerEngineClient
{
    public void Stream(string clientId, string instrumentId, bool enableLivePrices);
}

public interface IPricerEngineInternal
{
    public void ThereIsUpdate(string instrumentId);
}

public class PricerEngine(IClient client) : IPricerEngineClient, IPricerEngineInternal
{
    private readonly Dictionary<string, HashSet<StockOptions>> _clientsDict = new();
    private readonly HashSet<StockOptions> _stocks = client.GetStockOptions();

    public void Stream(string clientId, string instrumentId, bool enableLivePrices)
    {
        if (!_clientsDict.ContainsKey(clientId))
        {
            var tmpStocks = _stocks;
            _clientsDict.Add(clientId, tmpStocks);
        }
        else
        {
            var stockOption = _clientsDict[clientId].First(o => o.InstrumentId == instrumentId);
            stockOption.EnableLivePrices = enableLivePrices;
        }

        foreach (var client in _clientsDict)
        {
            Console.WriteLine("Client: {0} with setup: {1}", client.Key, client.Value);
        }
    }

    public void ThereIsUpdate(string instrumentId)
    {
        var newPrice = GeneratePrice(instrumentId);
        UpdatePrice(instrumentId, newPrice);
    }

    private void UpdatePrice(string instrumentId, float price)
    {
        
    }

    private float GeneratePrice(string instrumentId)
    {
        CheckMarketPrice(instrumentId);
        CheckBook(instrumentId);
        return 0.0f;
    }

    private void CheckMarketPrice(string instrumentId)
    {
        
    }

    private void CheckBook(string instrumentId)
    {
        
    }
    
}