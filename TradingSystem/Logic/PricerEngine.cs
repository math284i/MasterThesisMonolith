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
    private readonly Dictionary<string, HashSet<Stocks>> _clientsDict = new();
    private readonly HashSet<Stocks> _referencePrices;
    private readonly IOptions<InstrumentsOptions> _tradingOptions;
    private readonly IObservable _observable;
    private const string Id = "pricerEngine";

    public PricerEngine(ILogger<PricerEngine> logger, IOptions<InstrumentsOptions> stocksOptions, IObservable observable)
    {
        _logger = logger;
        _referencePrices = new HashSet<Stocks>();
        _tradingOptions = stocksOptions;
        _observable = observable;
        
    }

    public void Start()
    {
        _logger.PricerEngineStartUp();
        var stocks = _tradingOptions.Value.Stocks;

        foreach (var stock in stocks)
        {
            var newStock = new Stocks
            {
                InstrumentId = stock,
                Price = 0.0f
            };
            _referencePrices.Add(newStock);
            var stockTopic = TopicGenerator.TopicForMarketInstrumentPrice(newStock.InstrumentId);
            _observable.Subscribe<Stocks>(stockTopic, Id, UpdatePrice);
        }
        var topic = TopicGenerator.TopicForAllInstruments();
        _observable.Publish(topic, _referencePrices);
        _logger.PricerEngineStarted();
    }

    public void Stop()
    {
        // TODO unsubscribe to everything
    }

    private void UpdatePrice(Stocks stock)
    {
        //Reference price should be updated aswell.
        _logger.PricerEngineReceivedNewPrice(stock.InstrumentId, stock.Price);
        var stockTopic = TopicGenerator.TopicForClientInstrumentPrice(stock.InstrumentId);
        _observable.Publish(stockTopic, stock);
    }
}