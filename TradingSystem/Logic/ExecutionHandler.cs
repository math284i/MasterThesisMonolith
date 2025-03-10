using TradingSystem.Data;

namespace TradingSystem.Logic;

public interface IExecutionHandler
{
    public void Start();
    
    public void Stop();
}

public class ExecutionHandler : IExecutionHandler
{
    private readonly ILogger<ExecutionHandler> _logger;
    private const string Id = "executionHandler";
    private readonly IMessageBus _messageBus;
    private HashSet<StockOptions> _stockOptions = new HashSet<StockOptions>();
    private HashSet<StockOptions> _prices = new HashSet<StockOptions>();
    public ExecutionHandler(ILogger<ExecutionHandler> logger, IMessageBus messageBus)
    {
        _logger = logger;
        _messageBus = messageBus;
    }


    public void Start()
    {
        _messageBus.Subscribe<HashSet<StockOptions>>("allInstruments", Id, stocks =>
        {
            _stockOptions = stocks;

            foreach (var stock in _stockOptions)
            {
                var topic = TopicGenerator.TopicForClientInstrumentPrice(stock.InstrumentId);

                _messageBus.Subscribe<StockOptions>(topic, Id, updatedStock =>
                {
                    var matchingStock = _stockOptions.SingleOrDefault(s => s.InstrumentId == updatedStock.InstrumentId);
                    if (matchingStock != null)
                    {
                        if (_prices.Contains(matchingStock))
                        {
                            _prices.Remove(matchingStock);
                        }
                        _prices.Add(updatedStock);
                    }
                });
            }
        });
        var topic = TopicGenerator.TopicForClientBuyOrderApproved();
        _messageBus.Subscribe<Order>(topic, Id, HandleBuyOrder);
    }

    private void HandleBuyOrder(Order order)
    {
        var matchingStock = _stockOptions.SingleOrDefault(s => s.InstrumentId == order.Stock.InstrumentId);
        if (matchingStock == null) return;

        if (matchingStock.Price == order.Stock.Price)
        {
            // Here check if we should book it our self or we should hedge it to the market
            Console.WriteLine($"Letting {order.ClientId} buy order {order.Stock.InstrumentId} at price {order.Stock.Price} quantity {order.Stock.Quantity}");
        }
        
    }

    public void Stop()
    {
        
    }
}