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
    
    public void DestroyClientConsumers(Guid clientId, string clientUsername);

    public void Start();
    public void Stop();
}

public class ClientAPI : IClient
{
    private HashSet<Stock> _tradingOptions;
    private readonly List<Delegate> _clients;
    private const string Id = "clientAPI";
    private readonly IObservable _observable;
    private readonly ConcurrentDictionary<Guid, ClientData> _clientDatas;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokenSources;
    
    private INatsClient _natsClient;
    private readonly Dictionary<string, CancellationTokenSource> _subscriptionTokens = new();
    private readonly List<Task> _subscriptionTasks = new();

    public ClientAPI(IObservable observable, NatsClient natsClient)
    {
        _tradingOptions = new HashSet<Stock>();
        _clients = new List<Delegate>();
        _clientDatas = new ConcurrentDictionary<Guid, ClientData>();
        _cancellationTokenSources = new ConcurrentDictionary<string, CancellationTokenSource>();
        _observable = observable;
        _natsClient = natsClient;
        
        SetupConsumers();
    }

    private async void SetupConsumers()
    {
        var consumerConfig = new ConsumerConfig
        {
            Name = "ClientAPIAllInstrumentsConsumer",
            DurableName = "ClientAPIAllInstrumentsConsumer",
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            DeliverGroup = "ClientAPI",
            FilterSubject = TopicGenerator.TopicForAllInstruments()
        };
        
        var stream = "StreamMisc";
        
        await _natsClient.CreateJetStreamContext().CreateOrUpdateConsumerAsync(stream, consumerConfig);
        Start();
    }

    public HashSet<Stock> GetStockOptions<T>(Action<T> client)
    {
        _clients.Add(client);
        Console.WriteLine("Returning trading options " + _tradingOptions.Count );
        return _tradingOptions;
    }

    public async void StreamPrice(StreamInformation info, Action<Stock> updatePrice, bool isAskPrice = true)
    {
        var stockTopic = TopicGenerator.TopicForClientInstrumentPrice(info.InstrumentId);
        var ctx = _natsClient.CreateJetStreamContext();
        var type = isAskPrice ? "Ask" : "Bid";
        var keyName = info.ClientId + info.InstrumentId + type;
        var durableName = $"clientPrices_{keyName}";
        var streamName = "StreamClientPrices";

        if (info.EnableLivePrices)
        {
            if (_cancellationTokenSources.TryRemove(keyName, out var existingCts))
            {
                await existingCts.CancelAsync();
                existingCts.Dispose();
                try { await ctx.DeleteConsumerAsync(streamName, durableName); } catch { }
                Console.WriteLine($"Restarting stream for {keyName}");
            }

            var cts = new CancellationTokenSource();
            _cancellationTokenSources.TryAdd(keyName, cts);

            try
            {
                var consumerConfig = new ConsumerConfig
                {
                    Name = durableName,
                    DurableName = durableName,
                    DeliverPolicy = ConsumerConfigDeliverPolicy.Last,
                    FilterSubject = $"clientPrices.{info.InstrumentId}",
                    DeliverGroup = "ClientAPI",
                    AckPolicy = ConsumerConfigAckPolicy.Explicit
                };

                var consumer = await ctx.CreateOrUpdateConsumerAsync(streamName, consumerConfig, cts.Token);

                await foreach (var msg in consumer.ConsumeAsync<Stock>(cancellationToken: cts.Token))
                {
                    await msg.AckAsync(cancellationToken: cts.Token);

                    var localStock = (Stock)msg.Data.Clone();
                    var tier = _clientDatas[info.ClientId].Tier;
                    var (bid, ask) = SpreadCalculator.GetBidAsk(localStock.Price, tier);
                    localStock.Price = isAskPrice ? ask : bid;

                    updatePrice.Invoke(localStock);
                }

            }
            catch (OperationCanceledException)
            {
                // Expected on unsubscribe
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Stream error: {ex.Message}");
            }
            finally
            {
                _cancellationTokenSources.TryRemove(keyName, out _);
                try { await ctx.DeleteConsumerAsync(streamName, durableName, cts.Token); } catch { }
            }
        }
        else
        {
            if (_cancellationTokenSources.Remove(keyName, out var cts))
            {
                await cts.CancelAsync();
                Console.WriteLine($"Client Cancelled for {keyName}");
            }
            else
            {
                Console.WriteLine($"No active stream found for {keyName}");
            }

            try { await ctx.DeleteConsumerAsync(streamName, durableName); } catch { }
        }
    }

