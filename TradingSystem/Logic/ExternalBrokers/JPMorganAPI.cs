using Microsoft.Extensions.Options;
using System.Xml.Linq;
using TradingSystem.Data;
using TradingSystem.Logic.LoggerExtensions;

namespace TradingSystem.Logic.ExternalBrokers
{
    public interface IJPMorgan
    {
        public Stock simulatePriceChange(int simSpeed, ref Lock simulationLock, ref CancellationToken token, ref bool first);
        public Dictionary<string, float> getPrices();
        public List<string> getStocks();
    }

    public class JPMorganAPI : IJPMorgan
    {
        private readonly ILogger<JPMorganAPI> _logger;
        private List<string> myStocks = new();
        private Dictionary<string, float> myPrices = new Dictionary<string, float>();
        private Random rand = new Random();
        
        public JPMorganAPI(ILogger<JPMorganAPI> logger, IOptions<BrokerStocks> brokerStocks)
        {
            _logger = logger;
            var options = brokerStocks.Value;
            foreach (string name in options.JPMorgan)
            {
                myPrices.Add(name, 10.0f);
                myStocks.Add(name);
            }
        }

        public Dictionary<string,float> getPrices()
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
                    var price = (rand.Next(0, 2) > 0) ? myPrices[updateKey] - 0.1f : myPrices[updateKey] + 0.1f;
                    _logger.JpMorganApiUpdatePrice(updateKey, myPrices[updateKey], price);
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