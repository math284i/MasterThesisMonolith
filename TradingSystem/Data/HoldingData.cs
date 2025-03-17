namespace TradingSystem.Data
{
    public class HoldingData
    {
        public Guid ClientId { get; set; }
        public string InstrumentId { get; set; }
        public int Size { get; set; }
        
        public int SellSize { get; set; }
    }
}
