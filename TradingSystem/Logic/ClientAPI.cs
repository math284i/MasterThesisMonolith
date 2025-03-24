using Microsoft.Extensions.Options;
using TradingSystem.Data;
using TradingSystem.DTO;

namespace TradingSystem.Logic;

public interface IClient
{
    public HashSet<Stocks> GetStockOptions<T>(Action<T> client);
    public void HandleOrder(Order order, Action<Order> callback);

    public void Login(string username, string password, Action<LoginInfo> callbackLogin,
        Action<ClientData> callbackClientData);

    public void Logout(Action<bool> callback);
    void StreamPrice(StreamInformation info, Action<Stocks> updatePrice);
}

public class ClientAPI : IClient
{
    private HashSet<Stocks> _tradingOptions;
    private readonly List<Delegate> _clients;
    private const string Id = "clientAPI";
    private readonly IObservable _observable;

    public ClientAPI(IObservable observable)
    {
        _tradingOptions = new HashSet<Stocks>();
        _clients = new List<Delegate>();
        _observable = observable;
        var topic = TopicGenerator.TopicForAllInstruments();
        _observable.Subscribe<HashSet<Stocks>>(topic, Id, stockOptions =>
        {
            Console.WriteLine("ClientAPI found messages" + stockOptions);
            _tradingOptions = stockOptions;
            foreach (var client in _clients)
            {
                client.DynamicInvoke(stockOptions);
            }
        });
    }

    public HashSet<Stocks> GetStockOptions<T>(Action<T> client)
    {
        _clients.Add(client);
        return _tradingOptions;
    }

    public void StreamPrice(StreamInformation info, Action<Stocks> updatePrice)
    {
        var stockTopic = TopicGenerator.TopicForClientInstrumentPrice(info.InstrumentId);
        if (info.EnableLivePrices)
        {
            _observable.Subscribe(stockTopic, info.ClientId.ToString(), updatePrice);
        }
        else
        {
            _observable.Unsubscribe(stockTopic, info.ClientId.ToString());
        }
    }

    public void HandleOrder(Order order, Action<Order> callback)
    {
        var topicToPublish = TopicGenerator.TopicForClientBuyOrder();
        var topicToSubscribe = TopicGenerator.TopicForClientOrderEnded(order.ClientId.ToString());
        
        _observable.Subscribe(topicToSubscribe, Id, callback);
        
        _observable.Publish(topicToPublish, order, isTransient: true);
    }

    public void Login(string username, string password, Action<LoginInfo> callbackLogin, Action<ClientData> callbackClientData)
    {
        var requestTopic = TopicGenerator.TopicForLoginRequest();
        var responseTopic = TopicGenerator.TopicForLoginResponse();
        var info = new LoginInfo
        {
            Username = username,
            Password = password
        };
        _observable.Subscribe<LoginInfo>(responseTopic, Id, info =>
        {
            if (info.IsAuthenticated)
            {
                var topic = TopicGenerator.TopicForDBDataOfClient(info.ClientId.ToString());
                _observable.Subscribe(topic, Id, callbackClientData);
            }
            callbackLogin?.Invoke(info);
        });
        _observable.Publish(requestTopic, info, isTransient: true);
    }
    

    public void Logout(Action<bool> callback)
    {
        callback.Invoke(false);
    }
}