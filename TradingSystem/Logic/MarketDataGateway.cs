using TradingSystem.Data;
using TradingSystem.Logic.ExternalBrokers;

namespace TradingSystem.Logic;

public interface IMarketDataGateway
{
    public void Start();
    public void Stop();
    public Task<Stock> marketCheck();
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

        Task.Run(() => RunSimulation(_cts.Token));
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
        Dictionary<string, decimal> nordeaPrices = nordea.getPrices();
        Dictionary<string, decimal> JPMorganPrices = JPMorgan.getPrices();
        Dictionary<string, decimal> NASDAQPrices = NASDAQ.getPrices();
        
        foreach (Stock stock in _stockOptions)
        {
            _instrumentIds.Add(stock.InstrumentId);

            //Find minimal price of instrument on the market.
            var minMarketPrice = decimal.MaxValue;
            if (nordeaPrices.ContainsKey(stock.InstrumentId) && nordeaPrices[stock.InstrumentId] < minMarketPrice)
            {
                minMarketPrice = nordeaPrices[stock.InstrumentId];
            }
            if (JPMorganPrices.ContainsKey(stock.InstrumentId) && JPMorganPrices[stock.InstrumentId] < minMarketPrice)
            {
                minMarketPrice = JPMorganPrices[stock.InstrumentId];
            }
            if (NASDAQPrices.ContainsKey(stock.InstrumentId) && NASDAQPrices[stock.InstrumentId] < minMarketPrice)
            {
                minMarketPrice = NASDAQPrices[stock.InstrumentId];
            }
            if(minMarketPrice == decimal.MaxValue)
            {
                //Basecase - No price found, so none published
                minMarketPrice = 0.0m;
            }

            stock.Price = minMarketPrice;
            var stockTopic = TopicGenerator.TopicForMarketInstrumentPrice(stock.InstrumentId);
            observable.Publish(stockTopic, stock);
        }
    }

    public async Task RunSimulation(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Stock result = await marketCheck();
            if(_instrumentIds.Contains(result.InstrumentId))
            {
                var stockTopic = TopicGenerator.TopicForMarketInstrumentPrice(result.InstrumentId);
                observable.Publish(stockTopic, result);
            }
        }
    }

    public async Task<Stock> marketCheck()
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        bool firstInLocker = true;

        //Each API has a 1/simSpeed chance of simulating a price change every half second.
        Task<Stock> funNordea() => Task.Run( () =>
        {
            return nordea.simulatePriceChange(simSpeed, ref _simulationLock, ref token, ref firstInLocker);
        });

        Task<Stock> funJPMorgan() => Task.Run(() =>
        {
            return JPMorgan.simulatePriceChange(simSpeed, ref _simulationLock, ref token, ref firstInLocker);
        });

        Task<Stock> funNASDAQ() => Task.Run(() =>
        {
            return NASDAQ.simulatePriceChange(simSpeed, ref _simulationLock, ref token, ref firstInLocker);
        });
        
        Task<Stock>[] tasks = { funNordea(), funJPMorgan(), funNASDAQ() };
        
        Task<Stock> firstCompleted = await Task.WhenAny(tasks);

        cts.Cancel();

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