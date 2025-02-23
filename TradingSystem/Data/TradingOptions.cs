namespace TradingSystem.Data;

public class TradingOptions
{
    public const string SectionName = "TradingOptions";
    public HashSet<string> Stocks { get; set; } = [];
}