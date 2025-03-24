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
    private readonly IObservable _observable;
    private HashSet<Stocks> _stockOptions = new HashSet<Stocks>();
    private HashSet<Stocks> _prices = new HashSet<Stocks>();
    public ExecutionHandler(ILogger<ExecutionHandler> logger, IObservable observable)
    {
        _logger = logger;
        _observable = observable;
    }


    public void Start()
    {
        var topicInstruments = TopicGenerator.TopicForAllInstruments();
        _observable.Subscribe<HashSet<Stocks>>(topicInstruments, Id, stocks =>
        {
            _stockOptions = stocks;

            foreach (var stock in _stockOptions)
            {
                var topic = TopicGenerator.TopicForClientInstrumentPrice(stock.InstrumentId);

                _observable.Subscribe<Stocks>(topic, Id, updatedStock =>
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
        var topicOrderApproved = TopicGenerator.TopicForClientBuyOrderApproved();
        _observable.Subscribe<Order>(topicOrderApproved, Id, HandleBuyOrder);

        var topicHedgeResponse = TopicGenerator.TopicForHedgingOrderResponse();
        _observable.Subscribe<(TransactionData, string)>(topicHedgeResponse, Id, BookHedgedOrder);
    }

    private void HandleBuyOrder(Order order)
    {
        Console.WriteLine($"Execution handler got order {order.Stock.InstrumentId}");
        var matchingStock = _stockOptions.SingleOrDefault(s => s.InstrumentId == order.Stock.InstrumentId);
        if (matchingStock == null) return;
        
        var transaction = new TransactionData
        {
            InstrumentId = order.Stock.InstrumentId,
            Size = order.Stock.Size,
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
            var topicBook = "";
            if (order.HedgeOrder)
            {
                topicBook = TopicGenerator.TopicForHedgingOrderRequest();
            }
            else
            {
                topicBook = TopicGenerator.TopicForBookingOrder();
            }
            _observable.Publish(topicBook, transaction, isTransient: true);

            var topicClient = TopicGenerator.TopicForClientOrderEnded(order.ClientId.ToString());
            _observable.Publish(topicClient, order, isTransient: true);
        }
    }

    public void BookHedgedOrder((TransactionData trans, string brokerName) response)
    {
        var topic = TopicGenerator.TopicForHedgingOrder();
        _observable.Publish(topic, response, isTransient: true);
    }

    public void Stop()
    {
        var topicOrderApproved = TopicGenerator.TopicForClientBuyOrderApproved();
        _observable.Unsubscribe(topicOrderApproved, Id);

        var topicHedgeResponse = TopicGenerator.TopicForHedgingOrderResponse();
        _observable.Unsubscribe(topicHedgeResponse, Id);

        var topicInstruments = TopicGenerator.TopicForAllInstruments();
        _observable.Unsubscribe(topicInstruments, Id);

        foreach (var stock in _stockOptions)
        {
            var topic = TopicGenerator.TopicForClientInstrumentPrice(stock.InstrumentId);
            _observable.Unsubscribe(topic, Id);
        }
    }
}