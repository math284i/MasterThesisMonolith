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
        var topicInstruments = TopicGenerator.TopicForAllInstruments();
        _messageBus.Subscribe<HashSet<StockOptions>>(topicInstruments, Id, stocks =>
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
        
        var transaction = new TransactionData
        {
            InstrumentId = order.Stock.InstrumentId,
            Size = order.Stock.Quantity,
            Price = order.Stock.Price,
        };

        if (order.Side == OrderSide.RightSided)
        {
            // Buy
            transaction.BuyerId = order.ClientId;
            transaction.SellerId = Guid.Empty; // TODO
        }
        else
        {
            // Sell
            transaction.SellerId = order.ClientId;
            transaction.BuyerId = Guid.Empty;
        }
        
        if (matchingStock.Price == order.Stock.Price)
        {
            // TODO Here check if we should book it our self or we should hedge it to the market
            _logger.LogInformation($"Letting {order.ClientId} buy order {order.Stock.InstrumentId} at price {order.Stock.Price} quantity {order.Stock.Size}");
            
            // TODO if we send it to the market, wait for their acceptance before telling the client it was succeded.
            // TODO if sent to the market, create 2 books.
            order.Status = OrderStatus.Success;

            transaction.Succeeded = true;
            var topicBook = TopicGenerator.TopicForBookingOrder();
            _messageBus.Publish(topicBook, transaction, isTransient: true);
            
            var topicClient = TopicGenerator.TopicForClientOrderEnded(order.ClientId.ToString());
            _messageBus.Publish(topicClient, order, isTransient: true);
        }
    }

    public void Stop()
    {
        
    }
}