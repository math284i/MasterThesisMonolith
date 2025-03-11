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
    private readonly List<ClientData> _clients = new List<ClientData>();
    
    public RiskCalculator(IMessageBus messageBus)
    {
        _messageBus = messageBus;
    }
    
    public void Start()
    {   
        
        // TODO retrieve all clients currently in book, so we can start to calculate their risk already
        // TODO also retrieve their book to keep in cache, so that clientAPI can get it straight from here. 
        var topic = TopicGenerator.TopicForClientBuyOrder();
        _messageBus.Subscribe<Order>(topic, Id, CheckOrder);
    }

    private void CheckOrder(Order order)
    {
        var clientAmount = order.Stock.Price + 1.0f; // TODO tmp
        if (clientAmount > order.Stock.Price)
        {
            Console.WriteLine($"Risk Calculator checking client {order.ClientId} with stock {order.Stock}");
            var topic = TopicGenerator.TopicForClientBuyOrderApproved();
            _messageBus.Publish(topic, order, isTransient: true);
        }
        else
        {
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