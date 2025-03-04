using Microsoft.Extensions.Options;
using TradingSystem.Data;

namespace TradingSystem.Logic.ExternalBrokers
{
    public interface INASDAQ
    {
        public StockOptions simulatePriceChange(ref Lock simulationLock, ref CancellationToken token, ref bool first);
        public Dictionary<string, float> getPrices();
    }

    public class NASDAQAPI : INASDAQ
    {
        private Dictionary<string, float> myPrices = new Dictionary<string, float>();
        private Random rand = new Random();

        public NASDAQAPI(IOptions<BrokerStocks> brokerStocks)
        {
            var options = brokerStocks.Value;
            foreach (string name in options.NASDAQ)
                myPrices.Add(name, 10.0f);
        }
        public Dictionary<string, float> getPrices()
        {
            return myPrices;
        }

        public StockOptions simulatePriceChange(ref Lock simulationLock, ref CancellationToken token, ref bool first)
        {
            while (rand.Next(0,50) > 0)
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
                    Console.WriteLine("Updated price of NASDAQ stock: " + updateKey + " from " + myPrices[updateKey] + " to " + price);
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
