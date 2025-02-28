namespace TradingSystem.DTO;

public class StreamInformation
{
    // string clientId, string instrumentId, bool enableLivePrices
    public string ClientId { get; set; }
    public string InstrumentId { get; set; }
    public bool EnableLivePrices { get; set; }
}