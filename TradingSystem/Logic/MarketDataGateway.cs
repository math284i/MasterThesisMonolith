using TradingSystem.Data;
using TradingSystem.Logic.ExternalBrokers;

namespace TradingSystem.Logic;

public interface IMarketDataGateway
{
    public void Start();
    public void Stop();

    public void setSimSpeed(int newSpeed);
}


public class MarketDataGateway(IObservable observable, INordea nordea, IJPMorgan JPMorgan, INASDAQ NASDAQ) : IMarketDataGateway
{
    private HashSet<Stock> _stockOptions = new HashSet<Stock>();
    private HashSet<string> _instrumentIds = new HashSet<string>();
    private readonly CancellationTokenSource _cts = new();
    private Lock _simulationLock = new();
    private const string Id = "marketDataGateway";
    private int simSpeed = 25;

    public void Start()
    {
        SubscribeToInstruments();
        PublishInitialMarketPrice();

        Task.Run(() => RunSimulation(_cts.Token)); // Run in a background task
    }

    private void SubscribeToInstruments()
    {
        var topic = TopicGenerator.TopicForAllInstruments();
        observable.Subscribe<HashSet<Stock>>(topic, Id, stocks =>
        {
            _stockOptions = stocks;
        });
    }
    private void PublishInitialMarketPrice()
    {
        Dictionary<string, float> nordeaPrices = nordea.getPrices();
        Dictionary<string, float> JPMorganPrices = JPMorgan.getPrices();
        Dictionary<string, float> NASDAQPrices = NASDAQ.getPrices();

        //Need a set of only instrumentIds, as price changes mean that looking up in the stockoptions set will not function
        foreach (Stock stock in _stockOptions)
        {
            _instrumentIds.Add(stock.InstrumentId);

            //Find minimal price of instrument on the market.
            var minMarketPrice = float.MaxValue;
            if (nordeaPrices.ContainsKey(stock.InstrumentId) && nordeaPrices[stock.InstrumentId] < minMarketPrice)
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
                //Basecase - No price found, so none published
                minMarketPrice = 0.0f;
            }

            stock.Price = minMarketPrice;
            var stockTopic = TopicGenerator.TopicForMarketInstrumentPrice(stock.InstrumentId);
            observable.Publish(stockTopic, stock);
        }
    }

    private async Task RunSimulation(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Stock result = await marketCheck(nordea, JPMorgan, NASDAQ);
            if(_instrumentIds.Contains(result.InstrumentId))
            {
                var stockTopic = TopicGenerator.TopicForMarketInstrumentPrice(result.InstrumentId);
                observable.Publish(stockTopic, result);
            }
        }
    }

    private async Task<Stock> marketCheck(INordea Nordea, IJPMorgan JPMorgan, INASDAQ NASDAQ)
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        bool firstInLocker = true;

        //Each API has a 1/simSpeed chance of simulating a price change every half second.

        Task<Stock> funNordea() => Task.Run( () =>
        {
            return Nordea.simulatePriceChange(simSpeed, ref _simulationLock, ref token, ref firstInLocker);
        });

        Task<Stock> funJPMorgan() => Task.Run(() =>
        {
            return JPMorgan.simulatePriceChange(simSpeed, ref _simulationLock, ref token, ref firstInLocker);
        });

        Task<Stock> funNASDAQ() => Task.Run(() =>
        {
            return NASDAQ.simulatePriceChange(simSpeed, ref _simulationLock, ref token, ref firstInLocker);
        });

        // Start three tasks
        Task<Stock>[] tasks = { funNordea(), funJPMorgan(), funNASDAQ() };

        // Wait for the first task to complete
        Task<Stock> firstCompleted = await Task.WhenAny(tasks);

        // Cancel the remaining tasks
        cts.Cancel();

        // Return the result of the first completed task
        return await firstCompleted;
    }

    public void Stop()
    {
        _cts.Cancel();
        var topic = TopicGenerator.TopicForAllInstruments();
        observable.Unsubscribe(topic, Id);

        _stockOptions = new();
        _instrumentIds = new();
    }

    public void setSimSpeed(int newSpeed)
    {
        if(newSpeed > 0)
        {
            simSpeed = newSpeed;
        }
    }

}