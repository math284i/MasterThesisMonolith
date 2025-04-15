using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingSystem.Data;
using TradingSystem.Logic;

namespace TradingSystem.Tests;

public class RiskCalculatorTests
{
    private void SetupObservable(IObservable observable, string instrumentId, Guid clientId)
    {
        var clientHolding = new HoldingData
        {
            InstrumentId = instrumentId,
            ClientId = clientId,
            Size = 5,
        };
        var client = new ClientData
        {
            Balance = 1000.0m,
            ClientId = clientId,
            Holdings = [clientHolding],
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
            Holdings = [holdingData],
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
    public void CanHedgeToMarketOnSellOrder()
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
            Side = OrderSide.LeftSided,
            Stock = stock,
        };
        
        riskCalculator.Start();
        var orderTopic = TopicGenerator.TopicForClientOrder();
        
        Assert.False(order.HedgeOrder);
        
        observable.Publish(orderTopic, order);
        
        Assert.True(order.HedgeOrder);
    }

    [Fact]
    public void DanskeBankBuysIfUnderTargetOnSellOrder()
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
            Target = 20,
            Type = TargetType.FOK
        };
        var topicTarget = TopicGenerator.TopicForTargetPositionUpdate(targetNotFOK.InstrumentId);
        observable.Publish(topicTarget, targetNotFOK);
        var riskCalculator = GetRiskCalculator(observable);
        var order = new Order
        {
            ClientId = clientId,
            Side = OrderSide.LeftSided,
            Stock = stock,
        };
        var orderTopic = TopicGenerator.TopicForClientOrder();
        riskCalculator.Start();
        Assert.False(order.HedgeOrder);
        observable.Publish(orderTopic, order);
        Assert.False(order.HedgeOrder);
    }

    [Fact]
    public void DanskeBankSellsIfOverTargetOnBuyOrder()
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
            Target = 5,
            Type = TargetType.FOK
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
        Assert.False(order.HedgeOrder);
    }
    
    [Fact]
    public void CanRejectOrderIfInsufficientFunds()
    {
        var observable = new Observable();
        var instrumentId = "instrumentId1";
        var clientId = Guid.NewGuid();
        SetupObservable(observable, instrumentId, clientId);
        var stock = new Stock
        {
            InstrumentId = instrumentId,
            Price = 100.0m,
            Size = 20,
        };
        var targetNotFOK = new TargetPosition
        {
            InstrumentId = instrumentId,
            Target = 5,
            Type = TargetType.FOK
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
        observable.Publish(orderTopic, order);
        Assert.Equal(OrderStatus.Rejected, order.Status);
    }

    [Fact]
    public void CanAcceptBuyOrder()
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
            Target = 5,
            Type = TargetType.FOK
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
        observable.Publish(orderTopic, order);
        Assert.NotEqual(OrderStatus.Rejected, order.Status);
    }

    [Fact]
    public void CanAcceptSellOrder()
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
            Target = 5,
            Type = TargetType.FOK
        };
        var topicTarget = TopicGenerator.TopicForTargetPositionUpdate(targetNotFOK.InstrumentId);
        observable.Publish(topicTarget, targetNotFOK);
        var riskCalculator = GetRiskCalculator(observable);
        var order = new Order
        {
            ClientId = clientId,
            Side = OrderSide.LeftSided,
            Stock = stock,
        };
        var orderTopic = TopicGenerator.TopicForClientOrder();
        riskCalculator.Start();
        observable.Publish(orderTopic, order);
        Assert.NotEqual(OrderStatus.Rejected, order.Status);
    }

    [Fact]
    public void CanRejectIfSpreadIsTooBig()
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
            Target = 5,
            Type = TargetType.FOK
        };
        var topicTarget = TopicGenerator.TopicForTargetPositionUpdate(targetNotFOK.InstrumentId);
        observable.Publish(topicTarget, targetNotFOK);
        var riskCalculator = GetRiskCalculator(observable);
        var order = new Order
        {
            ClientId = clientId,
            Side = OrderSide.LeftSided,
            Stock = stock,
            SpreadPrice = 101.0m
        };
        var orderTopic = TopicGenerator.TopicForClientOrder();
        riskCalculator.Start();
        Assert.False(order.HedgeOrder);
        observable.Publish(orderTopic, order);
        Assert.Equal(OrderStatus.Rejected, order.Status);
    }

    [Fact]
    public void CanRejectIfClientDoesntOwnEnoughStock()
    {
        var observable = new Observable();
        var instrumentId = "instrumentId1";
        var clientId = Guid.NewGuid();
        SetupObservable(observable, instrumentId, clientId);
        var stock = new Stock
        {
            InstrumentId = instrumentId,
            Price = 100.0m,
            Size = 10,
        };
        var targetNotFOK = new TargetPosition
        {
            InstrumentId = instrumentId,
            Target = 5,
            Type = TargetType.FOK
        };
        var topicTarget = TopicGenerator.TopicForTargetPositionUpdate(targetNotFOK.InstrumentId);
        observable.Publish(topicTarget, targetNotFOK);
        var riskCalculator = GetRiskCalculator(observable);
        var order = new Order
        {
            ClientId = clientId,
            Side = OrderSide.LeftSided,
            Stock = stock,
            SpreadPrice = 0.0m
        };
        var orderTopic = TopicGenerator.TopicForClientOrder();
        riskCalculator.Start();
        Assert.False(order.HedgeOrder);
        observable.Publish(orderTopic, order);
        Assert.Equal(OrderStatus.Rejected, order.Status);
    }
}