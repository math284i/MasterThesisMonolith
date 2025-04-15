using Microsoft.Extensions.Logging.Abstractions;
using TradingSystem.Data;
using TradingSystem.Logic;

namespace TradingSystem.Tests;

public class ExecutionHandlerTests
{
    private IExecutionHandler GetExecutionHandler(IObservable observable)
    {
        return new ExecutionHandler(NullLogger<ExecutionHandler>.Instance, observable);
    }
    
    private void SetupObservable(IObservable observable, string instrumentId, Guid clientId, decimal price)
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

        var stock = new Stock
        {
            InstrumentId = instrumentId,
            Price = price
        };

        var allStocks = new HashSet<Stock> { stock };
        var topicForStocks = TopicGenerator.TopicForAllInstruments();
        observable.Publish(topicForStocks, allStocks);
        var topicForStock = TopicGenerator.TopicForClientInstrumentPrice(stock.InstrumentId);
        observable.Publish(topicForStock, stock);
        var allClients = new List<ClientData> { client };
        var topicAllClients = TopicGenerator.TopicForAllClients();
        observable.Publish(topicAllClients, allClients);
        var topicTestClient = TopicGenerator.TopicForDBDataOfClient(client.ClientId.ToString());
        observable.Publish(topicTestClient, client);
    }
    
    [Fact]
    public void CanHandleBuyOrder()
    {
        var observable = new Observable();
        var instrumentId = "instrumentId1";
        var clientId = Guid.NewGuid();
        var price = 10.0m;
        SetupObservable(observable, instrumentId, clientId, price);
        var executionHandler = GetExecutionHandler(observable);
        var stock = new Stock
        {
            InstrumentId = instrumentId,
            Price = price
        };
        var order = new Order
        {
            ClientId = clientId,
            Side = OrderSide.RightSided,
            Stock = stock,
        };
        var topic = TopicGenerator.TopicForClientOrderApproved();
        
        executionHandler.Start();
        observable.Publish(topic, order);

        Assert.Equal(OrderStatus.Success, order.Status);
    }

    [Fact]
    public void CanHandleSellOrder()
    {
        var observable = new Observable();
        var instrumentId = "instrumentId1";
        var clientId = Guid.NewGuid();
        var price = 10.0m;
        SetupObservable(observable, instrumentId, clientId, price);
        var executionHandler = GetExecutionHandler(observable);
        var stock = new Stock
        {
            InstrumentId = instrumentId,
            Price = price
        };
        var order = new Order
        {
            ClientId = clientId,
            Side = OrderSide.LeftSided,
            Stock = stock,
        };
        var topic = TopicGenerator.TopicForClientOrderApproved();
        
        executionHandler.Start();
        observable.Publish(topic, order);

        Assert.Equal(OrderStatus.Success, order.Status);
    }

    [Fact]
    public void AcceptsOrderIfPriceMatch()
    {
        var observable = new Observable();
        var instrumentId = "instrumentId1";
        var clientId = Guid.NewGuid();
        var price = 10.0m;
        SetupObservable(observable, instrumentId, clientId, price);
        var executionHandler = GetExecutionHandler(observable);
        var stock = new Stock
        {
            InstrumentId = instrumentId,
            Price = price
        };
        var order = new Order
        {
            ClientId = clientId,
            Side = OrderSide.RightSided,
            Stock = stock,
        };
        var topic = TopicGenerator.TopicForClientOrderApproved();
        
        executionHandler.Start();
        observable.Publish(topic, order);

        Assert.Equal(OrderStatus.Success, order.Status);
    }

    [Fact]
    public void CancelsOrderIfPriceDontMatch()
    {
        var observable = new Observable();
        var instrumentId = "instrumentId1";
        var clientId = Guid.NewGuid();
        var price = 10.0m;
        SetupObservable(observable, instrumentId, clientId, price);
        var executionHandler = GetExecutionHandler(observable);
        var stock = new Stock
        {
            InstrumentId = instrumentId,
            Price = price + 10.0m
        };
        var order = new Order
        {
            ClientId = clientId,
            Side = OrderSide.RightSided,
            Stock = stock,
        };
        var topic = TopicGenerator.TopicForClientOrderApproved();
        
        executionHandler.Start();
        observable.Publish(topic, order);

        Assert.Equal(OrderStatus.Canceled, order.Status);
    }
}