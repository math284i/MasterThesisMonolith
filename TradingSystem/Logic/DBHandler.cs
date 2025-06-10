using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Transactions;
using TradingSystem.Data;

namespace TradingSystem.Logic;

public interface IDBHandler
{
    public void Start();
    public void Stop();
    public void UpdateTargetPosition(TargetPosition newTarget);
    public void AddClient(string name, Tier tier);
    public void AddClientCustomer(string name, string username, string password, Tier tier);
    public void AddTransaction(TransactionData transaction);
    public Tier GetClientTier(string name);
    public decimal GetClientBalance(string name);
    public Guid GetClientGuid(string name);
    public List<TransactionData> GetClientTransactions(string name);
    public List<HoldingData> GetClientHoldings(Guid clientId);
    public List<HoldingData> GetAllHoldings();

    public List<ClientData> GetAllClients();
    public List<CustomerData> GetAllCustomers();
    public List<TransactionData> GetAllTransactions();
    public List<SaltData> GetAllSalts();

    public void CheckLogin(LoginInfo info);

    public void ResetDB();
    
    public List<TargetPosition> GetTargetPositions();
}

public class DBHandler : IDBHandler
{
    private string databaseFilePath;

    private readonly IObservable _observable;
    private const string Id = "DBHandler";
    private BrokerStocks _brokerStocks;
    private InstrumentsOptions _instrumentsOptions;
    private Lock _readerLock = new();

    public DBHandler(IObservable observable, IOptions<BrokerStocks> brokerStocks, IOptions<InstrumentsOptions> tradingOptions, string path = "Data\\FakeDB.json")
    {
        _observable = observable;
        databaseFilePath = Path.Combine(AppContext.BaseDirectory, path);

        //Used for resetting the database so that it matches the inventories and instruments that are defined in appsettings
        _brokerStocks = brokerStocks.Value;
        _instrumentsOptions = tradingOptions.Value;
    }

    public void Start()
    {
        //ResetDB();
        SubscribeToLogin();
        PublishAllClients();
        SetupTargetPositions();
    }

    private void SubscribeToLogin()
    {
        var topic = TopicGenerator.TopicForLoginRequest();
        _observable.Subscribe<LoginInfo>(topic, Id, CheckLogin);
    }
    private void PublishAllClients()
    {
        var allClients = GetAllClients();
        foreach (var client in allClients)
        {
            var topicClient = TopicGenerator.TopicForDBDataOfClient(client.ClientId.ToString());
            client.Holdings = GetClientHoldings(client.ClientId);
            _observable.Publish(topicClient, client);
        }
    }
    private void SetupTargetPositions()
    {
        var topicAllTargetPositions = TopicGenerator.TopicForAllTargetPositions();
        var allTargetPositions = GetTargetPositions();
        foreach (var topicTarget in allTargetPositions.Select(targetPosition => TopicGenerator.TopicForTargetPositionUpdate(targetPosition.InstrumentId)))
        {
            _observable.Subscribe<TargetPosition>(topicTarget, Id, UpdateTargetPosition);
        }
        _observable.Publish(topicAllTargetPositions, allTargetPositions);
    }
    
    public void UpdateTargetPosition(TargetPosition newTarget)
    {
        var db = DeserializeDB();

        var targetPosition = db.TargetPositions.Find(t => t.InstrumentId == newTarget.InstrumentId);
        if (targetPosition != null)
        {
            targetPosition.Target = newTarget.Target;
        }
        else
        {
            db.TargetPositions.Add(newTarget);
        }
        
        Serialize(db);
    }

    public void Stop()
    {
        var topic = TopicGenerator.TopicForLoginRequest();
        _observable.Unsubscribe(topic, Id);
    }

    public void AddClient(string name, Tier tier)
    {
        DatabaseData db = DeserializeDB();

        ClientData client = new ClientData
        {
            ClientId = Guid.NewGuid(),
            Name = name,
            Balance = 1000.0m,
            Tier = tier,
            Holdings = new List<HoldingData>(),
        };

        db.Clients.Add(client);
        Serialize(db);
        return;
    }

    //If you are a customer, you are also automatically a client. This relation does not go the other way.
    //For instance, brokers are clients, but do not need username/password for our system.
    public void AddClientCustomer(string name, string username, string password, Tier tier)
    {
        DatabaseData db = DeserializeDB();

        Guid ID = Guid.NewGuid();

        CustomerData customer = new CustomerData
        {
            ClientId = ID,
            Username = username,
            Password = hashPassword(password, ID, db)
        };
        db.Customers.Add(customer);

        ClientData client = new ClientData
        {
            ClientId = ID,
            Name = name,
            Balance = 1000.0m,
            Tier = tier,
            Holdings = new List<HoldingData>(),
        };
        db.Clients.Add(client);

        Serialize(db);
        return;
    }

