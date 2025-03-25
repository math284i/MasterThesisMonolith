using Microsoft.Extensions.Options;
using TradingSystem.Data;
using TradingSystem.Logic.LoggerExtensions;

namespace TradingSystem.Logic.ExternalBrokers
{
    public interface INASDAQ
    {
        public Stock simulatePriceChange(int simSpeed, ref Lock simulationLock, ref CancellationToken token, ref bool first);
        public Dictionary<string, decimal> getPrices();
        public List<string> getStocks();
    }

    public class NASDAQAPI : INASDAQ
    {
        private readonly ILogger<NASDAQAPI> _logger;
        private List<string> myStocks = new();
        private Dictionary<string, decimal> myPrices = new Dictionary<string, decimal>();
        private Random rand = new Random();
        
        public NASDAQAPI(ILogger<NASDAQAPI> logger, IOptions<BrokerStocks> brokerStocks)
        {
            _logger = logger;
            var options = brokerStocks.Value;
            foreach (string name in options.NASDAQ)
            {
                myPrices.Add(name, 10.0m);
                myStocks.Add(name);
            }
        }

        public Dictionary<string, decimal> getPrices()
        {
            return myPrices;
        }
        public List<string> getStocks()
        {
            return myStocks;
        }

        public Stock simulatePriceChange(int simSpeed, ref Lock simulationLock, ref CancellationToken token, ref bool first)
        {
            while (rand.Next(0,simSpeed) > 0)
            {
                if (token.IsCancellationRequested)
                {
                    return new Stock();
                }
                Thread.Sleep(500);
            }

            lock (simulationLock)
            {
                if (!token.IsCancellationRequested && first)
                {
                    first = false;
                    var updateKey = myPrices.ElementAt(rand.Next(0, myPrices.Count)).Key;
                    var price = (rand.Next(0, 2) > 0) ? myPrices[updateKey] - 0.1m : myPrices[updateKey] + 0.1m;
                    _logger.NasdaqApiUpdatePrice(updateKey, myPrices[updateKey], price);
                    myPrices[updateKey] = price;
                    var updatedStock = new Stock
                    {
                        InstrumentId = updateKey,
                        Price = price
                    };
                    return updatedStock;
                }else
                {
                    return new Stock();
                }
            }
        }

    }
}
