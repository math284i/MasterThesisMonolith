using System.Collections.Concurrent;
using TradingSystem.Components.Pages;
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
    private ConcurrentDictionary<Guid, ClientData> _clientDatas = new ConcurrentDictionary<Guid, ClientData>();
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

        _messageBus.Subscribe<List<ClientData>>(topicForClients, Id, clients =>
        {
            foreach (var client in clients.Where(client => !_clients.Contains(client)))
            {
                _clients.Add(client);
                var topicForClientData = TopicGenerator.TopicForDBDataOfClient(client.ClientId.ToString());

                _messageBus.Subscribe<ClientData>(topicForClientData, Id, clientData =>
                {
                    _clientDatas.AddOrUpdate(client.ClientId, clientData, (key, oldValue) => clientData);
                });
            }
        });
        // TODO retrieve all clients currently in book, so we can start to calculate their risk already
        // TODO also retrieve their book to keep in cache, so that clientAPI can get it straight from here. 
        var topic = TopicGenerator.TopicForClientBuyOrder();
        _messageBus.Subscribe<Order>(topic, Id, CheckOrder);
    }

    private void CheckOrder(Order order)
    {
        _clientDatas.TryGetValue(order.ClientId, out var clientData);
        if (clientData == null)
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
            float price = order.Stock.Price * order.Stock.Size;
            Console.WriteLine($"Buying for {price} balance is: {clientData.Balance}");
            if (clientData.Balance >= price)
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
            var holding = _clientDatas[order.ClientId].Holdings.Find(h => h.InstrumentId == order.Stock.InstrumentId);
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
        var topic = TopicGenerator.TopicForClientBuyOrder();
        _messageBus.Unsubscribe(topic, Id);
        var topicForClients = TopicGenerator.TopicForAllClients();
        _messageBus.Unsubscribe(topicForClients, Id);

        foreach (var client in _clients)
        {
            var topicForClientData = TopicGenerator.TopicForDBDataOfClient(client.ClientId.ToString());

            _messageBus.Unsubscribe(topicForClientData, Id);
        }
        _clients = new List<ClientData>();
        _clientDatas = new ConcurrentDictionary<Guid, ClientData>();
    }
}