    public void AddTransaction(TransactionData transaction)
    {
        Console.WriteLine("Add transaction is called for intrustment: " + transaction.InstrumentId);
        DatabaseData db = DeserializeDB();

        transaction.Time = DateTime.Now;
        db.Transactions.Add(transaction);
        if(transaction.Succeeded)
        {
            db = UpdateHoldings(db, transaction);
            var buyer = db.Clients.Find(x => x.ClientId == transaction.BuyerId);
            if(buyer != null)
            {
                db.Clients.Remove(buyer);
                buyer.Balance -= transaction.Size * (transaction.Price + transaction.SpreadPrice);
                db.Clients.Add(buyer);
                Serialize(db);
                var topic = TopicGenerator.TopicForDBDataOfClient(buyer.ClientId.ToString());
                buyer.Holdings = GetClientHoldings(buyer.ClientId);
                _observable.Publish(topic, buyer);
            }
            var seller = db.Clients.Find(x => x.ClientId == transaction.SellerId);
            if (seller != null)
            {
                db.Clients.Remove(seller);
                seller.Balance += transaction.Size * (transaction.Price + transaction.SpreadPrice);
                db.Clients.Add(seller);
                Serialize(db);
                var topic = TopicGenerator.TopicForDBDataOfClient(seller.ClientId.ToString());
                seller.Holdings = GetClientHoldings(seller.ClientId);
                _observable.Publish(topic, seller);
            }
        }
        Serialize(db);
        return;
    }

    private DatabaseData UpdateHoldings(DatabaseData db, TransactionData trans)
    {
        var currentHoldBuyer = db.Holdings.Find(x => x.ClientId == trans.BuyerId && x.InstrumentId == trans.InstrumentId);
        if(currentHoldBuyer == null)
        {
            HoldingData newHolding = new HoldingData
            {
                ClientId = trans.BuyerId,
                InstrumentId = trans.InstrumentId,
                Size = trans.Size,
                DateMaturity = trans.DateMaturity
            };
            db.Holdings.Add(newHolding);
        }
        else
        {
            db.Holdings.Remove(currentHoldBuyer);
            currentHoldBuyer.Size += trans.Size;
            db.Holdings.Add(currentHoldBuyer);
        }
        var currentHoldSeller = db.Holdings.Find(x => x.ClientId == trans.SellerId && x.InstrumentId == trans.InstrumentId);
        if (currentHoldSeller == null)
        {
            HoldingData newHolding = new HoldingData
            {
                ClientId = trans.SellerId,
                InstrumentId = trans.InstrumentId,
                Size = -trans.Size,
                DateMaturity = trans.DateMaturity
            };
            if(newHolding.Size > 0)
            {
                db.Holdings.Add(newHolding);
            }
        }
        else
        {
            db.Holdings.Remove(currentHoldSeller);
            currentHoldSeller.Size -= trans.Size;

            if(currentHoldSeller.Size > 0)
            {
                db.Holdings.Add(currentHoldSeller);
            }
        }
        return db;
    }

    public Tier GetClientTier(string name)
    {
        DatabaseData db = DeserializeDB();
        var client = db.Clients.Find(x => x.Name.Equals(name));
        if(client == null)
        {
            return Tier.ClientNotFound;
        }
        return client.Tier;
    }

    public decimal GetClientBalance(string name)
    {
        DatabaseData db = DeserializeDB();
        var client = db.Clients.Find(x => x.Name.Equals(name));
        if (client == null)
        {
            return -1.0m;
        }
        return client.Balance;
    }

    public Guid GetClientGuid(string name)
    {
        DatabaseData db = DeserializeDB();
        var client = db.Clients.Find(x => x.Name.Equals(name));
        if (client == null)
        {
            return Guid.Empty;
        }
        return client.ClientId;
    }

    public List<TransactionData> GetClientTransactions(string name)
    {
        DatabaseData db = DeserializeDB();
        var client = db.Clients.Find(x => x.Name.Equals(name));
        if (client == null)
        {
            return new List<TransactionData>();
        }
        List<TransactionData> transBuyer = db.Transactions.FindAll(x => x.BuyerId == client.ClientId);
        List<TransactionData> transSeller = db.Transactions.FindAll(x => x.SellerId == client.ClientId);
        return transBuyer.Concat(transSeller).ToList();
    }

    public List<HoldingData> GetClientHoldings(Guid clientId)
    {
        DatabaseData db = DeserializeDB();
        List<HoldingData> holdings = db.Holdings.FindAll(x => x.ClientId == clientId);
        return holdings;
    }

    public List<HoldingData> GetAllHoldings()
    {
        DatabaseData db = DeserializeDB();
        return db.Holdings;
    }

