namespace TradingSystem.Data;

public class Order : ICloneable
{
    public required Guid ClientId;
    public required OrderSide Side;
    public required Stock Stock;
    public OrderStatus Status;
    public string ErrorMesssage = "";
    public decimal SpreadPrice;
    public bool HedgeOrder;
    public object Clone()
    {
        return new Order
        {
            ClientId = ClientId,
            Side = Side,
            Stock = (Stock)Stock.Clone(),
            Status = Status,
            ErrorMesssage = ErrorMesssage,
            SpreadPrice = SpreadPrice,
            HedgeOrder = HedgeOrder
        };
    }
}