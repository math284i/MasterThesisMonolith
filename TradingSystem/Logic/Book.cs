using TradingSystem.Data;

namespace TradingSystem.Logic;

public interface IBook
{
    public void Start();
    public void Stop();
}

public class Book : IBook
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<IBook> _logger;
    private readonly IDBHandler _dbHandler;
    private const string Id = "book";
    
    
    public Book(IMessageBus messageBus, ILogger<IBook> logger, IDBHandler dBHandler)
    {
        _messageBus = messageBus;
        _logger = logger;
        _dbHandler = dBHandler;
    }
    
    public void Start()
    {
        var clients = _dbHandler.GetAllClients();
        var topic = TopicGenerator.TopicForAllClients();
            
        _messageBus.Publish(topic, clients);

        var topicBookOrder = TopicGenerator.TopicForBookingOrder();
        _messageBus.Subscribe<TransactionData>(topicBookOrder, Id, BookOrder);
    }

    public void BookOrder(TransactionData transaction)
    {
        _logger.LogInformation("Book order called for {instrumentId}", transaction.InstrumentId);
        _dbHandler.AddTransaction(transaction);
    }

    public void Stop()
    {
        
    }
}