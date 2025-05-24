using System.Collections.Concurrent;
using NATS.Client.JetStream.Models;
using NATS.Net;
using TradingSystem.Data;

namespace TradingSystem.Logic;

public interface IExecutionHandler
{
    public void Start();
    
    public void Stop();
}

public class ExecutionHandler : IExecutionHandler
{
    private readonly ILogger<ExecutionHandler> _logger;
    private const string Id = "executionHandler";
    private readonly IObservable _observable;
    private HashSet<Stock> _stockOptions = new HashSet<Stock>();
    private ConcurrentDictionary<string, Stock> _prices = new();
    
    private readonly NatsClient _natsClient;
    private readonly Dictionary<string, CancellationTokenSource> _subscriptionTokens = new();
    private readonly List<Task> _subscriptionTasks = new();
    
    public ExecutionHandler(ILogger<ExecutionHandler> logger, IObservable observable, NatsClient natsClient)
    {
        _logger = logger;
        _observable = observable;
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
            Name = "buyOrderApprovedConsumer",
            DurableName = "buyOrderApprovedConsumer",
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            DeliverGroup = "ExecutionHandler",
            FilterSubject = TopicGenerator.TopicForClientBuyOrderApproved()
        };
        var hedgeConsumerConfig = new ConsumerConfig
        {
            Name = "hedgeOrderResponseConsumer",
            DurableName = "hedgeOrderResponseConsumer",
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            DeliverGroup = "ExecutionHandler",
            FilterSubject = TopicGenerator.TopicForHedgingOrderResponse()
        };
        
        var stream = "streamOrders";
        
        var allInstrumentsConfig = new ConsumerConfig
        {
            Name = "ExecutionHandlerAllInstrumentsConsumer",
            DurableName = "ExecutionHandlerAllInstrumentsConsumer",
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            DeliverGroup = "ExecutionHandler",
            FilterSubject = TopicGenerator.TopicForAllInstruments()
        };
        
        var streamMisc = "StreamMisc";
        
        
        await _natsClient.CreateJetStreamContext().CreateOrUpdateConsumerAsync(stream, consumerConfig);
        await _natsClient.CreateJetStreamContext().CreateOrUpdateConsumerAsync(stream, hedgeConsumerConfig);
        await _natsClient.CreateJetStreamContext().CreateOrUpdateConsumerAsync(streamMisc, allInstrumentsConfig);
        
