using TradingSystem.Data;

namespace TradingSystem.Logic;

public interface IRiskCalculator
{
    public void Start();
    public void Stop();
}

public class RiskCalculator : IRiskCalculator
{
    private readonly IMessageBus _messageBus;
    private const string Id = "riskCalculator";
    private List<ClientData> _clients = new List<ClientData>();
    private readonly ILogger<RiskCalculator> _logger;
    
    public RiskCalculator(IMessageBus messageBus, ILogger<RiskCalculator> logger)
    {
        _messageBus = messageBus;
        _logger = logger;
    }
    
    public void Start()
    {
        var topicForClients = TopicGenerator.TopicForAllClients();
        _messageBus.Subscribe<List<ClientData>>(topicForClients, Id, clients =>
        {
            _clients = clients; // TODO make this threadsafe
        });
        // TODO retrieve all clients currently in book, so we can start to calculate their risk already
        // TODO also retrieve their book to keep in cache, so that clientAPI can get it straight from here. 
        var topic = TopicGenerator.TopicForClientBuyOrder();
        _messageBus.Subscribe<Order>(topic, Id, CheckOrder);
    }

    private void CheckOrder(Order order)
    {
        // TODO distinguish between buy and sell, using orderSide, Right = buy, left = sell
        var clientAmount = _clients.Find(c => c.clientId == order.ClientId);
        if (clientAmount == null)
        {
            _logger.LogError("Risk calculator, Client {ClientId} not found", order.ClientId);
            return;
        }
        if (clientAmount.balance >= order.Stock.Price * order.Stock.Quantity)
        {
            _logger.LogInformation("RiskCalculator accepting order");
            var topic = TopicGenerator.TopicForClientBuyOrderApproved();
            _messageBus.Publish(topic, order, isTransient: true);
        }
        else
        {
            _logger.LogInformation("RiskCalculator Rejected order");
            var topic = TopicGenerator.TopicForClientOrderEnded(order.ClientId.ToString());
            order.Status = OrderStatus.Rejected;
            order.ErrorMesssage = "Insufficient Funds";
            _messageBus.Publish(topic, order, isTransient: true);
        }
    }

    public void Stop()
    {
        
    }
}