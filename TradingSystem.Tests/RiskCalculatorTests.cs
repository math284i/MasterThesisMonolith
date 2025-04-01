using Microsoft.Extensions.Logging.Abstractions;
using TradingSystem.Data;
using TradingSystem.Logic;

namespace TradingSystem.Tests;

public class RiskCalculatorTests
{
    private void SetupObservable(IObservable observable, string instrumentId, Guid clientId)
    {
        var holdings = new List<HoldingData>();
        var client = new ClientData
        {
            Balance = 1000.0m,
            ClientId = clientId,
            Holdings = holdings,
            Name = "Test1",
            Tier = Tier.Regular
        };
        
        var dbId = Guid.NewGuid();
        var holdingData = new HoldingData
        {
            InstrumentId = instrumentId,
            ClientId = dbId,
            Size = 10,
        };
        var danskeBank = new ClientData
        {
            ClientId = dbId,
            Name = "Danske_Bank",
            Holdings = new List<HoldingData> { holdingData },
        };
        var allClients = new List<ClientData> { client, danskeBank };
        var topicAllClients = TopicGenerator.TopicForAllClients();
        observable.Publish(topicAllClients, allClients);
        var topicTestClient = TopicGenerator.TopicForDBDataOfClient(client.ClientId.ToString());
        observable.Publish(topicTestClient, client);
        var topicDB = TopicGenerator.TopicForDBDataOfClient(danskeBank.ClientId.ToString());
        observable.Publish(topicDB, danskeBank);
        var targetPosition = new TargetPosition
        {
            InstrumentId = instrumentId,
            Target = 10,
            Type = TargetType.FOK
        };
        var allTargetPositions = new List<TargetPosition> { targetPosition };
        var topicAllTargetPositions = TopicGenerator.TopicForAllTargetPositions();
        observable.Publish(topicAllTargetPositions, allTargetPositions);
    }
    private IRiskCalculator GetRiskCalculator(IObservable observable)
    {
        var logger = NullLogger<RiskCalculator>.Instance;
        return new RiskCalculator(observable, logger);
    }
    
    [Fact]
    public void CanHedgeToMarket()
    {
        var observable = new Observable();
        var instrumentId = "instrumentId1";
        var clientId = Guid.NewGuid();
        SetupObservable(observable, instrumentId, clientId);
        var stock = new Stock
        {
            InstrumentId = instrumentId,
            Price = 100.0m,
            Size = 1,
        };
        var riskCalculator = GetRiskCalculator(observable);
        var order = new Order
        {
            ClientId = clientId,
            Side = OrderSide.RightSided,
            Stock = stock,
        };
        
        riskCalculator.Start();
        var orderTopic = TopicGenerator.TopicForClientOrder();
        
        Assert.False(order.HedgeOrder);
        
        observable.Publish(orderTopic, order);
        
        Assert.True(order.HedgeOrder);
    }

    [Fact]
    public void WillAlwaysHedgeIfNotFOK()
    {
        var observable = new Observable();
        var instrumentId = "instrumentId1";
        var clientId = Guid.NewGuid();
        SetupObservable(observable, instrumentId, clientId);
        var stock = new Stock
        {
            InstrumentId = instrumentId,
            Price = 100.0m,
            Size = 1,
        };
        var targetNotFOK = new TargetPosition
        {
            InstrumentId = instrumentId,
            Target = 10,
            Type = TargetType.IOC
        };
        var topicTarget = TopicGenerator.TopicForTargetPositionUpdate(targetNotFOK.InstrumentId);
        observable.Publish(topicTarget, targetNotFOK);
        var riskCalculator = GetRiskCalculator(observable);
        var order = new Order
        {
            ClientId = clientId,
            Side = OrderSide.RightSided,
            Stock = stock,
        };
        var orderTopic = TopicGenerator.TopicForClientOrder();
        riskCalculator.Start();
        Assert.False(order.HedgeOrder);
        observable.Publish(orderTopic, order);
        Assert.True(order.HedgeOrder);
    }

    [Fact]
    public void CanHedgeToMarketOnBuyOrder()
    {
        
    }

    [Fact]
    public void CanHedgeToMarketOnSellOrder()
    {
        
    }

    [Fact]
    public void DanskeBankBuysIfUnderTargetOnSellOrder()
    {
        
    }

    [Fact]
    public void DanskeBankSellsIfOverTargetOnBuyOrder()
    {
        
    }

    [Fact]
    public void CanSubscribeToAllClients()
    {
        
    }

    [Fact]
    public void CanSubscribeToAllTargets()
    {
        
    }

    [Fact]
    public void CanRejectOrderIfInsufficientFunds()
    {
        
    }

    [Fact]
    public void CanAcceptBuyOrder()
    {
        
    }

    [Fact]
    public void CanAcceptSellOrder()
    {
        
    }

    [Fact]
    public void CanRejectIfSpreadIsTooBig()
    {
        
    }

    [Fact]
    public void CanRejectIfClientDoesntOwnEnoughStock()
    {
        //Check 0 and then less than trying to sell
    }
    
}