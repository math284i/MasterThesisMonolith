namespace TradingSystem.Data;

public class TradingOptions
{
    public const string SectionName = "TradingOptions";
    public List<string> Stocks { get; set; } = [];
}