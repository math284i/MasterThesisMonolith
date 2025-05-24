using System.Collections.Concurrent;
using NATS.Client.JetStream.Models;
using NATS.Net;
using TradingSystem.Components.Pages;
using TradingSystem.Data;

namespace TradingSystem.Logic;

public interface IRiskCalculator
{
    public void Start();
    public void Stop();
}

public class RiskCalculator : IRiskCalculator
{
    private readonly IObservable _observable;
    private const string Id = "riskCalculator";
    private const string DanskeBankClientName = "Danske_Bank";
    private List<ClientData> _clients = new List<ClientData>();
    private ConcurrentDictionary<Guid, ClientData> _clientDatas = new ConcurrentDictionary<Guid, ClientData>();

    private readonly ConcurrentDictionary<string, TargetPosition> _targetPositions =
        new ConcurrentDictionary<string, TargetPosition>();
    private readonly ILogger<RiskCalculator> _logger;
    
    private readonly NatsClient _natsClient;
    private readonly Dictionary<string, CancellationTokenSource> _subscriptionTokens = new();
    private readonly List<Task> _subscriptionTasks = new();

    public RiskCalculator(IObservable observable, ILogger<RiskCalculator> logger, NatsClient natsClient)
    {
        _observable = observable;
        _logger = logger;
        _natsClient = natsClient;
    }
    
    public void Start()
    {
        SetupConsumers();
    }

    private async void SetupConsumers()
    {
        var consumerConfig = new ConsumerConfig
        {
            Name = "buyOrderConsumer",
            DurableName = "buyOrderConsumer",
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            DeliverGroup = "RiskCalculator",
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            FilterSubject = TopicGenerator.TopicForClientBuyOrder()
        };
        
        var stream = "streamOrders";
        var allClientsConfig = new ConsumerConfig
        {
            Name = "allClientsConsumer",
            DurableName = "allClientsConsumer",
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            DeliverGroup = "RiskCalculator",
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            FilterSubject = TopicGenerator.TopicForAllClients()
        };
        
        var streamMisc = "StreamMisc";
        
        var allTargetsConfig = new ConsumerConfig
        {
            Name = "allTargetPositionsConsumer",
            DurableName = "allTargetPositionsConsumer",
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            DeliverGroup = "RiskCalculator",
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            FilterSubject = TopicGenerator.TopicForAllTargetPositions()
        };
        
        
        await _natsClient.CreateJetStreamContext().CreateOrUpdateConsumerAsync(stream, consumerConfig);
        await _natsClient.CreateJetStreamContext().CreateOrUpdateConsumerAsync(streamMisc, allClientsConfig);
        await _natsClient.CreateJetStreamContext().CreateOrUpdateConsumerAsync(streamMisc, allTargetsConfig);
        
        SubscribeToAllClients();

        SubscribeToAllTargets();

        SubscribeToBuyOrders();
    }

