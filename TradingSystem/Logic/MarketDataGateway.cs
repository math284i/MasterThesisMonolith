using TradingSystem.Data;
using TradingSystem.Logic.ExternalBrokers;

namespace TradingSystem.Logic;

public interface IMarketDataGateway
{
    public void Start();
    public void Stop();
}


public class MarketDataGateway(IObservable observable, INordea nordea, IJPMorgan JPMorgan, INASDAQ NASDAQ) : IMarketDataGateway
{
    private HashSet<Stocks> _stockOptions = new HashSet<Stocks>();
    private HashSet<string> _instrumentIds = new HashSet<string>();
    private readonly CancellationTokenSource _cts = new();
    private Lock _simulationLock = new();
    private const string Id = "marketDataGateway";

    public void Start()
    {
        var topic = TopicGenerator.TopicForAllInstruments();
        observable.Subscribe<HashSet<Stocks>>(topic, Id, stocks =>
        {
            _stockOptions = stocks;
        });

        Dictionary<string, float> nordeaPrices = nordea.getPrices();
        Dictionary<string, float> JPMorganPrices = JPMorgan.getPrices();
        Dictionary<string, float> NASDAQPrices = NASDAQ.getPrices();

        //Need a set of only instrumentIds, as price changes mean that looking up in the stockoptions set will not function
        foreach (Stocks stock in _stockOptions)
        {
            _instrumentIds.Add(stock.InstrumentId);

            //Publish initial prices of stocks to bus
            var minMarketPrice = float.MaxValue;
            if(nordeaPrices.ContainsKey(stock.InstrumentId) && nordeaPrices[stock.InstrumentId] < minMarketPrice)
            {
                minMarketPrice = nordeaPrices[stock.InstrumentId];
            }
            else if (JPMorganPrices.ContainsKey(stock.InstrumentId) && JPMorganPrices[stock.InstrumentId] < minMarketPrice)
            {
                minMarketPrice = JPMorganPrices[stock.InstrumentId];
            }
            else if (NASDAQPrices.ContainsKey(stock.InstrumentId) && NASDAQPrices[stock.InstrumentId] < minMarketPrice)
            {
                minMarketPrice = NASDAQPrices[stock.InstrumentId];
            }
            else
            {
                minMarketPrice = 0.0f;
            }

            stock.Price = minMarketPrice;
            var stockTopic = TopicGenerator.TopicForMarketInstrumentPrice(stock.InstrumentId);
            observable.Publish(stockTopic, stock);
        }

        Task.Run(() => RunLoop(_cts.Token)); // Run in a background task
    }

    private async Task RunLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Stocks result = await marketCheck(nordea, JPMorgan, NASDAQ);
            if(_instrumentIds.Contains(result.InstrumentId))
            {
                var stockTopic = TopicGenerator.TopicForMarketInstrumentPrice(result.InstrumentId);
                observable.Publish(stockTopic, result);
            }
        }
    }

    private async Task<Stocks> marketCheck(INordea Nordea, IJPMorgan JPMorgan, INASDAQ NASDAQ)
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        bool firstInLocker = true;

        //Each API has a 1/simSpeed chance of simulating a price change every half second.
        int simSpeed = 25;

        Task<Stocks> funNordea() => Task.Run( () =>
        {
            return Nordea.simulatePriceChange(simSpeed, ref _simulationLock, ref token, ref firstInLocker);
        });

        Task<Stocks> funJPMorgan() => Task.Run(() =>
        {
            return JPMorgan.simulatePriceChange(simSpeed, ref _simulationLock, ref token, ref firstInLocker);
        });

        Task<Stocks> funNASDAQ() => Task.Run(() =>
        {
            return NASDAQ.simulatePriceChange(simSpeed, ref _simulationLock, ref token, ref firstInLocker);
        });

        // Start three tasks
        Task<Stocks>[] tasks = { funNordea(), funJPMorgan(), funNASDAQ() };

        // Wait for the first task to complete
        Task<Stocks> firstCompleted = await Task.WhenAny(tasks);

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