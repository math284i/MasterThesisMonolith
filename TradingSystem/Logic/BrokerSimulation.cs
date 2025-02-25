using TradingSystem.Data;

namespace TradingSystem.Logic;


public interface IBrokerSimulation
{
    public float GetBrokerPrice(StockOptions stock);
}

public class BrokerSimulation() : IBrokerSimulation
{
    private Dictionary<StockOptions, float> myPrices;

    public float GetBrokerPrice(StockOptions stock)
    {
        return 0.0f;
    }
}
