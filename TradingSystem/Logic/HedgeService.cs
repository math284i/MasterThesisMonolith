﻿using TradingSystem.Data;
using TradingSystem.Logic.ExternalBrokers;

namespace TradingSystem.Logic;

public interface IHedgeService
{
    public void Start();
    public void Stop();
    public void HandleHedgeRequest(TransactionData trans);
}


public class HedgeService(IObservable observable, INordea nordea, IJPMorgan JPMorgan, INASDAQ NASDAQ) : IHedgeService
{
    private Dictionary<string, List<string>> brokerInventory = new();
    private const string Id = "hedgeService";

    public void Start()
    {
        GetBrokerInventory();
        SubscribeToHedgeRequests();
    }

    private void SubscribeToHedgeRequests()
    {
        var topic = TopicGenerator.TopicForHedgingOrderRequest();
        observable.Subscribe<TransactionData>(topic, Id, HandleHedgeRequest);
    }
    private void GetBrokerInventory()
    {
        brokerInventory.Add("Nordea", nordea.getStocks());
        brokerInventory.Add("JPMorgan", JPMorgan.getStocks());
        brokerInventory.Add("NASDAQ", NASDAQ.getStocks());
    }

    public void HandleHedgeRequest(TransactionData trans)
    {
        var brokerName = brokerInventory.FirstOrDefault(x => x.Value.Contains(trans.InstrumentId)).Key;

        // TODO Add ability for broker to reject hedge
        //For now we assume that all external brokers always want to buy/sell
        if(false)
        {
            trans.Succeeded = false;
        }

        var topic = TopicGenerator.TopicForHedgingOrderResponse();
        observable.Publish(topic, (trans, brokerName), isTransient: true);
    }

    public void Stop()
    {
        brokerInventory = new();
        var topic = TopicGenerator.TopicForHedgingOrderRequest();
        observable.Unsubscribe(topic, Id);
    }
}