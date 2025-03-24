using System.Transactions;
using TradingSystem.Data;

namespace TradingSystem.Logic;

public interface IBook
{
    public void Start();
    public void Stop();
}

public class Book : IBook
{
    private readonly IObservable _observable;
    private readonly ILogger<IBook> _logger;
    private readonly IDBHandler _dbHandler;
    private const string Id = "book";
    private const string DanskeBankClientName = "Danske_Bank";


    public Book(IObservable observable, ILogger<IBook> logger, IDBHandler dBHandler)
    {
        _observable = observable;
        _logger = logger;
        _dbHandler = dBHandler;
    }
    
    public void Start()
    {
        var clients = _dbHandler.GetAllClients();
        var topic = TopicGenerator.TopicForAllClients();
            
        _observable.Publish(topic, clients);

        var topicBookOrder = TopicGenerator.TopicForBookingOrder();
        _observable.Subscribe<TransactionData>(topicBookOrder, Id, BookOrder);

        var topicHedgeOrder = TopicGenerator.TopicForHedgingOrder();
        _observable.Subscribe<(TransactionData, string)>(topicHedgeOrder, Id, HedgeOrder);
    }

    public void BookOrder(TransactionData transaction)
    {
        var danskeBankId = _dbHandler.GetClientGuid(DanskeBankClientName);
        if (transaction.BuyerId == Guid.Empty)
        {
            //Customer is selling
            transaction.BuyerId = danskeBankId;
        }else
        {
            //Customer is buying
            transaction.SellerId = danskeBankId;
        }
        _logger.LogInformation("Book order called for {instrumentId}", transaction.InstrumentId);
        _dbHandler.AddTransaction(transaction);
    }

    public void HedgeOrder((TransactionData trans, string brokerName) response)
    {
        _logger.LogInformation("Hedge order called for {instrumentId}", response.trans.InstrumentId);
        /*
        var externalBrokerIds = _dbHandler.GetAllClients().FindAll(x => x.Tier == "external").Select(b => b.ClientId).ToList();
        var brokerId = externalBrokerIds.Find(x => _dbHandler.GetClientHoldings(x).Exists(y => y.InstrumentId == transaction.InstrumentId));
        */
        var danskeBankId = _dbHandler.GetClientGuid(DanskeBankClientName);
        var brokerId = _dbHandler.GetClientGuid(response.brokerName);

        var trans1 = new TransactionData
        {
            TransactionId = response.trans.TransactionId,
            BuyerId = response.trans.BuyerId,
            SellerId = response.trans.SellerId,
            InstrumentId = response.trans.InstrumentId,
            Size = response.trans.Size,
            Price = response.trans.Price,
            Time = response.trans.Time,
            Succeeded = response.trans.Succeeded
};
        var trans2 = new TransactionData
        {
            TransactionId = response.trans.TransactionId,
            BuyerId = response.trans.BuyerId,
            SellerId = response.trans.SellerId,
            InstrumentId = response.trans.InstrumentId,
            Size = response.trans.Size,
            Price = response.trans.Price,
            Time = response.trans.Time,
            Succeeded = response.trans.Succeeded
        };
        if (response.trans.BuyerId == Guid.Empty)
        {
            //Client is selling stock
            trans1.BuyerId = danskeBankId;
            trans2.SellerId = danskeBankId;
            trans2.BuyerId = brokerId;
            
        }else
        {
            //Client is buying stock
            trans1.SellerId = brokerId;
            trans1.BuyerId = danskeBankId;
            trans2.SellerId = danskeBankId;
        }
        _dbHandler.AddTransaction(trans1);
        _dbHandler.AddTransaction(trans2);
    }

    public void Stop()
    {
        var topicBookOrder = TopicGenerator.TopicForBookingOrder();
        _observable.Unsubscribe(topicBookOrder, Id);

        var topicHedgeOrder = TopicGenerator.TopicForHedgingOrder();
        _observable.Unsubscribe(topicHedgeOrder, Id);
    }
}