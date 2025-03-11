namespace TradingSystem.Data;

public class Order
{
    public required string ClientId;
    public required OrderSide Side;
    public required StockOptions Stock;
}