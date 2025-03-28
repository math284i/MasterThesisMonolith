using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingSystem.Data;
using TradingSystem.Logic;
using TradingSystem.Logic.ExternalBrokers;

namespace TradingSystem.Tests;

public class PricerEngineTests
{
    private IPricerEngine GetPricerEngine(InstrumentsOptions stocksOptions, IObservable observable)
    {
        var logger = NullLogger<PricerEngine>.Instance;
        var options = Options.Create(stocksOptions);
        var pricerEngine = new PricerEngine(logger, options, observable);
        return pricerEngine;
    }
    
    [Fact]
    public void CanSubscribeToMarketData()
    {
        var testInstrument = "GME";
        var observable = new Observable();
        var finalPrice = 100m;
        var jpPrice = new Dictionary<string, decimal>();
        jpPrice.Add(testInstrument, finalPrice);
        var jpMorgan = new Mock<IJPMorgan>();
        jpMorgan.Setup(e => e.getPrices()).Returns(jpPrice);
        var nasdaq = new Mock<INASDAQ>();
        nasdaq.Setup(e => e.getPrices()).Returns(new Dictionary<string, decimal>());
        var nordea = new Mock<INordea>();
        nordea.Setup(e => e.getPrices()).Returns(new Dictionary<string, decimal>());
        var marketDataGateway = new MarketDataGateway(observable, nordea.Object, jpMorgan.Object, nasdaq.Object);
        var instruments = new InstrumentsOptions
        {
            Stocks = new HashSet<string> { testInstrument }
        };
        var topic = TopicGenerator.TopicForMarketInstrumentPrice(testInstrument);
        var pricerEngine = GetPricerEngine(instruments, observable);
        
        pricerEngine.Start();
        var prices = pricerEngine.GetReferencePrices();
        var instrument = prices.Single();
        Assert.Equal(0.0m, instrument.Price);
        marketDataGateway.Start();
        
        prices = pricerEngine.GetReferencePrices();
        instrument = prices.Single();
        Assert.Equal(finalPrice, instrument.Price);
    }

    [Fact]
    public void CanPublishClientPrice()
    {
       var testInstrument = "GME";
       var finalPrice = 100m;
       var observable = new Observable();
       var instruments = new InstrumentsOptions
       {
           Stocks = new HashSet<string> { testInstrument }
       };
       var topic = TopicGenerator.TopicForClientInstrumentPrice(testInstrument);
       var pricerEngine = GetPricerEngine(instruments, observable);
       var stock = new Stock
       {
           InstrumentId = testInstrument,
           Price = finalPrice,
       };
       
       pricerEngine.Start();
       pricerEngine.UpdatePrice(stock);

       var messages = observable.GetPersistentMessages();
       foreach (var message in messages.Where(message => message.Key == testInstrument))
       {
           Assert.Equal(finalPrice, message.Value);
       }

    }

    [Fact]
    public void CanUpdatePrice()
    {
        var testInstrument = "GME";
        var startPrice = 100m;
        var finalPrice = 500m;
        var observable = new Observable();
        var instruments = new InstrumentsOptions
        {
            Stocks = new HashSet<string> { testInstrument }
        };
        var topic = TopicGenerator.TopicForClientInstrumentPrice(testInstrument);
        var pricerEngine = GetPricerEngine(instruments, observable);
        var stock = new Stock
        {
            InstrumentId = testInstrument,
            Price = startPrice,
        };
       
        pricerEngine.Start();
        pricerEngine.UpdatePrice(stock);

        var messages = observable.GetPersistentMessages();
        foreach (var message in messages.Where(message => message.Key == testInstrument))
        {
            Assert.Equal(startPrice, message.Value);
        }
        
        stock.Price = finalPrice;
        pricerEngine.UpdatePrice(stock);
        
        messages = observable.GetPersistentMessages();
        foreach (var message in messages.Where(message => message.Key == testInstrument))
        {
            Assert.Equal(finalPrice, message.Value);
        }
    }
}