    public List<ClientData> GetAllClients()
    {
        DatabaseData db = DeserializeDB();
        return db.Clients;
    }
    public List<CustomerData> GetAllCustomers()
    {
        DatabaseData db = DeserializeDB();
        return db.Customers;
    }
    public List<TransactionData> GetAllTransactions()
    {
        DatabaseData db = DeserializeDB();
        return db.Transactions;
    }
    public List<SaltData> GetAllSalts()
    {
        DatabaseData db = DeserializeDB();
        return db.Salts;
    }

    public void CheckLogin(LoginInfo info)
    {
        var db = DeserializeDB();

        var username = info.Username;
        var password = info.Password;

        var customer = db.Customers.Find(x => x.Username.Equals(username) && x.Password.Equals(hashPassword(password,x.ClientId, db)));
        info.IsAuthenticated = customer != null;
        info.ClientId = customer == null ?  Guid.Empty : customer.ClientId;

        var topic = TopicGenerator.TopicForLoginResponse();
        _observable.Publish(topic, info, isTransient: true);
        return;
    }

    public void ResetDB()
    {
        DatabaseData db = new DatabaseData
        {
            Clients = new List<ClientData>(),
            Customers = new List<CustomerData>(),
            Transactions = new List<TransactionData>(),
            Holdings = new List<HoldingData>(),
            Salts = new List<SaltData>(),
            TargetPositions = new List<TargetPosition>()
        };

        var initialHoldingSize = 100;
        var initialBrokerBalance = 1000000.0m;
        var externalBrokerTier = Tier.External;

        var danskeBankGuid = Guid.NewGuid();
        db.Clients.Add(new ClientData
        {
            ClientId = danskeBankGuid,
            Name = "Danske_Bank",
            Balance = initialBrokerBalance,
            Tier = Tier.Internal,
        });
        foreach (string s in _instrumentsOptions.Stocks)
        {
            db.Holdings.Add(new HoldingData
            {
                ClientId = danskeBankGuid,
                InstrumentId = s,
                Size = initialHoldingSize
            });
            db.TargetPositions.Add(new TargetPosition
            {
                InstrumentId = s,
                Target = 97
            });
        }

        var nordeaGuid = Guid.NewGuid();
        db.Clients.Add(new ClientData
        {
            ClientId = nordeaGuid,
            Name = "Nordea",
            Balance = initialBrokerBalance,
            Tier = externalBrokerTier
        });
        foreach (string s in _brokerStocks.Nordea)
        {
            db.Holdings.Add(new HoldingData
            {
                ClientId = nordeaGuid,
                InstrumentId = s,
                Size = initialHoldingSize
            });
        }

        var NASDAQGuid = Guid.NewGuid();
        db.Clients.Add(new ClientData
        {
            ClientId = NASDAQGuid,
            Name = "NASDAQ",
            Balance = initialBrokerBalance,
            Tier = externalBrokerTier
        });
        foreach (string s in _brokerStocks.NASDAQ)
        {
            db.Holdings.Add(new HoldingData
            {
                ClientId = NASDAQGuid,
                InstrumentId = s,
                Size = initialHoldingSize
            });
        }

        var JPMorganGuid = Guid.NewGuid();
        db.Clients.Add(new ClientData
        {
            ClientId = JPMorganGuid,
            Name = "JPMorgan",
            Balance = initialBrokerBalance,
            Tier = externalBrokerTier
        });
        foreach (string s in _brokerStocks.JPMorgan)
        {
            db.Holdings.Add(new HoldingData
            {
                ClientId = JPMorganGuid,
                InstrumentId = s,
                Size = initialHoldingSize
            });
        }

        Serialize(db);

        AddClientCustomer("Anders", "KP", "KP", Tier.External);
        AddClientCustomer("Anders2", "KP2", "KP", Tier.Internal);
        AddClientCustomer("Mathias", "Dyberg", "Dyberg", Tier.Premium);
        return;
    }

    public List<TargetPosition> GetTargetPositions()
    {
        var db = DeserializeDB();
        return db.TargetPositions;
    }

    private void Serialize(DatabaseData db)
    {
        string jsonString = JsonSerializer.Serialize(db);
        lock (_readerLock)
        {
            File.WriteAllText(databaseFilePath, jsonString);
            return;
        }
    }

    private DatabaseData DeserializeDB()
    {
        lock(_readerLock)
        {
            string jsonString = File.ReadAllText(databaseFilePath);
            return JsonSerializer.Deserialize<DatabaseData>(jsonString)!;
        }
    }

    private string hashPassword(string password, Guid clientId, DatabaseData db)
    {
        var salt = db.Salts.Find(x => x.ClientId == clientId);
        if (salt == null)
        {
            salt = new SaltData
            {
                ClientId = clientId,
                Salt = System.Text.Encoding.Default.GetString(RandomNumberGenerator.GetBytes(16))
            };
            db.Salts.Add(salt);
        }
        string saltedPassword = salt.Salt + password;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(saltedPassword));
        return System.Text.Encoding.Default.GetString(hash);
    }
}