using Microsoft.Extensions.Options;
using TradingSystem.Data;

namespace TradingSystem.Logic.ExternalBrokers
{
    public interface INordea
    {
        public (string,float) simulatePriceChange();
    }

    public class NordeaAPI : INordea
    {
        private Dictionary<string, float> myPrices = new Dictionary<string, float>();
        private Random rand = new Random();
        public NordeaAPI(IOptions<BrokerStocks> brokerStocks)
        {
            var options = brokerStocks.Value;
            foreach (string name in options.Nordea)
                myPrices.Add(name, 10.0f);
        }
        
        public (string,float) simulatePriceChange()
        {
            while (rand.Next(0, 100) > 0)
                System.Threading.Thread.Sleep(500);

            var updateKey = myPrices.ElementAt(rand.Next(0, myPrices.Count)).Key;
            var price = (rand.Next(0, 2) > 0) ? myPrices[updateKey] - 0.1f : myPrices[updateKey] + 0.1f;
            Console.WriteLine("Updated price of Nordea stock: " + updateKey + " from " + myPrices[updateKey] + " to " + price);
            myPrices[updateKey] = price;
            return (updateKey,price);
        }
    }
}
