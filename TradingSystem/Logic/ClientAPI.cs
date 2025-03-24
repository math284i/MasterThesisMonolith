using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TradingSystem.Data;
using TradingSystem.DTO;

namespace TradingSystem.Logic;

public interface IClient
{
    public HashSet<Stock> GetStockOptions<T>(Action<T> client);
    public void HandleOrder(Order order, Action<Order> callback);

    public void Login(string username, string password, Action<LoginInfo> callbackLogin,
        Action<ClientData> callbackClientData);

    public void Logout(Action<bool> callback);
    public void StreamPrice(StreamInformation info, Action<Stock> updatePrice, bool isAskPrice = true);
}

public class ClientAPI : IClient
{
    private HashSet<Stock> _tradingOptions;
    private readonly List<Delegate> _clients;
    private const string Id = "clientAPI";
    private readonly IObservable _observable;
    private readonly ConcurrentDictionary<Guid, ClientData> _clientDatas;

    public ClientAPI(IObservable observable)
    {
        _tradingOptions = new HashSet<Stock>();
        _clients = new List<Delegate>();
        _clientDatas = new ConcurrentDictionary<Guid, ClientData>();
        _observable = observable;
        var topic = TopicGenerator.TopicForAllInstruments();
        _observable.Subscribe<HashSet<Stock>>(topic, Id, stockOptions =>
        {
            _tradingOptions = stockOptions;
            foreach (var client in _clients)
            {
                client.DynamicInvoke(stockOptions);
            }
        });
    }

    public HashSet<Stock> GetStockOptions<T>(Action<T> client)
    {
        _clients.Add(client);
        return _tradingOptions;
    }

    public void StreamPrice(StreamInformation info, Action<Stock> updatePrice, bool isAskPrice = true)
    {
        var stockTopic = TopicGenerator.TopicForClientInstrumentPrice(info.InstrumentId);
        if (info.EnableLivePrices)
        {
            _observable.Subscribe<Stock>(stockTopic, info.ClientId.ToString(), stock =>
            {
                var clientTier = _clientDatas[info.ClientId].Tier;
                var (bid, ask) = SpreadCalculator.GetBidAsk(stock.Price, clientTier);
                
                stock.Price = isAskPrice ? ask : bid;
                updatePrice.DynamicInvoke(stock);
            });
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
        var clientTier = _clientDatas[order.ClientId].Tier;
        var spreadProcent = SpreadCalculator.GetSpreadPercentage(clientTier);

        if (order.Side == OrderSide.RightSided)
        {
            // Buy
            var priceWithSpread = order.Stock.Price;
            order.Stock.Price = priceWithSpread * (1.0f / (1.0f + spreadProcent));
            order.SpreadPrice = priceWithSpread - order.Stock.Price;
        }
        else
        {
            var priceWithSpread = order.Stock.Price;
            order.Stock.Price = priceWithSpread * (1.0f / (1.0f - spreadProcent));
            order.SpreadPrice = order.Stock.Price - priceWithSpread;
        }
        
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
                _observable.Subscribe<ClientData>(topic, Id, cD =>
                {
                    _clientDatas.AddOrUpdate(cD.ClientId, cD, (key, oldValue) => cD);
                    callbackClientData.DynamicInvoke(cD);
                });
            }
            callbackLogin?.DynamicInvoke(info);
        });
        _observable.Publish(requestTopic, info, isTransient: true);
    }
    

    public void Logout(Action<bool> callback)
    {
        callback.Invoke(false);
    }
}

internal class SpreadCalculator
{
    private static readonly Dictionary<Tier, float> SpreadPercentages = new()
    {
        { Tier.External, 0.005f },  // 0.5%
        { Tier.Internal, 0.001f },  // 0.1%
        { Tier.Regular, 0.002f },   // 0.2%
        { Tier.Premium, 0.0005f }   // 0.05%
    };
    
    public static float GetSpreadPercentage(Tier tier)
    {
        // TODO deal with tier not existing
        return SpreadPercentages.GetValueOrDefault(tier, 0.0f);
    }
    
    public static (float Bid, float Ask) GetBidAsk(float midPrice, Tier tier)
    {
        var spread = midPrice * GetSpreadPercentage(tier);
        var ask = midPrice + (spread / 2.0f);
        var bid = midPrice - (spread / 2.0f);
        return (bid, ask);
    }
}