using Moq;
using TradingSystem.Data;
using TradingSystem.Logic;
using TradingSystem.Logic.ExternalBrokers;

namespace TradingSystem.Tests;

public class HedgeServiceTests
{
    [Fact]
    public void CanHandleHedges()
    {
        var observable = new Observable();
        List<string> stocks = [ "DIS", "GME", "NVDA" ];
        var JPMorganMock = new Mock<IJPMorgan>();
        JPMorganMock.Setup(e => e.getStocks()).Returns(stocks);
        var nordeaMock = new Mock<INordea>();
        nordeaMock.Setup(e => e.getStocks()).Returns(new List<string>());
        var NASDAQMock = new Mock<INASDAQ>();
        NASDAQMock.Setup(e => e.getStocks()).Returns(new List<string>());
        var hedgeService = new HedgeService(observable, nordeaMock.Object, JPMorganMock.Object, NASDAQMock.Object);
        var instrument = "GME";
        var transaction = new TransactionData
        {
            TransactionId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            InstrumentId = instrument,
            Size = 1,
            Price = 10.0m,
            SpreadPrice = 1.0m,
            Succeeded = true
        };

        hedgeService.Start();
        hedgeService.HandleHedgeRequest(transaction);
        var topic = TopicGenerator.TopicForHedgingOrderResponse();
        var messages = observable.GetTransientMessages();
        var hedges = messages[topic].ConvertAll(x => ((TransactionData,string)) x);

        //Is the hedging response posted to observable and is the correct broker attached
        Assert.True(hedges.Exists(x => x.Item1 == transaction && x.Item2 == "JPMorgan"));
    }
}