using Microsoft.Extensions.Options;
using TradingSystem.Data;
using TradingSystem.Logic.LoggerExtensions;

namespace TradingSystem.Logic.ExternalBrokers
{
    public interface IJPMorgan
    {
        public StockOptions simulatePriceChange(ref Lock simulationLock);
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

        public StockOptions simulatePriceChange(ref Lock simulationLock)
        {
            while (rand.Next(0, 100) > 0)
                Thread.Sleep(100);

            lock (simulationLock)
            {
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
            }
        }
    }
}