    private async void SubscribeToAllClients()
    {
        var topicForClients = TopicGenerator.TopicForAllClients();

        // _observable.Subscribe<List<ClientData>>(topicForClients, Id, clients =>
        // {
        //     foreach (var client in clients.Where(client => !_clients.Contains(client)))
        //     {
        //         _clients.Add(client);
        //         var topicForClientData = TopicGenerator.TopicForDBDataOfClient(client.ClientId.ToString());
        //
        //         _observable.Subscribe<ClientData>(topicForClientData, Id, clientData =>
        //         {
        //             _clientDatas.AddOrUpdate(client.ClientId, clientData, (key, oldValue) => clientData);
        //         });
        //     }
        // });
        
        if (_subscriptionTokens.TryGetValue(topicForClients, out var existingCts))
        {
            existingCts.Cancel();
            _subscriptionTokens.Remove(topicForClients);
        }
        
        var cts = new CancellationTokenSource();
        _subscriptionTokens[topicForClients] = cts;
        
        var task = Task.Run(async () =>
        {
            try
            {
                var consumer = await _natsClient.CreateJetStreamContext().GetConsumerAsync("StreamMisc", "allClientsConsumer", cts.Token);
                await foreach (var msg in  consumer.ConsumeAsync<List<ClientData>>(cancellationToken: cts.Token))
                {
                    var clients = msg.Data;
                    foreach (var client in clients.Where(client => !_clients.Contains(client)))
                    {
                        _clients.Add(client);
                        var topicForClientData = TopicGenerator.TopicForDBDataOfClient(client.ClientId.ToString());
                        if (_subscriptionTokens.TryGetValue(topicForClientData, out var existingCtss))
                        {
                            await existingCtss.CancelAsync();
                            _subscriptionTokens.Remove(topicForClientData);
                        }
        
                        var ctss = new CancellationTokenSource();
                        _subscriptionTokens[topicForClientData] = ctss;
                        
                        var clientDataConfig = new ConsumerConfig
                        {
                            Name = "clientDataConsumer" + client.ClientId.ToString(),
                            DurableName = "clientDataConsumer" + client.ClientId.ToString(),
                            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
                            DeliverGroup = "RiskCalculator",
                            AckPolicy = ConsumerConfigAckPolicy.Explicit,
                            FilterSubject = topicForClientData
                        };
        
                        var stream = "StreamDBData";
                        var consumerCD = await _natsClient.CreateJetStreamContext().CreateOrUpdateConsumerAsync(stream, clientDataConfig);
                        
                        var task = Task.Run(async () =>
                        {
                            try
                            {
                                await foreach (var natsMsg in consumerCD.ConsumeAsync<ClientData>(cancellationToken: ctss.Token))
                                {
                                    var clientData = natsMsg.Data;
                                    if (clientData != null)
                                        _clientDatas.AddOrUpdate(client.ClientId, clientData,
                                            (key, oldValue) => clientData);
                                    await natsMsg.AckAsync(cancellationToken: ctss.Token);
                                }
                            }
                            catch (OperationCanceledException) { Console.WriteLine($"Stopped subscription: {topicForClientData}"); }
                        }, ctss.Token);
                        _subscriptionTasks.Add(task);
                    }

                    await msg.AckAsync(cancellationToken: cts.Token);
                }
            }
            catch (OperationCanceledException) { Console.WriteLine($"Stopped subscription: {topicForClients}"); }
        }, cts.Token);
        
        _subscriptionTasks.Add(task);
    }
    private void SubscribeToAllTargets()
    {
        var topicAllTargets = TopicGenerator.TopicForAllTargetPositions();
        
        if (_subscriptionTokens.TryGetValue(topicAllTargets, out var existingCts))
        {
            existingCts.Cancel();
            _subscriptionTokens.Remove(topicAllTargets);
        }
        
        var cts = new CancellationTokenSource();
        _subscriptionTokens[topicAllTargets] = cts;
        
        var task = Task.Run(async () =>
        {
            try
            {
                var consumer = await _natsClient.CreateJetStreamContext().GetConsumerAsync("StreamMisc", "allTargetPositionsConsumer", cts.Token);
                await foreach (var msg in  consumer.ConsumeAsync<List<TargetPosition>>(cancellationToken: cts.Token))
                {
                    var targets = msg.Data;
                    if (targets != null)
                    {
                        foreach (var target in targets.Where(target =>
                                     !_targetPositions.ContainsKey(target.InstrumentId)))
                        {
                            _targetPositions.AddOrUpdate(target.InstrumentId, target, (key, oldValue) => target);
                            var topicForTarget = TopicGenerator.TopicForTargetPositionUpdate(target.InstrumentId);

                            var ctss = new CancellationTokenSource();
                            _subscriptionTokens[topicForTarget] = ctss;

                            var clientDataConfig = new ConsumerConfig
                            {
                                Name = "targetDataConsumer" + target.InstrumentId,
                                DurableName = "targetDataConsumer" + target.InstrumentId,
                                DeliverPolicy = ConsumerConfigDeliverPolicy.All,
                                DeliverGroup = "RiskCalculator",
                                AckPolicy = ConsumerConfigAckPolicy.Explicit,
                                FilterSubject = topicForTarget
                            };

                            var stream = "StreamDBData";
                            var consumerCD = await _natsClient.CreateJetStreamContext()
                                .CreateOrUpdateConsumerAsync(stream, clientDataConfig, ctss.Token);

                            var task = Task.Run(async () =>
                            {
                                try
                                {
                                    await foreach (var natsMsg in consumerCD.ConsumeAsync<TargetPosition>(
                                                       cancellationToken: ctss.Token))
                                    {
                                        var targetPosition = natsMsg.Data;
                                        if (targetPosition != null)
                                            _targetPositions.AddOrUpdate(targetPosition.InstrumentId, targetPosition,
                                                (key, oldValue) => targetPosition);
                                        await natsMsg.AckAsync(cancellationToken: ctss.Token);
                                    }
                                }
                                catch (OperationCanceledException)
                                {
                                    Console.WriteLine($"Stopped subscription: {topicForTarget}");
                                }
                            }, ctss.Token);
                            _subscriptionTasks.Add(task);
                        }
                        await msg.AckAsync(cancellationToken: cts.Token);
                    }
                }
            }
            catch (OperationCanceledException) { Console.WriteLine($"Stopped subscription: {topicAllTargets}"); }
        }, cts.Token);
        
        _subscriptionTasks.Add(task);
    }
    private void SubscribeToBuyOrders()
    {
        var topic = TopicGenerator.TopicForClientBuyOrder();
        //_observable.Subscribe<Order>(topic, Id, CheckOrder);
        
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
                var consumer = await _natsClient.CreateJetStreamContext().GetConsumerAsync("streamOrders", "buyOrderConsumer", cts.Token);
                await foreach (var msg in consumer.ConsumeAsync<Order>(cancellationToken: cts.Token))
                {
                    //await msg.AckProgressAsync(); TODO figure out time
                    CheckOrder(msg.Data);
                    await msg.AckAsync(cancellationToken: cts.Token);
                }
            }
            catch (OperationCanceledException) {  }
        }, cts.Token);
        _subscriptionTasks.Add(task);
    }

    private async void CheckOrder(Order order)
    {
        _clientDatas.TryGetValue(order.ClientId, out var clientData);
        if (clientData == null)
        {
            _logger.LogError("Risk calculator, Client {ClientId} not found", order.ClientId);
            return;
        }

        
        order.HedgeOrder = ShouldWeHedge(order);

        if (order.Side == OrderSide.RightSided)
        {
            // Buy
            var price = (order.Stock.Price + order.SpreadPrice) * order.Stock.Size;
            if (clientData.Balance >= price)
            {
                _logger.LogInformation("RiskCalculator accepting order");
                var topic = TopicGenerator.TopicForClientBuyOrderApproved();
                //_observable.Publish(topic, order, isTransient: true);
                await _natsClient.PublishAsync(topic, order);
            }
            else
            {
                _logger.LogInformation("RiskCalculator Rejected order");
                var topic = TopicGenerator.TopicForClientOrderEnded(order.ClientId.ToString());
                order.Status = OrderStatus.Rejected;
                order.ErrorMesssage = "Insufficient Funds";
                //_observable.Publish(topic, order, isTransient: true);
                await _natsClient.PublishAsync(topic, order);
            }
        }
        else
        {
            // Sell
            var holding = _clientDatas[order.ClientId].Holdings.Find(h => h.InstrumentId == order.Stock.InstrumentId);
            var topic = "";
            if (order.SpreadPrice > order.Stock.Price)
            {
                // Spread was bigger than 100 %, shouldn't be fair
                topic = TopicGenerator.TopicForClientOrderEnded(order.ClientId.ToString());
                order.Status = OrderStatus.Rejected;
                order.ErrorMesssage = "Spread is too big";
                //_observable.Publish(topic, order, isTransient: true);
                await _natsClient.PublishAsync(topic, order);
                return;
            }
            
            if (holding != null)
            {
                if (holding.Size >= order.Stock.Size)
                {
                    topic = TopicGenerator.TopicForClientBuyOrderApproved();
                }
                else
                {
                    topic = TopicGenerator.TopicForClientOrderEnded(order.ClientId.ToString());
                    order.Status = OrderStatus.Rejected;
                    order.ErrorMesssage = $"Client doesn't contain enough of the stock {order.Stock.InstrumentId}";
                }
            }
            else
            {
                topic = TopicGenerator.TopicForClientOrderEnded(order.ClientId.ToString());
                order.Status = OrderStatus.Rejected;
                order.ErrorMesssage = $"Client doesn't own any stock of {order.Stock.InstrumentId}";
            }
            //_observable.Publish(topic, order, isTransient: true);
            await _natsClient.PublishAsync(topic, order);
        }
    }

    private bool ShouldWeHedge(Order order)
    {
        var danskeBank = _clients.Find(c => c.Name == DanskeBankClientName);
        if (danskeBank == null) return true;
        var danskeData = _clientDatas[danskeBank.ClientId];
        var danskeStock = danskeData.Holdings.Find(h => h.InstrumentId == order.Stock.InstrumentId);
        if (danskeStock == null) return true;
        var targetPosition = _targetPositions[order.Stock.InstrumentId]; //Should be a check if we have the key in the list.
        var shouldWeHedge = targetPosition.Type switch
        {
            TargetType.FOK => ShouldWeHedgeFOK(order, targetPosition, danskeStock),
            TargetType.IOC => true,
            TargetType.GTC => true,
            TargetType.GFD => true,
            _ => throw new ArgumentOutOfRangeException()
        };
        return shouldWeHedge;
    }

    private bool ShouldWeHedgeFOK(Order order, TargetPosition targetPosition, HoldingData danskeStock)
    {
        if (order.Side == OrderSide.RightSided)
        {
            // TODO ask Nikodem about target type, and below code.
            // Buy
            if (targetPosition.Target >= danskeStock.Size) return true;
            return danskeStock.Size < order.Stock.Size;
        }
        else
        {
            // Sell
            return targetPosition.Target <= danskeStock.Size;
        }
    }

    public async void Stop()
    {
        var topic = TopicGenerator.TopicForClientBuyOrder();
        _observable.Unsubscribe(topic, Id);
        var topicForClients = TopicGenerator.TopicForAllClients();
        _observable.Unsubscribe(topicForClients, Id);

        foreach (var client in _clients)
        {
            var topicForClientData = TopicGenerator.TopicForDBDataOfClient(client.ClientId.ToString());

            _observable.Unsubscribe(topicForClientData, Id);
        }
        _clients = new List<ClientData>();
        _clientDatas = new ConcurrentDictionary<Guid, ClientData>();
        
        var keys = _subscriptionTokens.Keys.ToList(); 

        foreach (var key in keys)
        {
            if (_subscriptionTokens.TryGetValue(key, out var cts))
            {
                await cts.CancelAsync();
            }
        }

        _subscriptionTokens.Clear();
        await Task.WhenAll(_subscriptionTasks);

    }
}