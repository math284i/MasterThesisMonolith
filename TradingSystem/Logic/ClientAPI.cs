using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;
using TradingSystem.Data;
using TradingSystem.DTO;
using Tier = TradingSystem.Data.Tier;

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
    private INatsClient _natsClient;

    public ClientAPI(IObservable observable, NatsClient natsClient)
    {
        _tradingOptions = new HashSet<Stock>();
        _clients = new List<Delegate>();
        _clientDatas = new ConcurrentDictionary<Guid, ClientData>();
        _observable = observable;
        _natsClient = natsClient;
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

    public async void StreamPrice(StreamInformation info, Action<Stock> updatePrice, bool isAskPrice = true)
    {
        var stockTopic = TopicGenerator.TopicForClientInstrumentPrice(info.InstrumentId);
        if (info.EnableLivePrices)
        {
            var type = isAskPrice ? "Ask" : "Bid";
            var cts = CancellationToken.None;
            var ctx = _natsClient.CreateJetStreamContext();
            // var consumer = await ctx.GetConsumerAsync("StreamPrices", "clientPrices_GME", cts);
            // await foreach (var msg in consumer.ConsumeAsync<Stock>(cancellationToken: cts))
            // {
            //     var data = msg.Data;
            //     Console.WriteLine($"Received: {data}");
            //     await msg.AckAsync(cancellationToken: cts);
            // }
            // var ctx = _natsClient.CreateJetStreamContext();
            // var config = new ConsumerConfig
            // {
            //     FilterSubjects = [stockTopic],
            //     DurableName = info.ClientId + stockTopic,
            //     Name = info.ClientId + stockTopic
            // };
            //
            // var consumer = await ctx.CreateOrUpdateConsumerAsync("StreamPrices", config, cts.Token);

            // _natsClient.SubscribeAsync();
            // var subscriptionTask = Task.Run(async () =>
            // {
            //     await foreach (var msg in _natsClient.SubscribeAsync<Stock>(stockTopic, cancellationToken: cts.Token))
            //     {
            //         var order = msg.Data;
            //         Console.WriteLine($"Subscriber received {msg.Subject}: {order}");
            //         var localStock = (Stock)order.Clone();
            //         var clientTier = _clientDatas[info.ClientId].Tier;
            //         var (bid, ask) = SpreadCalculator.GetBidAsk(localStock.Price, clientTier);
            //
            //         localStock.Price = isAskPrice ? ask : bid;
            //         updatePrice.Invoke(localStock);
            //     }
            //     Console.WriteLine("Unsubscribed");
            // }, cts.Token)
            // await subscriptionTask;
            // var js = _natsClient.CreateJetStreamContext();
            // var consumer = await js.GetConsumerAsync("StreamPrices", "clientPrices_1234", cts);
            // await foreach (var msg in consumer.ConsumeAsync<Stock>(cancellationToken: cts))
            // {
            //     var data = msg.Data;
            //     Console.WriteLine($"CLIENT Received: {data}");
            //     await msg.AckAsync(cancellationToken: cts);
            // }
        }
        else
        {
            _observable.Unsubscribe(stockTopic, info.ClientId.ToString());
        }
    }

    public void HandleOrder(Order order, Action<Order> callback)
    {
        var localOrder = (Order) order.Clone();
        var topicToPublish = TopicGenerator.TopicForClientBuyOrder();
        var topicToSubscribe = TopicGenerator.TopicForClientOrderEnded(localOrder.ClientId.ToString());
        var clientTier = _clientDatas[localOrder.ClientId].Tier;
        var spreadProcent = SpreadCalculator.GetSpreadPercentage(clientTier);

        if (localOrder.Side == OrderSide.RightSided)
        {
            // Buy
            var priceWithSpread = localOrder.Stock.Price;
            localOrder.Stock.Price = priceWithSpread * (1.0m / (1.0m + spreadProcent));
            localOrder.SpreadPrice = priceWithSpread - localOrder.Stock.Price;
        }
        else
        {
            var priceWithSpread = localOrder.Stock.Price;
            Console.WriteLine($"pws {priceWithSpread}");
            localOrder.Stock.Price = priceWithSpread * (1.0m / (1.0m - spreadProcent));
            localOrder.SpreadPrice = localOrder.Stock.Price - priceWithSpread;
        }
        
        _observable.Subscribe<Order>(topicToSubscribe, Id, ord =>
        {
            var ordLocal = (Order) ord.Clone();
            callback.DynamicInvoke(ordLocal);
        });
        
        _observable.Publish(topicToPublish, localOrder, isTransient: true);
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
    private static readonly Dictionary<Tier, decimal> SpreadPercentages = new()
    {
        { Tier.External, 0.005m },  // 0.5%
        { Tier.Internal, 0.001m },  // 0.1%
        { Tier.Regular, 0.002m },   // 0.2%
        { Tier.Premium, 0.0005m }   // 0.05%
    };
    
    public static decimal GetSpreadPercentage(Tier tier)
    {
        // TODO deal with tier not existing
        return SpreadPercentages.GetValueOrDefault(tier, 0.0m);
    }
    
    public static (decimal Bid, decimal Ask) GetBidAsk(decimal midPrice, Tier tier)
    {
        var spread = midPrice * GetSpreadPercentage(tier);
        var ask = midPrice + (spread);
        var bid = midPrice - (spread);
        return (bid, ask);
    }
}