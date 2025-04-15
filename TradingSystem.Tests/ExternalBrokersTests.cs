using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingSystem.Data;
using TradingSystem.Logic;
using TradingSystem.Logic.ExternalBrokers;

namespace TradingSystem.Tests;

public class ExternalBrokersTests
{
    [Fact]
    public void BrokersCanUpdatePrice()
    {
        var logger1 = NullLogger<JPMorganAPI>.Instance;
        var logger2 = NullLogger<NASDAQAPI>.Instance;
        var logger3 = NullLogger<NordeaAPI>.Instance;
        var brokerStocks = new BrokerStocks()
        {
            JPMorgan = ["DIS", "GME", "NVDA"],
            NASDAQ = ["NOVO-B", "AAPL", "PFE"],
            Nordea = ["TSLA", "META", "INTC"]
        };
        var options = Options.Create(brokerStocks);

        var JPMorgan = new JPMorganAPI(logger1, options);
        var NASDAQ = new NASDAQAPI(logger2, options);
        var nordea = new NordeaAPI(logger3, options);

        var simSpeed = 1;
        Lock[] locks = [new Lock(), new Lock(), new Lock()];
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        bool[] firsts = [true, true, true];

        var stock1 = JPMorgan.simulatePriceChange(simSpeed, ref locks[0], ref token, ref firsts[0]);
        var stock2 = NASDAQ.simulatePriceChange(simSpeed, ref locks[1], ref token, ref firsts[1]);
        var stock3 = nordea.simulatePriceChange(simSpeed, ref locks[2], ref token, ref firsts[2]);

        var expectedStartingPrice = 100.0m;
        var expectedPriceChange = 10.0m;
        //The returned stock has updated price correctly
        Assert.True(stock1.Price == (expectedStartingPrice + expectedPriceChange) || stock1.Price == (expectedStartingPrice - expectedPriceChange));
        Assert.True(stock2.Price == (expectedStartingPrice + expectedPriceChange) || stock2.Price == (expectedStartingPrice - expectedPriceChange));
        Assert.True(stock3.Price == (expectedStartingPrice + expectedPriceChange) || stock3.Price == (expectedStartingPrice - expectedPriceChange));
        //The returned stock is in the brokers inventory
        Assert.Contains(stock1.InstrumentId, brokerStocks.JPMorgan);
        Assert.Contains(stock2.InstrumentId, brokerStocks.NASDAQ);
        Assert.Contains(stock3.InstrumentId, brokerStocks.Nordea);
        //The broker updates their internal price to match the returned stock
        Assert.Equal(stock1.Price, JPMorgan.getPrices()[stock1.InstrumentId]);
        Assert.Equal(stock2.Price, NASDAQ.getPrices()[stock2.InstrumentId]);
        Assert.Equal(stock3.Price, nordea.getPrices()[stock3.InstrumentId]);
    }
}