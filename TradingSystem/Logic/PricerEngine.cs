using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;
using TradingSystem.Data;
using TradingSystem.DTO;
using TradingSystem.Logic.LoggerExtensions;
using TradingSystem.Setup;

namespace TradingSystem.Logic;

public interface IPricerEngine
{
    public void Start();
    public void Stop();
}


public class PricerEngine : IPricerEngine
{
    private readonly ILogger<PricerEngine> _logger;
    private HashSet<Stock> _referencePrices;
    private readonly IOptions<InstrumentsOptions> _tradingOptions;
    private readonly IObservable _observable;
    private INatsJSContext _clientPricesStream;
    private const string Id = "pricerEngine";
    
    private readonly NatsClient _natsClient;
    private readonly Dictionary<string, CancellationTokenSource> _subscriptionTokens = new();
    private readonly List<Task> _subscriptionTasks = new();

    public PricerEngine(ILogger<PricerEngine> logger
        , IOptions<InstrumentsOptions> stocksOptions
        , IObservable observable
        , NatsClient natsClient)
    {
        _logger = logger;
        _referencePrices = new HashSet<Stock>();
        _tradingOptions = stocksOptions;
        _observable = observable;
        _natsClient = natsClient;
        _clientPricesStream = _natsClient.CreateJetStreamContext();
    }

    public void Start()
    {
        _logger.PricerEngineStartUp();
        
        //SetupJetStream();
        SubscribeToMarketPriceUpdates();

        _logger.PricerEngineStarted();
    }
    
    private async void SetupJetStream()
    {
        _clientPricesStream = _natsClient.CreateJetStreamContext();
        var streamConfig = new StreamConfig
        {
            Name = "StreamClientPrices",
            Subjects = ["clientPrices.*"],
            Description = "This is where client prices will be streamed",
            AllowDirect = true,
            MaxConsumers = -1,
            MaxMsgsPerSubject = 1
        };

        var orderConfig = new StreamConfig
        {
            Name = "streamOrders",
            Subjects = ["clientOrders.*"],
            Description = "This is where client orders will be streamed",
            AllowDirect = true,
        };
        
        var marketConfig = new StreamConfig
        {
            Name = "StreamMarketPrices",
            Subjects = ["marketPrices.*"],
            Description = "This is where market prices will be streamed",
            AllowDirect = true,
            MaxConsumers = -1,
            MaxMsgsPerSubject = 1
        };
        
        // TODO Stream for DBData
        var dbConfig = new StreamConfig
        {
            Name = "StreamDBData",
            Subjects = ["StreamDBData.*"],
            Description = "Here you will find most of our DB data that is used throughout the system, be careful who has access to this",
            AllowDirect = true,
            MaxConsumers = -1,
            MaxMsgsPerSubject = 1
        };
        
        // TODO Stream for AllData
        // streamMISC
        var miscConfig = new StreamConfig
        {
            Name = "StreamMisc",
            Subjects = ["StreamMisc.*"],
            Description = "This is a stream for random persistent data that is needed by multiple services",
            AllowDirect = true,
            MaxConsumers = -1,
            MaxMsgsPerSubject = 1
        };
        
        var loginConfig = new StreamConfig
        {
            Name = "streamLoginRequest",
            Subjects = ["loginRequest.*"],
            Description = "This is where login requests will be streamed",
            AllowDirect = true,
        };
        
        // Create stream
        await _clientPricesStream.CreateOrUpdateStreamAsync(streamConfig);
        await _clientPricesStream.CreateOrUpdateStreamAsync(orderConfig);
        await _clientPricesStream.CreateOrUpdateStreamAsync(marketConfig);
        await _clientPricesStream.CreateOrUpdateStreamAsync(miscConfig);
        await _clientPricesStream.CreateOrUpdateStreamAsync(dbConfig);
        await _clientPricesStream.CreateOrUpdateStreamAsync(loginConfig);
    }

    private async void SubscribeToMarketPriceUpdates()
    {
        var stocks = _tradingOptions.Value.Stocks;

        foreach (var stock in stocks)
        {
            var newStock = new Stock
            {
                InstrumentId = stock,
                Price = 0.0m
            };
            _referencePrices.Add(newStock);
            var stockTopic = TopicGenerator.TopicForMarketInstrumentPrice(newStock.InstrumentId);
            
            if (_subscriptionTokens.TryGetValue(stockTopic, out var existingCts))
            {
                existingCts.Cancel();
                _subscriptionTokens.Remove(stockTopic);
            }
            var cts = new CancellationTokenSource();
            _subscriptionTokens[stockTopic] = cts;
            
            var consumerConfig = new ConsumerConfig
            {
                Name = "marketPricesConsumer" + newStock.InstrumentId,
                DurableName = "marketPricesConsumer" + newStock.InstrumentId,
                DeliverGroup = "PricerEngine",
                DeliverPolicy = ConsumerConfigDeliverPolicy.All,
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
                FilterSubject = stockTopic,
            };
        
            var stream = "StreamMarketPrices";
        
            var consumer = await _natsClient.CreateJetStreamContext().CreateOrUpdateConsumerAsync(stream, consumerConfig, cts.Token);
            
            var task = Task.Run(async () =>
            {
                try
                {
                    await foreach (var msg in consumer.ConsumeAsync<Stock>(cancellationToken: cts.Token))
                    {
                        //await msg.AckProgressAsync(); TODO figure out time
                        UpdatePrice(msg.Data);
                        await msg.AckAsync(cancellationToken: cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Hedge service handling cancelled");
                }
            }, cts.Token);
            _subscriptionTasks.Add(task);
            
            //_observable.Subscribe<Stock>(stockTopic, Id, UpdatePrice);
        }
        PublishReferencePrices();
    }
    private async void PublishReferencePrices()
    {
        var topic = TopicGenerator.TopicForAllInstruments();
        //_observable.Publish(topic, _referencePrices);
        await _natsClient.PublishAsync(topic, _referencePrices);
    }

    public async void Stop()
    {
        foreach (var stock in _tradingOptions.Value.Stocks)
        {
            var stockTopic = TopicGenerator.TopicForMarketInstrumentPrice(stock);
            _observable.Unsubscribe(stockTopic, Id);
        }
        _referencePrices = new();
        
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

    private async void UpdatePrice(Stock stock)
    {
        _logger.PricerEngineReceivedNewPrice(stock.InstrumentId, stock.Price);
        var stockTopic = TopicGenerator.TopicForClientInstrumentPrice(stock.InstrumentId);
        //_observable.Publish(stockTopic, stock);
        await _clientPricesStream.PublishAsync(stockTopic, stock);
    }
}