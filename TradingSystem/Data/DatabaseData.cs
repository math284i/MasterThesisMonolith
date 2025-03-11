namespace TradingSystem.Data
{
    public class DatabaseData
    {
        public List<ClientData> clients { get; set; }
        public List<CustomerData> customers { get; set; }
        public List<HoldingData> holdings { get; set; }
        public List<TransactionData> transactions { get; set; }
        public List<SaltData> Salts { get; set; }
    }
}
