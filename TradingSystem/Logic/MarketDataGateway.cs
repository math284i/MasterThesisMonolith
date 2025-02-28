using TradingSystem.Data;
using TradingSystem.Logic.ExternalBrokers;

namespace TradingSystem.Logic;

public interface IMarketDataGateway
{
    public void Start();
    public void Stop();
}


public class MarketDataGateway(IMessageBus messageBus, INordea nordea, IJPMorgan JPMorgan, INASDAQ NASDAQ)
{
    public const string REQUEST_MARKET_PRICE = "MarketDataGateway-requestMarketPrice";

    private readonly CancellationTokenSource _cts = new();
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
        Task.Run(() => RunLoop(_cts.Token)); // Run in a background task
    }

    private async Task RunLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            (string,float) result = await marketCheck(nordea, JPMorgan, NASDAQ);
            Console.WriteLine($"MarketDataGateWay sees that the price of {result.Item1} has update to {result.Item2}");
            //Send message about result on bus
        }
    }

    private async Task<(string, float)> marketCheck(INordea Nordea, IJPMorgan JPMorgan, INASDAQ NASDAQ)
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        Task<(string,float)> funNordea() => Task.Run( () =>
        {
            return Nordea.simulatePriceChange();
        }, token);

        Task<(string, float)> funJPMorgan() => Task.Run(() =>
        {
            return JPMorgan.simulatePriceChange();
        }, token);

        Task<(string, float)> funNASDAQ() => Task.Run(() =>
        {
            return NASDAQ.simulatePriceChange();
        }, token);

        // Start three tasks
        Task<(string, float)>[] tasks = { funNordea(), funJPMorgan(), funNASDAQ() };

        // Wait for the first task to complete
        Task<(string, float)> firstCompleted = await Task.WhenAny(tasks);

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