using TradingSystem.Data;
using TradingSystem.Logic.ExternalBrokers;

namespace TradingSystem.Logic;

public interface IMarketDataGateway
{
    public void Start();
    public void Stop();
}


public class MarketDataGateway(IMessageBus messageBus, INordea nordea, IJPMorgan JPMorgan, INASDAQ NASDAQ) : IMarketDataGateway
{
    private HashSet<StockOptions> _stockOptions = new HashSet<StockOptions>();
    private HashSet<string> _instrumentIds = new HashSet<string>();
    private readonly CancellationTokenSource _cts = new();
    private Lock _simulationLock = new();
    private const string Id = "marketDataGateway";
    /*
    public MarketDataGateway
    {
        while (true)
            marketCheck(nordea, JPMorgan, NASDAQ);
            //Send a message on the message queue with the info returned by marketCheck

        
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
    */

    public void Start()
    {
        messageBus.Subscribe<HashSet<StockOptions>>("allInstruments", Id, stocks =>
        {
            _stockOptions = stocks;
        });
        
        //Need a set of only instrumentIds, as price changes mean that looking up in the stockoptions set will not function
        foreach (StockOptions stock in _stockOptions)
        {
            _instrumentIds.Add(stock.InstrumentId);
        }
        Task.Run(() => RunLoop(_cts.Token)); // Run in a background task
    }

    private async Task RunLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            StockOptions result = await marketCheck(nordea, JPMorgan, NASDAQ);
            if(_instrumentIds.Contains(result.InstrumentId))
            {
                var stockTopic = TopicGenerator.TopicForMarketInstrumentPrice(result.InstrumentId);
                messageBus.Publish(stockTopic, result);
            }
        }
    }

    private async Task<StockOptions> marketCheck(INordea Nordea, IJPMorgan JPMorgan, INASDAQ NASDAQ)
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        Task<StockOptions> funNordea() => Task.Run( () =>
        {
            return Nordea.simulatePriceChange(ref _simulationLock);
        }, token);

        Task<StockOptions> funJPMorgan() => Task.Run(() =>
        {
            return JPMorgan.simulatePriceChange(ref _simulationLock);
        }, token);

        Task<StockOptions> funNASDAQ() => Task.Run(() =>
        {
            return NASDAQ.simulatePriceChange(ref _simulationLock);
        }, token);

        // Start three tasks
        Task<StockOptions>[] tasks = { funNordea(), funJPMorgan(), funNASDAQ() };

        // Wait for the first task to complete
        Task<StockOptions> firstCompleted = await Task.WhenAny(tasks);

        // Cancel the remaining tasks
        cts.Cancel();

        // Return the result of the first completed task
        return await firstCompleted;
    }

    public void Stop()
    {
        _cts.Cancel();
    }
}