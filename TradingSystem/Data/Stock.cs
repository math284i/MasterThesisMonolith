namespace TradingSystem.Data;

public class Stock
{
    public string InstrumentId { get; set; }
    public bool EnableLivePrices { get; set; }
    public float Price { get; set; }
    public int Size { get; set; } = 1;
}