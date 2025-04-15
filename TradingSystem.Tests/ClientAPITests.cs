using TradingSystem.Data;
using TradingSystem.DTO;
using TradingSystem.Logic;

namespace TradingSystem.Tests;

public class ClientAPITests
{
    private IClient GetClient(IObservable observable)
    {
        return new ClientAPI(observable);
    }
    
    [Fact]
    public void CanSaveClientDataOnLogin()
    {
        var observable = new Observable();
        var client = GetClient(observable);
        var responseTopic = TopicGenerator.TopicForLoginResponse();
        var clientId = Guid.NewGuid();
        var dataTopic = TopicGenerator.TopicForDBDataOfClient(clientId.ToString());
        var loginInfo = new LoginInfo
        {
            ClientId = clientId,
            IsAuthenticated = true,
        };
        
        client.Login("username", "password", info => {}, tmp => {});
        var clients = client.GetClientData();
        
        Assert.False(clients.ContainsKey(clientId));
        
        observable.Publish(responseTopic, loginInfo);
        observable.Publish(dataTopic, new ClientData {ClientId = clientId});
        clients = client.GetClientData();
        
        Assert.True(clients.ContainsKey(clientId));
    }

    [Fact]
    public void CanStreamPriceToClient()
    {
        var observable = new Observable();
        var client = GetClient(observable);
        var clientId = Guid.NewGuid();
        var instrumentId = "instrumentId1";
        var topic = TopicGenerator.TopicForClientInstrumentPrice(instrumentId);
        var currentPrice = 0.0m;
        var expectedPrice = 10.0m;
        var streamInfo = new StreamInformation
        {
            ClientId = clientId,
            EnableLivePrices = true,
            InstrumentId = instrumentId,
        };
        var dataTopic = TopicGenerator.TopicForDBDataOfClient(clientId.ToString());
        var loginInfo = new LoginInfo
        {
            ClientId = clientId,
            IsAuthenticated = true,
        };
        var responseTopic = TopicGenerator.TopicForLoginResponse();
        
        client.Login("username", "password", info => {}, tmp => {});
        observable.Publish(responseTopic, loginInfo);
        observable.Publish(dataTopic, new ClientData {ClientId = clientId, Tier = Tier.Internal});
        
        client.StreamPrice(streamInfo, price => { currentPrice = price.Price;});
        Assert.Equal(0.0m, currentPrice);
        
        observable.Publish(topic, new Stock {InstrumentId = instrumentId, Price = expectedPrice});
        
        Assert.True(currentPrice > expectedPrice);
    }

    [Fact]
    public void AddsCorrectSpreadWhenStreamingPrice()
    {
        var observable = new Observable();
        var client = GetClient(observable);
        var clientId = Guid.NewGuid();
        var instrumentId = "instrumentId1";
        var topic = TopicGenerator.TopicForClientInstrumentPrice(instrumentId);
        var currentPrice = 0.0m;
        var expectedPrice = 10.0m;
        var spreadPrice = (expectedPrice) + expectedPrice * 0.001m; // This will be effected if Tier change.
        var streamInfo = new StreamInformation
        {
            ClientId = clientId,
            EnableLivePrices = true,
            InstrumentId = instrumentId,
        };
        var dataTopic = TopicGenerator.TopicForDBDataOfClient(clientId.ToString());
        var loginInfo = new LoginInfo
        {
            ClientId = clientId,
            IsAuthenticated = true,
        };
        var responseTopic = TopicGenerator.TopicForLoginResponse();
        
        client.Login("username", "password", info => {}, tmp => {});
        observable.Publish(responseTopic, loginInfo);
        observable.Publish(dataTopic, new ClientData {ClientId = clientId, Tier = Tier.Internal});
        
        client.StreamPrice(streamInfo, price => { currentPrice = price.Price;});
        Assert.Equal(0.0m, currentPrice);
        
        observable.Publish(topic, new Stock {InstrumentId = instrumentId, Price = expectedPrice});
        
        Assert.True(currentPrice == spreadPrice);
    }

    [Fact]
    public void CanRemoveSpreadOnBuyOrder()
    {
        var observable = new Observable();
        var client = GetClient(observable);
        var clientId = Guid.NewGuid();
        var instrumentId = "instrumentId1";
        var expectedPrice = 10.0m;
        var spreadPrice = (expectedPrice) + expectedPrice * 0.001m;
        var dataTopic = TopicGenerator.TopicForDBDataOfClient(clientId.ToString());
        var actualOrder = new Order
        {
            ClientId = clientId,
            Side = OrderSide.RightSided,
            Stock = new Stock {InstrumentId = instrumentId, Price = spreadPrice}
        };
        var clientOrderTopic = TopicGenerator.TopicForClientOrder();
        var loginInfo = new LoginInfo
        {
            ClientId = clientId,
            IsAuthenticated = true,
        };
        var responseTopic = TopicGenerator.TopicForLoginResponse();
        
        client.Login("username", "password", info => {}, tmp => {});
        observable.Publish(responseTopic, loginInfo);
        observable.Publish(dataTopic, new ClientData {ClientId = clientId, Tier = Tier.Internal});
        
        observable.Subscribe<Order>(clientOrderTopic, "id", order => { actualOrder = order;});
        Assert.Equal(spreadPrice, actualOrder.Stock.Price);
        
        client.HandleOrder(actualOrder, order => {});
        
        Assert.Equal(expectedPrice, actualOrder.Stock.Price);
        Assert.Equal(spreadPrice - expectedPrice, actualOrder.SpreadPrice);
    }

    [Fact]
    public void CanAddSpreadOnSellOrder()
    {
        var observable = new Observable();
        var client = GetClient(observable);
        var clientId = Guid.NewGuid();
        var instrumentId = "instrumentId1";
        var expectedPrice = 10.0m;
        var spreadPrice = (expectedPrice) - expectedPrice * 0.001m;
        var dataTopic = TopicGenerator.TopicForDBDataOfClient(clientId.ToString());
        var actualOrder = new Order
        {
            ClientId = clientId,
            Side = OrderSide.LeftSided,
            Stock = new Stock {InstrumentId = instrumentId, Price = spreadPrice}
        };
        var clientOrderTopic = TopicGenerator.TopicForClientOrder();
        var loginInfo = new LoginInfo
        {
            ClientId = clientId,
            IsAuthenticated = true,
        };
        var responseTopic = TopicGenerator.TopicForLoginResponse();
        
        client.Login("username", "password", info => {}, tmp => {});
        observable.Publish(responseTopic, loginInfo);
        observable.Publish(dataTopic, new ClientData {ClientId = clientId, Tier = Tier.Internal});
        
        observable.Subscribe<Order>(clientOrderTopic, "id", order => { actualOrder = order;});
        Assert.Equal(spreadPrice, actualOrder.Stock.Price);
        
        client.HandleOrder(actualOrder, order => {});
        
        Assert.Equal(expectedPrice, actualOrder.Stock.Price);
        Assert.Equal(expectedPrice - spreadPrice, actualOrder.SpreadPrice);
    }
    
}