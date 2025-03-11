using Microsoft.Extensions.Options;
using TradingSystem.Data;
using TradingSystem.DTO;
using TradingSystem.Logic.LoggerExtensions;
using TradingSystem.Setup;

namespace TradingSystem.Logic;

public interface IPricerEngine
{
    public void Start();
    public void Stop();
}


public class PricerEngine : IPricerEngine
{
    private readonly ILogger<PricerEngine> _logger;
    private readonly Dictionary<string, HashSet<StockOptions>> _clientsDict = new();
    private readonly HashSet<StockOptions> _referencePrices;
    private readonly IOptions<TradingOptions> _tradingOptions;
    private readonly IMessageBus _messageBus;
    private const string Id = "pricerEngine";

    public PricerEngine(ILogger<PricerEngine> logger, IOptions<TradingOptions> stocksOptions, IMessageBus messageBus)
    {
        _logger = logger;
        _referencePrices = new HashSet<StockOptions>();
        _tradingOptions = stocksOptions;
        _messageBus = messageBus;
        
    }

    public void Start()
    {
        _logger.PricerEngineStartUp();
        var stocks = _tradingOptions.Value.Stocks;

        foreach (var stock in stocks)
        {
            var newStock = new StockOptions
            {
                InstrumentId = stock,
                Price = 0.0f
            };
            _referencePrices.Add(newStock);
            var stockTopic = TopicGenerator.TopicForMarketInstrumentPrice(newStock.InstrumentId);
            _messageBus.Subscribe<StockOptions>(stockTopic, Id, UpdatePrice);
        }
        var topic = TopicGenerator.TopicForAllInstruments();
        _messageBus.Publish(topic, _referencePrices);
        _logger.PricerEngineStarted();
    }

    public void Stop()
    {
        // TODO unsubscribe to everything
    }

    private void UpdatePrice(StockOptions stock)
    {
        //Reference price should be updated aswell.
        _logger.PricerEngineReceivedNewPrice(stock.InstrumentId, stock.Price);
        var stockTopic = TopicGenerator.TopicForClientInstrumentPrice(stock.InstrumentId);
        _messageBus.Publish(stockTopic, stock);
    }
}