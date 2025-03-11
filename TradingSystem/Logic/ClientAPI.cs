using Microsoft.Extensions.Options;
using TradingSystem.Data;
using TradingSystem.DTO;

namespace TradingSystem.Logic;

public interface IClient
{
    public HashSet<StockOptions> GetStockOptions<T>(Action<T> client);
    public void HandleOrder(Order order);

    public void Login(string clientId, string password, Action<LoginInfo> callback);

    public void Logout(Action<bool> callback);
    void StreamPrice(StreamInformation info, Action<StockOptions> updatePrice);
}

public class ClientAPI : IClient
{
    private HashSet<StockOptions> _tradingOptions;
    private readonly List<Delegate> _clients;
    private const string Id = "clientAPI";
    private readonly IMessageBus _messageBus;

    public ClientAPI(IMessageBus messageBus)
    {
        _tradingOptions = new HashSet<StockOptions>();
        _clients = new List<Delegate>();
        _messageBus = messageBus;
        _messageBus.Subscribe<HashSet<StockOptions>>("allInstruments", Id, stockOptions =>
        {
            Console.WriteLine("ClientAPI found messages" + stockOptions);
            _tradingOptions = stockOptions;
            foreach (var client in _clients)
            {
                client.DynamicInvoke(stockOptions);
            }
        });
    }

    public HashSet<StockOptions> GetStockOptions<T>(Action<T> client)
    {
        _clients.Add(client);
        return _tradingOptions;
    }

    public void StreamPrice(StreamInformation info, Action<StockOptions> updatePrice)
    {
        var stockTopic = TopicGenerator.TopicForClientInstrumentPrice(info.InstrumentId);
        if (info.EnableLivePrices)
        {
            _messageBus.Subscribe(stockTopic, info.ClientId.ToString(), updatePrice);
        }
        else
        {
            _messageBus.Unsubscribe(stockTopic, info.ClientId.ToString());
        }
    }

    public void HandleOrder(Order order)
    {
        var topic = TopicGenerator.TopicForClientBuyOrder();
        _messageBus.Publish(topic, order, isTransient: true);
    }

    public void Login(string username, string password, Action<LoginInfo> callback)
    {
        var requestTopic = TopicGenerator.TopicForLoginRequest();
        var responseTopic = TopicGenerator.TopicForLoginResponse();
        var info = new LoginInfo
        {
            Username = username,
            Password = password
        };
        _messageBus.Subscribe(responseTopic, Id, callback);
        _messageBus.Publish(requestTopic, info, isTransient: true);
    }

    public void Logout(Action<bool> callback)
    {
        callback.Invoke(false);
    }
}