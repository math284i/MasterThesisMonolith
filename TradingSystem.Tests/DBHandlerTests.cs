using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using TradingSystem.Data;
using TradingSystem.Logic;

namespace TradingSystem.Tests;

public class DBHandlerTests : IDisposable
{
    public void Dispose()
    {
        var db = GetDBHandler(new Observable());
        db.ResetDB();
    }

    private IDBHandler GetDBHandler(IObservable observable)
    {
        var brokerStocks = new BrokerStocks()
        {
            JPMorgan = ["DIS", "GME", "NVDA"],
            NASDAQ = ["NOVO-B", "AAPL", "PFE"],
            Nordea = ["TSLA", "META", "INTC"]
        };
        var tradingStocks = new InstrumentsOptions {
            Stocks = ["GME", "NVDA", "NOVO-B", "AAPL", "TSLA"]
        };
        var brokerOptions = Options.Create(brokerStocks);
        var tradingOptions = Options.Create(tradingStocks);
        return new DBHandler(observable, brokerOptions, tradingOptions, path: "TestFakeDB.json");
    }

    [Fact]
    public void CanPublishInitialData()
    {
        //The database should publish a message for each client as well as a message for the target positions
        var observable = new Observable();
        var db = GetDBHandler(observable);

        db.Start();

        var messages = observable.GetPersistentMessages();
        var clients = db.GetAllClients();
        var targetTopic = TopicGenerator.TopicForAllTargetPositions();
        var nonClientMessages = 1;

        //Check whether a message has been published for each client
        Assert.Equal(messages.Count - nonClientMessages, clients.Count);
        //Check whether the target position message has been published
        Assert.True(messages.ContainsKey(targetTopic));
        db.Stop();
    }

    [Fact]
    public void CanUpdateTargetPosition()
    {
        var db = GetDBHandler(new Observable());
        var instrument = "GME";
        var expectedTarget = 10;
        var target = new TargetPosition
        {
            InstrumentId = instrument,
            Target = expectedTarget
        };
        db.UpdateTargetPosition(target);
        var targets = db.GetTargetPositions();

        Assert.Equal(expectedTarget, targets.Find(x => x.InstrumentId == instrument).Target);
    }

    [Fact]
    public void CanAddClientAndCustomer()
    {
        //Adding a client will also add a customer, so both are tested here
        var db = GetDBHandler(new Observable());
        var name = "TestUser";
        var username = "Test";
        var password = "Password";
        var tier = Tier.Internal;

        db.AddClientCustomer(name, username, password, tier);
        var clients = db.GetAllClients();
        var customers = db.GetAllCustomers();
        var salts = db.GetAllSalts();

        //Is the client and customer added to the db
        Assert.True(clients.Exists(x => x.Name == name));
        Assert.True(customers.Exists(x => x.Username == username));
        //Has the customers password been hashed
        Assert.NotEqual(password, customers.Find(x => x.Username == username).Password);
        //Does a salt exist for the customers password
        Assert.True(customers.Exists(x => salts.Exists(y => x.ClientId == y.ClientId)));
    }

    [Fact]
    public void CanCheckLoginAttempt()
    {
        var observable = new Observable();
        var db = GetDBHandler(observable);
        var name = "TestUser1";
        var username = "Test1";
        var password = "Password1";
        var tier = Tier.Internal;
        db.AddClientCustomer(name, username, password, tier);

        var loginTrue = new LoginInfo
        {
            Username = username,
            Password = password
        };
        var loginFalse = new LoginInfo
        {
            Username = username,
            Password = "incorrect"
        };

        db.CheckLogin(loginTrue);
        db.CheckLogin(loginFalse);
        var messages = observable.GetTransientMessages();
        var topic = TopicGenerator.TopicForLoginResponse();
        List<LoginInfo> logins = messages[topic].ConvertAll(x => (LoginInfo)x);

        //Has the login been authenticated
        Assert.True(logins.Find(x => x.Username == username && x.Password == password).IsAuthenticated);
        Assert.False(logins.Find(x => x.Username == username && x.Password != password).IsAuthenticated);

        //Has the users ID been identified on succesful login
        Assert.NotEqual(Guid.Empty, logins.Find(x => x.Username == username && x.Password == password).ClientId);
        Assert.Equal(Guid.Empty, logins.Find(x => x.Username == username && x.Password != password).ClientId);
    }

    [Fact]
    public void CanAddTransactionAndUpdateHolding()
    {
        //Adding a transaction also updates holdings, so both are tested here
        var observable = new Observable();
        var db = GetDBHandler(observable);
        var name = "TestUser2";
        var username = "Test2";
        var password = "Password2";
        var tier = Tier.Internal;
        db.AddClientCustomer(name, username, password, tier);
        var clientId = db.GetClientGuid(name);
        var danskeBankId = db.GetClientGuid("Danske_Bank");
        var instrument = "GME";
        var price = 20.0m;
        var spread = 1.0m;
        var buySize = 2;
        var sellSize = 1;
        List<Guid> transIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        var transaction1 = new TransactionData
        {
            TransactionId = transIds[0],
            BuyerId = clientId,
            SellerId = danskeBankId,
            InstrumentId = instrument,
            Size = buySize,
            Price = price,
            SpreadPrice = spread,
            Succeeded = true
        };
        var transaction2 = new TransactionData
        {
            TransactionId = transIds[1],
            BuyerId = danskeBankId,
            SellerId = clientId,
            InstrumentId = instrument,
            Size = sellSize,
            Price = price,
            SpreadPrice = 0.0m,
            Succeeded = true
        };
        var transactionFalse = new TransactionData
        {
            TransactionId = transIds[2],
            BuyerId = danskeBankId,
            SellerId = clientId,
            InstrumentId = instrument,
            Size = sellSize,
            Price = price,
            SpreadPrice = 0.0m,
            Succeeded = false
        };

        db.AddTransaction(transaction1);
        db.AddTransaction(transaction2);
        db.AddTransaction(transactionFalse);
        var transactions = db.GetAllTransactions();
        var clientHoldings = db.GetClientHoldings(clientId);
        var bankHoldings = db.GetClientHoldings(danskeBankId);
        var expectedInitialBankHolding = 100;
        var expectedInitialBankBalance = 1000000.0m;
        var expectedInitialClientBalance = 1000.0m;
        var messages = observable.GetPersistentMessages();
        var clientTopic = TopicGenerator.TopicForDBDataOfClient(clientId.ToString());
        var bankTopic = TopicGenerator.TopicForDBDataOfClient(danskeBankId.ToString());

        //Are all transactions present in the DB
        Assert.True(transIds.TrueForAll(x => transactions.Exists(y => y.TransactionId == x)));
        //The holdings are updated (only accounting for succeeded transactions)
        Assert.Equal(buySize - sellSize, clientHoldings.Find(x => x.InstrumentId == instrument).Size);
        Assert.Equal(expectedInitialBankHolding - (buySize - sellSize), bankHoldings.Find(x => x.InstrumentId == instrument).Size);
        //Are the clients balances updated (only accounting for succeeded transactions)
        Assert.Equal(expectedInitialBankBalance + ((buySize * (price + spread) - sellSize * price)), db.GetClientBalance("Danske_Bank"));
        Assert.Equal(expectedInitialClientBalance - ((buySize * (price + spread) - sellSize * price)), db.GetClientBalance(name));
        //Are the updates communicated on the observable
        Assert.True(messages.ContainsKey(clientTopic));
        Assert.True(messages.ContainsKey(bankTopic));
    }
}