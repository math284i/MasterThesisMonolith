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

    public void UpdatePrice(Stock stock);
    public HashSet<Stock> GetReferencePrices();
}


public class PricerEngine : IPricerEngine
{
    private readonly ILogger<PricerEngine> _logger;
    private HashSet<Stock> _referencePrices;
    private readonly IOptions<InstrumentsOptions> _tradingOptions;
    private readonly IObservable _observable;
    private const string Id = "pricerEngine";

    public PricerEngine(ILogger<PricerEngine> logger, IOptions<InstrumentsOptions> stocksOptions, IObservable observable)
    {
        _logger = logger;
        _referencePrices = new HashSet<Stock>();
        _tradingOptions = stocksOptions;
        _observable = observable;
        
    }

    public void Start()
    {
        _logger.PricerEngineStartUp();

        SubscribeToMarketPriceUpdates();
        PublishReferencePrices();

        _logger.PricerEngineStarted();
    }

    private void SubscribeToMarketPriceUpdates()
    {
        var stocks = _tradingOptions.Value.Stocks;

        foreach (var stock in stocks)
        {
            var newStock = new Stock
            {
                InstrumentId = stock,
                Price = 0.0m
            };
            _referencePrices.Add(newStock);
            var stockTopic = TopicGenerator.TopicForMarketInstrumentPrice(newStock.InstrumentId);
            _observable.Subscribe<Stock>(stockTopic, Id, UpdatePrice);
        }
    }
    private void PublishReferencePrices()
    {
        var topic = TopicGenerator.TopicForAllInstruments();
        _observable.Publish(topic, _referencePrices);
    }

    public void Stop()
    {
        foreach (var stock in _tradingOptions.Value.Stocks)
        {
            var stockTopic = TopicGenerator.TopicForMarketInstrumentPrice(stock);
            _observable.Unsubscribe(stockTopic, Id);
        }
        _referencePrices = new();
    }

    public HashSet<Stock> GetReferencePrices()
    {
        return _referencePrices;
    }

    public void UpdatePrice(Stock stock)
    {
        _logger.PricerEngineReceivedNewPrice(stock.InstrumentId, stock.Price);
        stock.DateMaturity = DateTime.Today.AddYears(10);
        foreach (var reference in _referencePrices.Where(reference => reference.InstrumentId == stock.InstrumentId))
        {
            reference.Price = stock.Price;
            reference.DateMaturity = stock.DateMaturity;
        }
        var stockTopic = TopicGenerator.TopicForClientInstrumentPrice(stock.InstrumentId);
        _observable.Publish(stockTopic, stock);
    }
}