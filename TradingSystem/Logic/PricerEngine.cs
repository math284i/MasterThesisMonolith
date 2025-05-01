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
    private readonly NatsClient _natsClient;
    private INatsJSContext _clientPricesStream;
    private const string Id = "pricerEngine";

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
    }

    public void Start()
    {
        _logger.PricerEngineStartUp();
        
        SetupJetStream();
        SubscribeToMarketPriceUpdates();
        PublishReferencePrices();

        _logger.PricerEngineStarted();
    }

    private async void SetupJetStream()
    {
        _clientPricesStream = _natsClient.CreateJetStreamContext();
        var streamConfig = new StreamConfig
        {
            Name = "StreamPrices",
            Subjects = ["clientPrices.*"],
            Description = "This is where client prices will be streamed",
            AllowDirect = true,
            MaxConsumers = -1,
            MaxMsgsPerSubject = 1
        };

        // Create stream
        var stream = await _clientPricesStream.CreateOrUpdateStreamAsync(streamConfig);
        var cts = new CancellationTokenSource();
        var ctx = _natsClient.CreateJetStreamContext();
        var consumerConfig = new ConsumerConfig
        {
            Name = "clientPrices_GME",
            DurableName = "clientPrices_GME", // unique per consumer
            DeliverPolicy = ConsumerConfigDeliverPolicy.Last,
            FilterSubject = "clientPrices.GME",
        };
        //stream.CreateOrUpdateConsumerAsync();
        await _clientPricesStream.CreateOrUpdateConsumerAsync("StreamPrices", consumerConfig, cts.Token);
    }

    private void SubscribeToMarketPriceUpdates()
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
            _observable.Subscribe<Stock>(stockTopic, Id, UpdatePrice);
        }
    }
    private void PublishReferencePrices()
    {
        var topic = TopicGenerator.TopicForAllInstruments();
        _observable.Publish(topic, _referencePrices);
    }

    public void Stop()
    {
        foreach (var stock in _tradingOptions.Value.Stocks)
        {
            var stockTopic = TopicGenerator.TopicForMarketInstrumentPrice(stock);
            _observable.Unsubscribe(stockTopic, Id);
        }
        _referencePrices = new();
    }

    private async void UpdatePrice(Stock stock)
    {
        _logger.PricerEngineReceivedNewPrice(stock.InstrumentId, stock.Price);
        var stockTopic = TopicGenerator.TopicForClientInstrumentPrice(stock.InstrumentId);
        _observable.Publish(stockTopic, stock);
        await _clientPricesStream.PublishAsync(stockTopic, stock);
    }
}