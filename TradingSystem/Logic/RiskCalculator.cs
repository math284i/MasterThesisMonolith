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
    private readonly IObservable _observable;
    private const string Id = "riskCalculator";
    private const string DanskeBankClientName = "Danske_Bank";
    private List<ClientData> _clients = new List<ClientData>();
    private ConcurrentDictionary<Guid, ClientData> _clientDatas = new ConcurrentDictionary<Guid, ClientData>();

    private readonly ConcurrentDictionary<string, TargetPosition> _targetPositions =
        new ConcurrentDictionary<string, TargetPosition>();
    private readonly ILogger<RiskCalculator> _logger;

    public RiskCalculator(IObservable observable, ILogger<RiskCalculator> logger)
    {
        _observable = observable;
        _logger = logger;
    }
    
    public void Start()
    {
        var topicForClients = TopicGenerator.TopicForAllClients();

        _observable.Subscribe<List<ClientData>>(topicForClients, Id, clients =>
        {
            foreach (var client in clients.Where(client => !_clients.Contains(client)))
            {
                _clients.Add(client);
                var topicForClientData = TopicGenerator.TopicForDBDataOfClient(client.ClientId.ToString());

                _observable.Subscribe<ClientData>(topicForClientData, Id, clientData =>
                {
                    _clientDatas.AddOrUpdate(client.ClientId, clientData, (key, oldValue) => clientData);
                });
            }
        });

        var topicAllTargets = TopicGenerator.TopicForAllTargetPositions();
        _observable.Subscribe<List<TargetPosition>>(topicAllTargets, Id, targets =>
        {
            foreach (var target in targets.Where(target => !_targetPositions.ContainsKey(target.InstrumentId)))
            {
                _targetPositions.AddOrUpdate(target.InstrumentId, target, (key, oldValue) => target);
                var topicForTarget = TopicGenerator.TopicForTargetPositionUpdate(target.InstrumentId);
                _observable.Subscribe<TargetPosition>(topicForTarget, Id, targetPosition =>
                {
                    Console.WriteLine($"Got target position {targetPosition}");
                    _targetPositions.AddOrUpdate(targetPosition.InstrumentId, targetPosition, (key, oldValue) => targetPosition);
                });
            }
        });
        
        // TODO retrieve all clients currently in book, so we can start to calculate their risk already
        // TODO also retrieve their book to keep in cache, so that clientAPI can get it straight from here. 
        var topic = TopicGenerator.TopicForClientBuyOrder();
        _observable.Subscribe<Order>(topic, Id, CheckOrder);
    }

    private void CheckOrder(Order order)
    {
        _clientDatas.TryGetValue(order.ClientId, out var clientData);
        if (clientData == null)
        {
            _logger.LogError("Risk calculator, Client {ClientId} not found", order.ClientId);
            return;
        }

        
        order.HedgeOrder = ShouldWeHedge(order);

        if (order.Side == OrderSide.RightSided)
        {
            // Buy
            float price = order.Stock.Price * order.Stock.Size;
            Console.WriteLine($"Buying for {price} balance is: {clientData.Balance}");
            if (clientData.Balance >= price)
            {
                _logger.LogInformation("RiskCalculator accepting order");
                var topic = TopicGenerator.TopicForClientBuyOrderApproved();
                _observable.Publish(topic, order, isTransient: true);
            }
            else
            {
                _logger.LogInformation("RiskCalculator Rejected order");
                var topic = TopicGenerator.TopicForClientOrderEnded(order.ClientId.ToString());
                order.Status = OrderStatus.Rejected;
                order.ErrorMesssage = "Insufficient Funds";
                _observable.Publish(topic, order, isTransient: true);
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
            _observable.Publish(topic, order, isTransient: true);
        }
    }

    private bool ShouldWeHedge(Order order)
    {
        var danskeBank = _clients.Find(c => c.Name == DanskeBankClientName);
        if (danskeBank == null) return true;
        var danskeData = _clientDatas[danskeBank.ClientId];
        var danskeStock = danskeData.Holdings.Find(h => h.InstrumentId == order.Stock.InstrumentId);
        if (danskeStock == null) return true;
        var targetPosition = _targetPositions[order.Stock.InstrumentId];
        var shouldWeHedge = targetPosition.Type switch
        {
            TargetType.FOK => ShouldWeHedgeFOK(order, targetPosition, danskeStock),
            TargetType.IOC => true,
            TargetType.GTC => true,
            TargetType.GFD => true,
            _ => throw new ArgumentOutOfRangeException()
        };
        return shouldWeHedge;
    }

    private bool ShouldWeHedgeFOK(Order order, TargetPosition targetPosition, HoldingData danskeStock)
    {
        if (order.Side == OrderSide.RightSided)
        {
            // TODO ask Nikodem about target type, and below code.
            // Buy
            if (targetPosition.Target >= danskeStock.Size) return true;
            return danskeStock.Size < order.Stock.Size;
        }
        else
        {
            // Sell
            return targetPosition.Target <= danskeStock.Size;
        }
    }

    public void Stop()
    {
        var topic = TopicGenerator.TopicForClientBuyOrder();
        _observable.Unsubscribe(topic, Id);
        var topicForClients = TopicGenerator.TopicForAllClients();
        _observable.Unsubscribe(topicForClients, Id);

        foreach (var client in _clients)
        {
            var topicForClientData = TopicGenerator.TopicForDBDataOfClient(client.ClientId.ToString());

            _observable.Unsubscribe(topicForClientData, Id);
        }
        _clients = new List<ClientData>();
        _clientDatas = new ConcurrentDictionary<Guid, ClientData>();
    }
}