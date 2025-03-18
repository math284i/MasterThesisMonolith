using System.Collections.Concurrent;
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
    private ConcurrentDictionary<Guid, List<HoldingData>> _clientHoldings = new ConcurrentDictionary<Guid, List<HoldingData>>();
    private readonly ILogger<RiskCalculator> _logger;
    private Random rand = new Random();

    public RiskCalculator(IMessageBus messageBus, ILogger<RiskCalculator> logger)
    {
        _messageBus = messageBus;
        _logger = logger;
    }
    
    public void Start()
    {
        var topicForClients = TopicGenerator.TopicForAllClients();
        var tmpBoolAlreadySubscribed = false;
        _messageBus.Subscribe<List<ClientData>>(topicForClients, Id, clients =>
        {
            
            _clients = clients; // TODO make this threadsafe
            if (!tmpBoolAlreadySubscribed)
            {
                foreach (var client in _clients)
                {
                    _logger.LogInformation($"Subscribed to {client}");
                    var topicForHolding = TopicGenerator.TopicForHoldingOfClient(client.ClientId.ToString());
                    _messageBus.Subscribe<List<HoldingData>>(topicForHolding, Id, holding =>
                    {
                        if (!_clientHoldings.TryAdd(client.ClientId, holding))
                        {
                            _clientHoldings[client.ClientId] = holding;
                        }
                    });
                }
            }
            tmpBoolAlreadySubscribed = true;
            
        });
        // TODO retrieve all clients currently in book, so we can start to calculate their risk already
        // TODO also retrieve their book to keep in cache, so that clientAPI can get it straight from here. 
        var topic = TopicGenerator.TopicForClientBuyOrder();
        _messageBus.Subscribe<Order>(topic, Id, CheckOrder);
    }

    private void CheckOrder(Order order)
    {
        // TODO distinguish between buy and sell, using orderSide, Right = buy, left = sell
        var clientAmount = _clients.Find(c => c.ClientId == order.ClientId);
        if (clientAmount == null)
        {
            _logger.LogError("Risk calculator, Client {ClientId} not found", order.ClientId);
            return;
        }

        //TODO: Add logic to determine whether order should be hedged. Currently just randomly selects whether to hedge order
        order.HedgeOrder = (rand.Next(0, 2) > 0);
        //order.HedgeOrder = true;

        if (order.Side == OrderSide.RightSided)
        {
            // Buy
            if (clientAmount.Balance >= order.Stock.Price * order.Stock.Size)
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
        else
        {
            // Sell
            var holding = _clientHoldings[order.ClientId].Find(h => h.InstrumentId == order.Stock.InstrumentId);
            Console.WriteLine($"Riskcalculator holding: {holding.ClientId} {holding.InstrumentId}, {holding.Size}");
            var topic = "";
            if (holding != null)
            {
                if (holding.Size >= order.Stock.Size)
                {
                    topic = TopicGenerator.TopicForClientBuyOrderApproved();
                }
                else
                {
                    topic = TopicGenerator.TopicForClientOrderEnded(order.ClientId.ToString());
                    order.Status = OrderStatus.Rejected;
                    order.ErrorMesssage = $"Client doesn't contain enough of the stock {order.Stock.InstrumentId}";
                }
            }
            else
            {
                topic = TopicGenerator.TopicForClientOrderEnded(order.ClientId.ToString());
                order.Status = OrderStatus.Rejected;
                order.ErrorMesssage = $"Client doesn't own any stock of {order.Stock.InstrumentId}";
            }
            _messageBus.Publish(topic, order, isTransient: true);
        }
    }

    public void Stop()
    {
        
    }
}