namespace TradingSystem.Data
{
    public class TransactionData
    {
        public Guid TransactionId { get; set; }
        public Guid BuyerId { get; set; }
        public Guid SellerId { get; set; }
        public string InstrumentId { get; set; }
        public int Size { get; set; }
        public float Price { get; set; }
        public float SpreadPrice { get; set; }
        public DateTime Time { get; set; } //Maybe string is better? depends on implementation i guess. Left as DateTime object for now
        public bool Succeeded { get; set; }

    }
}
