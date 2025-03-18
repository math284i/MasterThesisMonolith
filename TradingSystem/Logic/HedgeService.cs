using TradingSystem.Data;
using TradingSystem.Logic.ExternalBrokers;

namespace TradingSystem.Logic;

public interface IHedgeService
{
    public void Start();
    public void Stop();
}


public class HedgeService(IMessageBus messageBus, INordea nordea, IJPMorgan JPMorgan, INASDAQ NASDAQ) : IHedgeService
{
    private Dictionary<string, List<string>> brokerInventory = new();
    private const string Id = "hedgeService";

    public void Start()
    {
        brokerInventory.Add("Nordea", nordea.getStocks());
        brokerInventory.Add("JPMorgan", JPMorgan.getStocks());
        brokerInventory.Add("NASDAQ", NASDAQ.getStocks());

        var topic = TopicGenerator.TopicForHedgingOrderRequest();
        messageBus.Subscribe<TransactionData>(topic, Id, HandleHedgeRequest);
    }

    public void HandleHedgeRequest(TransactionData trans)
    {
        var brokerName = brokerInventory.FirstOrDefault(x => x.Value.Contains(trans.InstrumentId)).Key;

        //Add ability for broker to reject hedge
        //For now we assume that all external brokers always want to buy/sell
        if(false)
        {
            trans.Succeeded = false;
        }

        var topic = TopicGenerator.TopicForHedgingOrderResponse();
        messageBus.Publish(topic, (trans, brokerName), isTransient: true);
    }

    public void Stop()
    {
        brokerInventory = new();
        var topic = TopicGenerator.TopicForHedgingOrderRequest();
        messageBus.Unsubscribe(topic, Id);
    }
}