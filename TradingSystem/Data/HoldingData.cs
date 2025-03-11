namespace TradingSystem.Data
{
    public class HoldingData
    {
        public Guid clientId { get; set; }
        public string instrumentId { get; set; }
        public int amount { get; set; }
    }
}