    public async void DestroyClientConsumers(Guid clientId, string clientUsername)
    {
        var durableNameDB = clientId + "DBConsumer";
        var streamDB = "StreamDBData";

        try
        {
            await _natsClient.CreateJetStreamContext().DeleteConsumerAsync(streamDB, durableNameDB);

        }
        catch
        {
            Console.WriteLine($"Failed to delete client consumers for {durableNameDB}");
        }
    }

    public void Start()
    {
        var topic = TopicGenerator.TopicForAllInstruments();
        // _observable.Subscribe<HashSet<Stock>>(topic, Id, stockOptions =>
        // {
        //     _tradingOptions = stockOptions;
        //     foreach (var client in _clients)
        //     {
        //         client.DynamicInvoke(stockOptions);
        //     }
        // });
        
        if (_subscriptionTokens.TryGetValue(topic, out var existingCts))
        {
            existingCts.Cancel();
            _subscriptionTokens.Remove(topic);
        }

        var cts = new CancellationTokenSource();
        _subscriptionTokens[topic] = cts;
        
        var task = Task.Run(async () =>
        {
            try
            {
                var consumer = await _natsClient.CreateJetStreamContext().GetConsumerAsync("StreamMisc", "ClientAPIAllInstrumentsConsumer", cts.Token);
                await foreach (var msg in consumer.ConsumeAsync<HashSet<Stock>>(cancellationToken: cts.Token))
                {
                    //await msg.AckProgressAsync(); TODO figure out time
                    Console.WriteLine("Client api got All Instruments " + msg.Data.Count);
                    _tradingOptions = msg.Data;
                    foreach (var client in _clients)
                    {
                        client.DynamicInvoke(msg.Data);
                    }
                    await msg.AckAsync(cancellationToken: cts.Token);
                }
            }
            catch (OperationCanceledException) {  }
        }, cts.Token);
        _subscriptionTasks.Add(task);
        
    }

    public async void Stop()
    {
        var keys = _subscriptionTokens.Keys.ToList(); 

        foreach (var key in keys)
        {
            if (_subscriptionTokens.TryGetValue(key, out var cts))
            {
                await cts.CancelAsync();
            }
        }

        _subscriptionTokens.Clear();
    }


    public async void HandleOrder(Order order, Action<Order> callback)
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
        
        // _observable.Subscribe<Order>(topicToSubscribe, Id, ord =>
        // {
        //     var ordLocal = (Order) ord.Clone();
        //     callback.DynamicInvoke(ordLocal);
        // });
        
        var consumerConfig = new ConsumerConfig
        {
            Name = "buyOrderEndedConsumer",
            DurableName = "buyOrderEndedConsumer",
            DeliverGroup = "ClientAPI",
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            FilterSubject = topicToSubscribe
        };
        
        var stream = "streamOrders";
        
        var consumer = await _natsClient.CreateJetStreamContext().CreateOrUpdateConsumerAsync(stream, consumerConfig);
        if (_subscriptionTokens.TryGetValue(topicToSubscribe, out var existingCts))
        {
            existingCts.Cancel();
            _subscriptionTokens.Remove(topicToSubscribe);
        }
        
        var cts = new CancellationTokenSource();
        _subscriptionTokens[topicToSubscribe] = cts;
        