        SubscribeToBuyOrderApproved();
        SubscribeToHedgeResponse();
        SetupClientStockPrices();
    }

    private void SetupClientStockPrices()
    {
        var topicInstruments = TopicGenerator.TopicForAllInstruments();
        if (_subscriptionTokens.TryGetValue(topicInstruments, out var existingCts))
        {
            existingCts.Cancel();
            _subscriptionTokens.Remove(topicInstruments);
        }

        var cts = new CancellationTokenSource();
        _subscriptionTokens[topicInstruments] = cts;
        
        var task = Task.Run(async () =>
        {
            try
            {
                var consumer = await _natsClient.CreateJetStreamContext()
                    .GetConsumerAsync("StreamMisc", "ExecutionHandlerAllInstrumentsConsumer", cts.Token);
                await foreach (var msg in consumer.ConsumeAsync<HashSet<Stock>>(cancellationToken: cts.Token))
                {
                    Console.WriteLine("Execution handler got all instruments count " + msg.Data.Count);
                    //await msg.AckProgressAsync(); TODO figure out time
                    _stockOptions = new HashSet<Stock>();
                    foreach (var localStock in msg.Data.Select(stock => (Stock) stock.Clone()))
                    {
                        _stockOptions.Add(localStock);
                    }
                    foreach (var stock in _stockOptions)
                    {
                        var topic = TopicGenerator.TopicForClientInstrumentPrice(stock.InstrumentId);
                       if (_subscriptionTokens.TryGetValue(topic, out var existingCtss))
                       {
                           await existingCtss.CancelAsync();
                           _subscriptionTokens.Remove(topic);
                       }
        
                       var ctss = new CancellationTokenSource();
                       _subscriptionTokens[topic] = ctss;
                        
                       var clientPriceConfig = new ConsumerConfig
                       {
                           Name = "ExecutionHandlerClientPriceConsumer" + stock.InstrumentId,
                           DurableName = "ExecutionHandlerClientPriceConsumer" + stock.InstrumentId,
                           DeliverPolicy = ConsumerConfigDeliverPolicy.All,
                           DeliverGroup = "ExecutionHandler",
                           AckPolicy = ConsumerConfigAckPolicy.Explicit,
                           FilterSubject = topic
                       };
        
                       var stream = "StreamClientPrices";
                       var consumerCD = await _natsClient.CreateJetStreamContext().CreateOrUpdateConsumerAsync(stream, clientPriceConfig, ctss.Token);
                        
                       var task = Task.Run(async () =>
                       {
                           try
                           {
                               await foreach (var natsMsg in consumerCD.ConsumeAsync<Stock>(cancellationToken: ctss.Token))
                               {
                                   var updatedStock = natsMsg.Data;
                                   if (updatedStock != null)
                                   {
                                       var localStock = (Stock) updatedStock.Clone();
                                       var matchingStock = _stockOptions.SingleOrDefault(s => s.InstrumentId == localStock.InstrumentId);
                                       if (matchingStock != null)
                                       {
                                           _prices.AddOrUpdate(
                                               matchingStock.InstrumentId,
                                               localStock,
                                               (key, existing) =>
                                               {
                                                   existing.Price = localStock.Price;
                                                   return existing;
                                               });
                                           
                                           await natsMsg.AckAsync(cancellationToken: ctss.Token);
                                       }   
                                   }
                               }
                           }
                           catch (OperationCanceledException) { Console.WriteLine($"Stopped subscription: {topic}"); }
                       }, ctss.Token);
                       _subscriptionTasks.Add(task);
                    }
                    await msg.AckAsync(cancellationToken: cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Hedge service handling cancelled");
            }
        }, cts.Token);
        _subscriptionTasks.Add(task);
    }
    private void SubscribeToBuyOrderApproved()
    {
        var topicOrderApproved = TopicGenerator.TopicForClientBuyOrderApproved();
        //_observable.Subscribe<Order>(topicOrderApproved, Id, HandleBuyOrder);
        
        if (_subscriptionTokens.TryGetValue(topicOrderApproved, out var existingCts))
        {
            existingCts.Cancel();
            _subscriptionTokens.Remove(topicOrderApproved);
        }

        var cts = new CancellationTokenSource();
        _subscriptionTokens[topicOrderApproved] = cts;
        
        var task = Task.Run(async () =>
        {
            try
            {
                var consumer = await _natsClient.CreateJetStreamContext().GetConsumerAsync("streamOrders", "buyOrderApprovedConsumer", cts.Token);
                await foreach (var msg in consumer.ConsumeAsync<Order>(cancellationToken: cts.Token))
                {
                    //await msg.AckProgressAsync(); TODO figure out time
                    HandleBuyOrder(msg.Data);
                    await msg.AckAsync(cancellationToken: cts.Token);
                }
            }
            catch (OperationCanceledException) {  }
        }, cts.Token);
        _subscriptionTasks.Add(task);
    }
    private void SubscribeToHedgeResponse()
    {
        var topicHedgeResponse = TopicGenerator.TopicForHedgingOrderResponse();
        //_observable.Subscribe<(TransactionData, string)>(topicHedgeResponse, Id, BookHedgedOrder);
        if (_subscriptionTokens.TryGetValue(topicHedgeResponse, out var existingCts))
        {
            existingCts.Cancel();
            _subscriptionTokens.Remove(topicHedgeResponse);
        }

        var cts = new CancellationTokenSource();
        _subscriptionTokens[topicHedgeResponse] = cts;
        
        var task = Task.Run(async () =>
        {
            try
            {
                var consumer = await _natsClient.CreateJetStreamContext().GetConsumerAsync("streamOrders", "hedgeOrderResponseConsumer", cts.Token);
                await foreach (var msg in consumer.ConsumeAsync<TransactionData>(cancellationToken: cts.Token))
                {
                    //await msg.AckProgressAsync(); TODO figure out time
                    BookHedgedOrder(msg.Data);
                    await msg.AckAsync(cancellationToken: cts.Token);
                }
            }
            catch (OperationCanceledException) {  }
        }, cts.Token);
        _subscriptionTasks.Add(task);
    }

    private async void HandleBuyOrder(Order order)
    {
        Console.WriteLine("HandleBuyOrder by executionHandler");
        if (!_prices.TryGetValue(order.Stock.InstrumentId, out var matchingStock))
        {
            Console.WriteLine("No matching stock found for instrument: " + order.Stock.InstrumentId +
                              " we have: [" + string.Join(", ", _prices.Keys) + "]");
            return;
        }
        Console.WriteLine($"Matching stock: {matchingStock.InstrumentId} price: {matchingStock.Price}");
        var transaction = new TransactionData
        {
            InstrumentId = order.Stock.InstrumentId,
            Size = order.Stock.Size,
            Price = order.Stock.Price,
            SpreadPrice = order.SpreadPrice,
        };

        if (order.Side == OrderSide.RightSided)
        {
            // Buy
            transaction.BuyerId = order.ClientId;
            transaction.SellerId = Guid.Empty;
        }
        else
        {
            // Sell
            transaction.SellerId = order.ClientId;
            transaction.BuyerId = Guid.Empty;
        }
        
        if (decimal.Compare(Math.Round(matchingStock.Price,2), Math.Round(order.Stock.Price,2)) == 0)
        {
            _logger.LogInformation("Letting {OrderClientId} buy order {StockInstrumentId} at price {StockPrice} quantity {StockSize}", order.ClientId, order.Stock.InstrumentId, order.Stock.Price, order.Stock.Size);

            order.Status = OrderStatus.Success;

            transaction.Succeeded = true;
            var topicBook = "";
            if (order.HedgeOrder)
            {
                topicBook = TopicGenerator.TopicForHedgingOrderRequest();
            }
            else
            {
                topicBook = TopicGenerator.TopicForBookingOrder();
            }
            //_observable.Publish(topicBook, transaction, isTransient: true);
            await _natsClient.PublishAsync(topicBook, transaction);

            var topicClient = TopicGenerator.TopicForClientOrderEnded(order.ClientId.ToString());
            //_observable.Publish(topicClient, order, isTransient: true);
            await _natsClient.PublishAsync(topicClient, order);
        }
        else
        {
            var topic = TopicGenerator.TopicForClientOrderEnded(order.ClientId.ToString());
            order.Status = OrderStatus.Canceled;
            order.ErrorMesssage = "Order was canceled due to price changed";
            //_observable.Publish(topic, order, isTransient: true);
            await _natsClient.PublishAsync(topic, order);
        }
    }

    public async void BookHedgedOrder(TransactionData response)
    {
        var topic = TopicGenerator.TopicForHedgingOrder();
        //_observable.Publish(topic, response, isTransient: true);
        await _natsClient.PublishAsync(topic, response);
    }

    public async void Stop()
    {
        var topicOrderApproved = TopicGenerator.TopicForClientBuyOrderApproved();
        _observable.Unsubscribe(topicOrderApproved, Id);

        var topicHedgeResponse = TopicGenerator.TopicForHedgingOrderResponse();
        _observable.Unsubscribe(topicHedgeResponse, Id);

        var topicInstruments = TopicGenerator.TopicForAllInstruments();
        _observable.Unsubscribe(topicInstruments, Id);

        foreach (var stock in _stockOptions)
        {
            var topic = TopicGenerator.TopicForClientInstrumentPrice(stock.InstrumentId);
            _observable.Unsubscribe(topic, Id);
        }
        
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
}