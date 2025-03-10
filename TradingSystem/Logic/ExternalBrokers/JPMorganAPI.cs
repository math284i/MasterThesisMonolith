using Microsoft.Extensions.Options;
using TradingSystem.Data;
using TradingSystem.Logic.LoggerExtensions;

namespace TradingSystem.Logic.ExternalBrokers
{
    public interface IJPMorgan
    {
        public StockOptions simulatePriceChange(int simSpeed, ref Lock simulationLock, ref CancellationToken token, ref bool first);
        public Dictionary<string, float> getPrices();
    }

    public class JPMorganAPI : IJPMorgan
    {
        private readonly ILogger<JPMorganAPI> _logger;
        private Dictionary<string, float> myPrices = new Dictionary<string, float>();
        private Random rand = new Random();
        
        public JPMorganAPI(ILogger<JPMorganAPI> logger, IOptions<BrokerStocks> brokerStocks)
        {
            _logger = logger;
            var options = brokerStocks.Value;
            foreach (string name in options.JPMorgan)
                myPrices.Add(name, 10.0f);
        }

        public Dictionary<string,float> getPrices()
        {
            return myPrices;
        }

        public StockOptions simulatePriceChange(int simSpeed, ref Lock simulationLock, ref CancellationToken token, ref bool first)
        {
            while (rand.Next(0,simSpeed) > 0)
            {
                if (token.IsCancellationRequested)
                {
                    return new StockOptions();
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
                    var updatedStock = new StockOptions
                    {
                        InstrumentId = updateKey,
                        Price = price
                    };
                    return updatedStock;
                }else
                {
                    return new StockOptions();
                }
            }
        }
    }
}