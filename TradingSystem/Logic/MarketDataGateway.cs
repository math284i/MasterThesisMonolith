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

    public void Start()
    {
        messageBus.Subscribe<HashSet<StockOptions>>("allInstruments", stocks =>
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

        bool firstInLocker = true;

        Task<StockOptions> funNordea() => Task.Run( () =>
        {
            return Nordea.simulatePriceChange(ref _simulationLock, ref token, ref firstInLocker);
        });

        Task<StockOptions> funJPMorgan() => Task.Run(() =>
        {
            return JPMorgan.simulatePriceChange(ref _simulationLock, ref token, ref firstInLocker);
        });

        Task<StockOptions> funNASDAQ() => Task.Run(() =>
        {
            return NASDAQ.simulatePriceChange(ref _simulationLock, ref token, ref firstInLocker);
        });

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