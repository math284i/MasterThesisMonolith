using NATS.Client.JetStream.Models;
using NATS.Net;
using TradingSystem.Data;
using TradingSystem.Logic.ExternalBrokers;

namespace TradingSystem.Logic;

public interface IMarketDataGateway
{
    public void Start();
    public void Stop();

    public void setSimSpeed(int newSpeed);
}


public class MarketDataGateway(IObservable observable, INordea nordea, IJPMorgan JPMorgan, INASDAQ NASDAQ, NatsClient natsClient) : IMarketDataGateway
{
    private HashSet<Stock> _stockOptions = new HashSet<Stock>();
    private HashSet<string> _instrumentIds = new HashSet<string>();
    private readonly CancellationTokenSource _cts = new();
    private Lock _simulationLock = new();
    private const string Id = "marketDataGateway";
    private int simSpeed = 25;
    
    private readonly Dictionary<string, CancellationTokenSource> _subscriptionTokens = new();
    private readonly List<Task> _subscriptionTasks = new();

    public void Start()
    {
        SetupConsumers();
    }

    private async void SetupConsumers()
    {
        var consumerConfig = new ConsumerConfig
        {
            Name = "MarketDataGatewayAllInstrumentsConsumer",
            DurableName = "MarketDataGatewayAllInstrumentsConsumer",
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            DeliverGroup = "MarketDataGateway",
            FilterSubject = TopicGenerator.TopicForAllInstruments()
        };
        
        var stream = "StreamMisc";
        
        await natsClient.CreateJetStreamContext().CreateOrUpdateConsumerAsync(stream, consumerConfig);
        
        SubscribeToInstruments();


        Task.Run(() => RunSimulation(_cts.Token));
    }

    private void SubscribeToInstruments()
    {
        var topic = TopicGenerator.TopicForAllInstruments();
        // observable.Subscribe<HashSet<Stock>>(topic, Id, stocks =>
        // {
        //     _stockOptions = stocks;
        //     PublishInitialMarketPrice();
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
                var consumer = await natsClient.CreateJetStreamContext().GetConsumerAsync("StreamMisc", "MarketDataGatewayAllInstrumentsConsumer", cts.Token);
                await foreach (var msg in consumer.ConsumeAsync<HashSet<Stock>>(cancellationToken: cts.Token))
                {
                    //await msg.AckProgressAsync(); TODO figure out time
                    _stockOptions = msg.Data;
                    PublishInitialMarketPrice();
                    await msg.AckAsync(cancellationToken: cts.Token);
                }
            }
            catch (OperationCanceledException) {  }
        }, cts.Token);
        _subscriptionTasks.Add(task);
    }
    private async void PublishInitialMarketPrice()
    {
        Dictionary<string, decimal> nordeaPrices = nordea.getPrices();
        Dictionary<string, decimal> JPMorganPrices = JPMorgan.getPrices();
        Dictionary<string, decimal> NASDAQPrices = NASDAQ.getPrices();
        
        foreach (Stock stock in _stockOptions)
        {
            _instrumentIds.Add(stock.InstrumentId);
            //Find minimal price of instrument on the market.
            var minMarketPrice = decimal.MaxValue;
            if (nordeaPrices.ContainsKey(stock.InstrumentId) && nordeaPrices[stock.InstrumentId] < minMarketPrice)
            {
                minMarketPrice = nordeaPrices[stock.InstrumentId];
            }
            else if (JPMorganPrices.ContainsKey(stock.InstrumentId) && JPMorganPrices[stock.InstrumentId] < minMarketPrice)
            {
                minMarketPrice = JPMorganPrices[stock.InstrumentId];
            }
            else if (NASDAQPrices.ContainsKey(stock.InstrumentId) && NASDAQPrices[stock.InstrumentId] < minMarketPrice)
            {
                minMarketPrice = NASDAQPrices[stock.InstrumentId];
            }
            else
            {
                //Basecase - No price found, so none published
                minMarketPrice = 0.0m;
            }

            stock.Price = minMarketPrice;
            var stockTopic = TopicGenerator.TopicForMarketInstrumentPrice(stock.InstrumentId);
            //observable.Publish(stockTopic, stock);
            await natsClient.PublishAsync(stockTopic, stock);
        }
    }

    private async Task RunSimulation(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var result = await marketCheck(nordea, JPMorgan, NASDAQ);
            if(_instrumentIds.Contains(result.InstrumentId))
            {
                var stockTopic = TopicGenerator.TopicForMarketInstrumentPrice(result.InstrumentId);
                //observable.Publish(stockTopic, result);
                await natsClient.PublishAsync(stockTopic, result, cancellationToken: token);
            }
        }
    }

    private async Task<Stock> marketCheck(INordea Nordea, IJPMorgan JPMorgan, INASDAQ NASDAQ)
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        bool firstInLocker = true;

        //Each API has a 1/simSpeed chance of simulating a price change every half second.
        Task<Stock> funNordea() => Task.Run( () =>
        {
            return Nordea.simulatePriceChange(simSpeed, ref _simulationLock, ref token, ref firstInLocker);
        });

        Task<Stock> funJPMorgan() => Task.Run(() =>
        {
            return JPMorgan.simulatePriceChange(simSpeed, ref _simulationLock, ref token, ref firstInLocker);
        });

        Task<Stock> funNASDAQ() => Task.Run(() =>
        {
            return NASDAQ.simulatePriceChange(simSpeed, ref _simulationLock, ref token, ref firstInLocker);
        });
        
        Task<Stock>[] tasks = { funNordea(), funJPMorgan(), funNASDAQ() };
        
        Task<Stock> firstCompleted = await Task.WhenAny(tasks);

        cts.Cancel();

        return await firstCompleted;
    }

    public async void Stop()
    {
        _cts.Cancel();
        var topic = TopicGenerator.TopicForAllInstruments();
        observable.Unsubscribe(topic, Id);

        _stockOptions = new();
        _instrumentIds = new();
        
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

    public void setSimSpeed(int newSpeed)
    {
        if(newSpeed > 0)
        {
            simSpeed = newSpeed;
        }
    }

}