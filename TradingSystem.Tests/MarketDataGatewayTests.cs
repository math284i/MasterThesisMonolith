using Moq;
using System.Runtime.CompilerServices;
using TradingSystem.Data;
using TradingSystem.Logic;
using TradingSystem.Logic.ExternalBrokers;

namespace TradingSystem.Tests;

public class MarketDataGatewayTests
{
    [Fact]
    public void CanPublishInitialMarketPrice()
    {
        var expectedInstrument1 = "NVDA";
        var expectedInstrument2 = "TSLA";
        var unexpectedInstrument = "SOME";
        var priceLow = 10.0m;
        var priceHigh = 20.0m;
        var expectedPrices1 = new Dictionary<string, decimal>{ { expectedInstrument1, priceHigh } };
        var expectedPrices2 = new Dictionary<string, decimal> { { expectedInstrument1, priceLow }, { expectedInstrument2, priceHigh } };
        var expectedPrices3 = new Dictionary<string, decimal> { { unexpectedInstrument, priceLow } };
        var nordeaMock = new Mock<INordea>();
        nordeaMock.Setup(e => e.getPrices()).Returns(expectedPrices1);
        var JPMorganMock = new Mock<IJPMorgan>();
        JPMorganMock.Setup(e => e.getPrices()).Returns(expectedPrices2);
        var NASDAQMock = new Mock<INASDAQ>();
        NASDAQMock.Setup(e => e.getPrices()).Returns(expectedPrices3);

        var observable = new Observable();
        var topic = TopicGenerator.TopicForAllInstruments();
        var stocks = new HashSet<Stock>{
            new Stock
            {
                InstrumentId = expectedInstrument1,
                Price = 0.0m
            },
            new Stock
            {
                InstrumentId = expectedInstrument2,
                Price = 0.0m
            }
        };
        observable.Publish(topic, stocks);

        var marketDataGateway = new MarketDataGateway(observable, nordeaMock.Object, JPMorganMock.Object, NASDAQMock.Object);
        marketDataGateway.Start();

        var stockTopic1 = TopicGenerator.TopicForMarketInstrumentPrice(expectedInstrument1);
        var stockTopic2 = TopicGenerator.TopicForMarketInstrumentPrice(expectedInstrument2);
        var stockTopic3 = TopicGenerator.TopicForMarketInstrumentPrice(unexpectedInstrument);
        var messages = observable.GetPersistentMessages();
        Assert.True(messages.ContainsKey(stockTopic1)); 
        Assert.True(messages.ContainsKey(stockTopic2));
        Stock stock1 = (Stock)messages[stockTopic1];
        Stock stock2 = (Stock)messages[stockTopic2];
        Assert.Equal(stock1.Price, priceLow);
        Assert.Equal(stock2.Price, priceHigh);
        Assert.False(messages.ContainsKey(unexpectedInstrument));
        marketDataGateway.Stop();
    }
    [Fact]
    public async void CanListenToMarketPrices()
    {
        var updatePrice = 500.0m;
        var observable = new Observable();
        var nordeaMock = new Mock<INordea>();
        nordeaMock.Setup(e => e.simulatePriceChange(It.IsAny<int>(), ref It.Ref<Lock>.IsAny, ref It.Ref<CancellationToken>.IsAny, ref It.Ref<bool>.IsAny)).Returns(new Data.Stock
        {
            InstrumentId = "TSLA",
            Price = updatePrice
        });
        var JPMorganMock = new Mock<IJPMorgan>();
        JPMorganMock.Setup(e => e.simulatePriceChange(It.IsAny<int>(), ref It.Ref<Lock>.IsAny, ref It.Ref<CancellationToken>.IsAny, ref It.Ref<bool>.IsAny)).Returns(new Data.Stock
        {
            InstrumentId = "NVDA",
            Price = updatePrice
        });
        var NASDAQMock = new Mock<INASDAQ>();
        NASDAQMock.Setup(e => e.simulatePriceChange(It.IsAny<int>(), ref It.Ref<Lock>.IsAny, ref It.Ref<CancellationToken>.IsAny, ref It.Ref<bool>.IsAny)).Returns(new Data.Stock
        {
            InstrumentId = "NOVO-B",
            Price = updatePrice
        });

        var marketDataGateway = new MarketDataGateway(observable, nordeaMock.Object, JPMorganMock.Object, NASDAQMock.Object);
        var priceUpdate = await marketDataGateway.marketCheck();
        var expectedStocks = new List<string> { "TSLA", "NVDA", "NOVO-B" };
        Assert.Contains(priceUpdate.InstrumentId, expectedStocks);
        Assert.Equal(priceUpdate.Price, updatePrice);
    }
}