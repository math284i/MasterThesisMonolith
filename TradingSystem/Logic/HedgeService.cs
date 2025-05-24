using NATS.Client.JetStream.Models;
using NATS.Net;
using TradingSystem.Data;
using TradingSystem.Logic.ExternalBrokers;

namespace TradingSystem.Logic;

public interface IHedgeService
{
    public void Start();
    public void Stop();
}


public class HedgeService(IObservable observable, INordea nordea, IJPMorgan JPMorgan, INASDAQ NASDAQ, NatsClient natsClient) : IHedgeService
{
    private Dictionary<string, List<string>> brokerInventory = new();
    private const string Id = "hedgeService";
    
    private readonly Dictionary<string, CancellationTokenSource> _subscriptionTokens = new();
    private readonly List<Task> _subscriptionTasks = new();


    public void Start()
    {
        CreateConsumers();
    }

    private async void CreateConsumers()
    {
        var consumerConfig = new ConsumerConfig
        {
            Name = "hedgeOrderRequestConsumer",
            DurableName = "hedgeOrderRequestConsumer",
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            DeliverGroup = "HedgeService",
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            FilterSubject = TopicGenerator.TopicForHedgingOrderRequest()
        };
        
        var stream = "streamOrders";
        
        await natsClient.CreateJetStreamContext().CreateOrUpdateConsumerAsync(stream, consumerConfig);
        
        GetBrokerInventory();
        SubscribeToHedgeRequests();
    }
    private void SubscribeToHedgeRequests()
    {
        var topic = TopicGenerator.TopicForHedgingOrderRequest();
        //observable.Subscribe<TransactionData>(topic, Id, HandleHedgeRequest);
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
                var consumer = await natsClient.CreateJetStreamContext()
                    .GetConsumerAsync("streamOrders", "hedgeOrderRequestConsumer", cts.Token);
                await foreach (var msg in consumer.ConsumeAsync<TransactionData>(cancellationToken: cts.Token))
                {
                    //await msg.AckProgressAsync(); TODO figure out time
                    HandleHedgeRequest(msg.Data);
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
    private void GetBrokerInventory()
    {
        brokerInventory.Add("Nordea", nordea.getStocks());
        brokerInventory.Add("JPMorgan", JPMorgan.getStocks());
        brokerInventory.Add("NASDAQ", NASDAQ.getStocks());
    }

    public async void HandleHedgeRequest(TransactionData trans)
    {
        var brokerName = brokerInventory.FirstOrDefault(x => x.Value.Contains(trans.InstrumentId)).Key;

        // TODO Add ability for broker to reject hedge
        //For now we assume that all external brokers always want to buy/sell
        if(false)
        {
            trans.Succeeded = false;
        }
        trans.BrokerName = brokerName;
        var topic = TopicGenerator.TopicForHedgingOrderResponse();
        //observable.Publish(topic, trans, isTransient: true);
        await natsClient.PublishAsync(topic, trans);
    }

    public async void Stop()
    {
        brokerInventory = new();
        var topic = TopicGenerator.TopicForHedgingOrderRequest();
        observable.Unsubscribe(topic, Id);
        
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