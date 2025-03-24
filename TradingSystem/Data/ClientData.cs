﻿namespace TradingSystem.Data
{
    public class ClientData
    {
        public Guid ClientId { get; set; }
        public string Name { get; set; }
        public float Balance { get; set; }
        public Tier Tier { get; set; }
        
        public List<HoldingData> Holdings { get; set; }
    }
}
