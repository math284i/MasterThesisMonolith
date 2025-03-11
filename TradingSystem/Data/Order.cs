namespace TradingSystem.Data;

public class Order
{
    public required Guid ClientId;
    public required OrderSide Side;
    public required StockOptions Stock;
}