        var task = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in consumer.ConsumeAsync<Order>(cancellationToken: cts.Token))
                {
                    //await msg.AckProgressAsync(); TODO figure out time
                    var ordLocal = (Order) msg.Data.Clone();
                    callback.DynamicInvoke(ordLocal);
                    await msg.AckAsync(cancellationToken: cts.Token);
                }
            }
            catch (OperationCanceledException) {  }
        }, cts.Token);
        _subscriptionTasks.Add(task);
        
        //_observable.Publish(topicToPublish, localOrder, isTransient: true);
        Console.WriteLine("ClientAPI order sent with: " + order.ClientId);
        await _natsClient.PublishAsync<Order>(topicToPublish, localOrder);
    }

    public async void Login(string username, string password, Action<LoginInfo> callbackLogin, Action<ClientData> callbackClientData)
    {
        var requestTopic = TopicGenerator.TopicForLoginRequest();
        var responseTopic = TopicGenerator.TopicForLoginResponse();
        var info = new LoginInfo
        {
            Username = username,
            Password = password
        };
        // _observable.Subscribe<LoginInfo>(responseTopic, Id, info =>
        // {
        //     if (info.IsAuthenticated)
        //     {
        //         var topic = TopicGenerator.TopicForDBDataOfClient(info.ClientId.ToString());
        //         _observable.Subscribe<ClientData>(topic, Id, cD =>
        //         {
        //             _clientDatas.AddOrUpdate(cD.ClientId, cD, (key, oldValue) => cD);
        //             callbackClientData.DynamicInvoke(cD);
        //         });
        //     }
        //     callbackLogin?.DynamicInvoke(info);
        // });
        
        var consumerConfig = new ConsumerConfig
        {
            Name = "loginRequestEndedConsumer" + username,
            DurableName = "loginRequestEndedConsumer" + username,
            DeliverGroup = "ClientAPI" + username,
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            FilterSubject = responseTopic
        };
        
        var stream = "streamLoginRequest";
        
        var consumer = await _natsClient.CreateJetStreamContext().CreateOrUpdateConsumerAsync(stream, consumerConfig);
        if (_subscriptionTokens.TryGetValue(responseTopic, out var existingCts))
        {
            existingCts.Cancel();
            _subscriptionTokens.Remove(responseTopic);
        }
        
        var cts = new CancellationTokenSource();
        _subscriptionTokens[responseTopic] = cts;
        
        var task = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in consumer.ConsumeAsync<LoginInfo>(cancellationToken: cts.Token))
                {
                    //await msg.AckProgressAsync(); TODO figure out time
                    info = msg.Data;
                    if (info.IsAuthenticated)
                    {
                        Console.WriteLine($"User {info.Username} logged in");
                        var topic = TopicGenerator.TopicForDBDataOfClient(info.ClientId.ToString());
                        var consumerConfig2 = new ConsumerConfig
                        {
                            Name = info.ClientId + "DBConsumer",
                            DurableName = info.ClientId + "DBConsumer",
                            DeliverGroup = info.ClientId + "DBConsumer",
                            DeliverPolicy = ConsumerConfigDeliverPolicy.LastPerSubject,
                            AckPolicy = ConsumerConfigAckPolicy.Explicit,
                            FilterSubject = topic
                        };
        
                        var stream2 = "StreamDBData";
        
                        var consumer2 = await _natsClient.CreateJetStreamContext().CreateOrUpdateConsumerAsync(stream2, consumerConfig2);
                        if (_subscriptionTokens.TryGetValue(topic, out var existingCtss))
                        {
                            existingCtss.Cancel();
                            _subscriptionTokens.Remove(topic);
                        }
        
                        var ctss = new CancellationTokenSource();
                        _subscriptionTokens[topic] = ctss;
                        var task = Task.Run(async () =>
                        {
                            try
                            {
                                await foreach (var msg2 in consumer2.ConsumeAsync<ClientData>(cancellationToken: cts.Token))
                                {
                                    Console.WriteLine("Got client data");
                                    //await msg.AckProgressAsync(); TODO figure out time
                                    var cD = msg2.Data;
                                    _clientDatas.AddOrUpdate(cD.ClientId, cD, (key, oldValue) => cD);
                                    callbackClientData.DynamicInvoke(cD);
                                    await msg2.AckAsync(cancellationToken: cts.Token);
                                }
                            }
                            catch (OperationCanceledException) {  }
                        }, cts.Token);
                        _subscriptionTasks.Add(task);
                        // _observable.Subscribe<ClientData>(topic, Id, cD =>
                        // {
                        //     _clientDatas.AddOrUpdate(cD.ClientId, cD, (key, oldValue) => cD);
                        //     callbackClientData.DynamicInvoke(cD);
                        // });
                    }
                    callbackLogin?.DynamicInvoke(info);
                    await msg.AckAsync(cancellationToken: cts.Token);
                }
            }
            catch (OperationCanceledException) {  }
        }, cts.Token);
        _subscriptionTasks.Add(task);
        
        //_observable.Publish(requestTopic, info, isTransient: true);
        await _natsClient.PublishAsync(requestTopic, info);
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