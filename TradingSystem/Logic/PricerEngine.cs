using Microsoft.Extensions.Options;
using TradingSystem.Data;
using TradingSystem.DTO;
using TradingSystem.Setup;

namespace TradingSystem.Logic;

public interface IPricerEngine
{
    
}


public class PricerEngine : IPricerEngine
{
    private readonly Dictionary<string, HashSet<StockOptions>> _clientsDict = new();
    private readonly HashSet<StockOptions> _referencePrices;
    private readonly IMessageBus _messageBus;

    public PricerEngine(IOptions<TradingOptions> stocksOptions, IMessageBus messageBus)
    {
        Console.WriteLine("Pricer engine starting up...");
        _referencePrices = new HashSet<StockOptions>();
        _messageBus = messageBus;
        var stocks = stocksOptions.Value.Stocks;

        foreach (var stock in stocks)
        {
            var newStock = new StockOptions
            {
                InstrumentId = stock,
                Price = 0.0f
            };
            //newStock.Price = GeneratePrice(newStock);
            _referencePrices.Add(newStock);
        }
        _messageBus.Publish("allInstruments", _referencePrices);
        SetupMessageBus();
    }

    private void SetupMessageBus()
    {

    }

    
}