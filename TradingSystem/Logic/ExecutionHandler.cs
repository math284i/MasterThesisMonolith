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
    private HashSet<Stock> _stockOptions = new HashSet<Stock>();
    private HashSet<Stock> _prices = new HashSet<Stock>();
    public ExecutionHandler(ILogger<ExecutionHandler> logger, IObservable observable)
    {
        _logger = logger;
        _observable = observable;
    }


    public void Start()
    {
        SetupClientStockPrices();
        SubscribeToBuyOrderApproved();
        SubscribeToHedgeResponse();
    }

    private void SetupClientStockPrices()
    {
        var topicInstruments = TopicGenerator.TopicForAllInstruments();
        _observable.Subscribe<HashSet<Stock>>(topicInstruments, Id, stocks =>
        {
            _stockOptions = new HashSet<Stock>();
            foreach (var localStock in stocks.Select(stock => (Stock) stock.Clone()))
            {
                _stockOptions.Add(localStock);
            }

            foreach (var topic in _stockOptions.Select(stock => 
                         TopicGenerator.TopicForClientInstrumentPrice(stock.InstrumentId)))
            {
                _observable.Subscribe<Stock>(topic, Id, updatedStock =>
                {
                    var localStock = (Stock) updatedStock.Clone();
                    var matchingStock = _stockOptions.SingleOrDefault(s => s.InstrumentId == localStock.InstrumentId);
                    if (matchingStock != null)
                    {
                        foreach (var price in _prices.Where(price => price.InstrumentId == matchingStock.InstrumentId))
                        {
                            _prices.Remove(price);
                        }

                        _prices.Add(localStock);
                    }
                });
            }
        });
    }
    private void SubscribeToBuyOrderApproved()
    {
        var topicOrderApproved = TopicGenerator.TopicForClientOrderApproved();
        _observable.Subscribe<Order>(topicOrderApproved, Id, HandleOrder);
    }
    private void SubscribeToHedgeResponse()
    {
        var topicHedgeResponse = TopicGenerator.TopicForHedgingOrderResponse();
        _observable.Subscribe<(TransactionData, string)>(topicHedgeResponse, Id, BookHedgedOrder);
    }

    private void HandleOrder(Order order)
    {
        var matchingStock = _prices.SingleOrDefault(s => s.InstrumentId == order.Stock.InstrumentId);
        if (matchingStock == null) return;
        
        var transaction = new TransactionData
        {
            InstrumentId = order.Stock.InstrumentId,
            Size = order.Stock.Size,
            Price = order.Stock.Price,
            SpreadPrice = order.SpreadPrice,
            DateMaturity = order.Stock.DateMaturity
        };

        if (order.Side == OrderSide.RightSided)
        {
            // Buy
            transaction.BuyerId = order.ClientId;
            transaction.SellerId = Guid.Empty;
        }
        else
        {
            // Sell
            transaction.SellerId = order.ClientId;
            transaction.BuyerId = Guid.Empty;
        }
            
        Console.WriteLine($"system Price {matchingStock.Price} for {order.Stock.Price}");
        if (decimal.Compare(Math.Round(matchingStock.Price,2), Math.Round(order.Stock.Price,2)) == 0)
        {
            _logger.LogInformation($"Letting {order.ClientId} buy order {order.Stock.InstrumentId} at price {order.Stock.Price} quantity {order.Stock.Size}");

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
        else
        {
            var topic = TopicGenerator.TopicForClientOrderEnded(order.ClientId.ToString());
            order.Status = OrderStatus.Canceled;
            order.ErrorMesssage = "Order was canceled due to price changed";
            _observable.Publish(topic, order, isTransient: true);
        }
    }

    public void BookHedgedOrder((TransactionData trans, string brokerName) response)
    {
        var topic = TopicGenerator.TopicForHedgingOrder();
        _observable.Publish(topic, response, isTransient: true);
    }

    public void Stop()
    {
        var topicOrderApproved = TopicGenerator.TopicForClientOrderApproved();
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