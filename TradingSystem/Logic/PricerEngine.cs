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
            _referencePrices.Add(newStock);
            var stockTopic = TopicGenerator.TopicForMarketInstrumentPrice(newStock.InstrumentId);
            _messageBus.Subscribe<StockOptions>(stockTopic, UpdatePrice);
        }
        _messageBus.Publish("allInstruments", _referencePrices);
        
    }

    private void UpdatePrice(StockOptions stock)
    {
        //Reference price should be updated aswell.
        var stockTopic = TopicGenerator.TopicForClientInstrumentPrice(stock.InstrumentId);
        _messageBus.Publish(stockTopic, stock);
    }
}