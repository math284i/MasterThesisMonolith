using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingSystem.Data;
using TradingSystem.Logic;

namespace TradingSystem.Tests;

public class BookTests
{
    private IBook GetBook(IObservable observable, IDBHandler dBHandler)
    {
        var logger = NullLogger<Book>.Instance;
        return new Book(observable, logger, dBHandler);
    }

    private IDBHandler GetDBHandler(IObservable observable)
    {
        var brokerStocks = new BrokerStocks()
        {
            JPMorgan = ["DIS", "GME", "NVDA"],
            NASDAQ = ["NOVO-B", "AAPL", "PFE"],
            Nordea = ["TSLA", "META", "INTC"]
        };
        var tradingStocks = new InstrumentsOptions
        {
            Stocks = ["GME", "NVDA", "NOVO-B", "AAPL", "TSLA"]
        };
        var brokerOptions = Options.Create(brokerStocks);
        var tradingOptions = Options.Create(tradingStocks);
        return new DBHandler(observable, brokerOptions, tradingOptions, path: "TestFakeDB.json");
    }

    [Fact]
    public void CanPublishClients()
    {
        var observable = new Observable();
        var expectedClients = new List<ClientData>
        {
            new ClientData
            {
                Name = "Unit",
                Tier = Tier.Regular,
                Balance = 100.0m
            },
            new ClientData
            {
                Name = "Test",
                Tier = Tier.Regular,
                Balance = 100.0m
            }
        };
        var dbMock = new Mock<IDBHandler>();
        dbMock.Setup(e => e.GetAllClients()).Returns(expectedClients);

        var book = GetBook(observable, dbMock.Object);
        book.Start();

        var topic = TopicGenerator.TopicForAllClients();
        var messages = observable.GetPersistentMessages();

        Assert.True(messages.ContainsKey(topic));
        Assert.Equal(messages[topic], expectedClients);
    }

    [Fact]
    public void CanBookOrder()
    {
        var observable = new Mock<IObservable>().Object;
        var db = GetDBHandler(observable);
        var book = GetBook(observable, db);
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var instrument = "GME";
        var spread = 1.0m;
        var BuyTransaction = new TransactionData
        {
            BuyerId = buyerId,
            SellerId = Guid.Empty,
            InstrumentId = instrument,
            Size = 1,
            Price = 10.0m,
            SpreadPrice = spread,
            Succeeded = true
        };
        var SellTransaction = new TransactionData
        {
            BuyerId = Guid.Empty,
            SellerId = sellerId,
            InstrumentId = instrument,
            Size = 1,
            Price = 10.0m,
            SpreadPrice = spread,
            Succeeded = true
        };

        book.BookOrder(BuyTransaction);
        book.BookOrder(SellTransaction);
        var transactions = db.GetAllTransactions();

        //Have the transactions been booked
        Assert.True(transactions.Exists(x => x.BuyerId == buyerId));
        Assert.True(transactions.Exists(x => x.SellerId == sellerId));
        //Has Danske Bank been added as buyer/seller
        Assert.NotEqual(Guid.Empty, transactions.Find(x => x.BuyerId == buyerId).SellerId);
        Assert.NotEqual(Guid.Empty, transactions.Find(x => x.SellerId == sellerId).BuyerId);
        //Has the Spread been inverted when customer is selling
        Assert.Equal(-spread, transactions.Find(x => x.SellerId == sellerId).SpreadPrice);
    }

    [Fact]
    public void CanHedgeOrder()
    {
        var observable = new Mock<IObservable>().Object;
        var db = GetDBHandler(observable);
        var book = GetBook(observable, db);
        var broker = "JPMorgan";
        var instrument = "GME";
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var spread = 1.0m;
        //Using a buy transaction for test. Very similar cases for sell, so seems redundant to do twice.
        var transaction = new TransactionData
        {
            BuyerId = buyerId,
            SellerId = Guid.Empty,
            InstrumentId = instrument,
            Size = 1,
            Price = 20.0m,
            SpreadPrice = spread,
            Succeeded = true
        };

        book.HedgeOrder((transaction, broker));
        var transactions = db.GetAllTransactions();

        //Has the initial transaction been booked
        Assert.True(transactions.Exists(x => x.BuyerId == buyerId));
        //Does another transaction exist, in which the seller (i.e. danske bank) acts as buyer
        Assert.True(transactions.Exists(x => transactions.Exists(y => x.SellerId == y.BuyerId)));
        //Do two transactions exists with the same ID
        Assert.Equal(2, (transactions.Where(x => x.TransactionId == transactions.Find(y => y.BuyerId == buyerId).TransactionId)).Count());
        //Spread is removed on the second of the two hedged transactions
        Assert.Equal(0.0m, transactions.Find(x => (x.BuyerId != buyerId) && (x.TransactionId == transactions.Find(y => y.BuyerId == buyerId).TransactionId)).SpreadPrice);
    }
}