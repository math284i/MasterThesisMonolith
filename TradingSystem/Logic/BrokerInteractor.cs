using TradingSystem.Data;

namespace TradingSystem.Logic;


public class BrokerInteractor
{
    public const string REQUEST_MARKET_PRICE = "brokerInteractor-requestMarketPrice";

    public BrokerInteractor(IMessageBus messageBus)
    {
        messageBus.Subscribe<StockOptions>(REQUEST_MARKET_PRICE, stock =>
        {
            Console.WriteLine("Got stock: " + stock.InstrumentId);
            //Check stock price with brokers
            //Calculate a best market price
            Random rand = new Random();
            float marketPrice = 1.0f * rand.Next(1, 11); //Number return is 1.0f to and including 10.f
            messageBus.Publish<float>("pricerEngine-responseMarketPrice", marketPrice);
            Console.WriteLine("Published price for stock " + stock.InstrumentId + " on the bus.");
        });
    }
}