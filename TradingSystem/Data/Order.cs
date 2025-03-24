namespace TradingSystem.Data;

public class Order
{
    public required Guid ClientId;
    public required OrderSide Side;
    public required Stocks Stock;
    public OrderStatus Status;
    public string ErrorMesssage = "";
    public float SpreadPrice;
    public bool HedgeOrder;
}