namespace TradingSystem.Data
{
    public class TransactionData
    {
        public Guid transactionId { get; set; }
        public Guid buyerId { get; set; }
        public Guid sellerId { get; set; }
        public string instrumentId { get; set; }
        public int size { get; set; }
        public float price { get; set; }
        public DateTime time { get; set; } //Maybe string is better? depends on implementation i guess. Left as DateTime object for now
        public bool succeeded { get; set; }

    }
}
