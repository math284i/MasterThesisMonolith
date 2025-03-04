using Microsoft.Extensions.Options;
using TradingSystem.Data;

namespace TradingSystem.Logic.ExternalBrokers
{
    public interface IJPMorgan
    {
        public StockOptions simulatePriceChange(ref Lock simulationLock);
    }

    public class JPMorganAPI : IJPMorgan
    {
        private Dictionary<string, float> myPrices = new Dictionary<string, float>();
        private Random rand = new Random();
        public JPMorganAPI(IOptions<BrokerStocks> brokerStocks)
        {
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
                Console.WriteLine("Updated price of JPMorgan stock: " + updateKey + " from " + myPrices[updateKey] + " to " + price);
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