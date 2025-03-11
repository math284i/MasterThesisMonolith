namespace TradingSystem.Data;

public class StockOptions
{
    public string InstrumentId { get; set; }
    public bool EnableLivePrices { get; set; }
    public float Price { get; set; }
    public int Quantity { get; set; } = 1;
}