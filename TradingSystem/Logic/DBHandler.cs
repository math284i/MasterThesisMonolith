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
    public void AddClient(string name);
    public void AddClientCustomer(string name, string username, string password); //All clients added are also customers
    public void AddTransaction(TransactionData transaction);
    //public void addHolding(Guid clientId, string instrumentId, int amount); //Probably not necessary
    public string GetClientTier(string name);
    public float GetClientBalance(string name);
    public Guid GetClientGuid(string name);
    public List<TransactionData> GetClientTransactions(string name);
    public List<HoldingData> GetClientHoldings(Guid clientId);

    public List<ClientData> GetAllClients();

    public void ResetDB();
}

public class DBHandler : IDBHandler
{
    private string databaseFilePath = ".\\Data\\QuoteUnquoteDB.json";

    private readonly IMessageBus _messageBus;
    private const string Id = "DBHandler";

    public DBHandler(IMessageBus messagebus)
    {
        _messageBus = messagebus;
    }

    public void Start()
    {
        ResetDB();
        var topic = TopicGenerator.TopicForLoginRequest();
        _messageBus.Subscribe<LoginInfo>(topic, Id, CheckLogin);
        var allClients = GetAllClients();
        foreach (var client in allClients)
        {
            var topicClient = TopicGenerator.TopicForDBDataOfClient(client.ClientId.ToString());
            client.Holdings = GetClientHoldings(client.ClientId);
            _messageBus.Publish(topicClient, client);
        }
    }

    public void Stop()
    {
        var topic = TopicGenerator.TopicForLoginRequest();
        _messageBus.Unsubscribe(topic, Id);
    }

    public void AddClient(string name)
    {
        DatabaseData db = DeserializeDB();

        if(db.Clients.Exists(x => x.Name.Equals(name)))
        {
            return;
        }

        ClientData client = new ClientData
        {
            ClientId = Guid.NewGuid(),
            Name = name,
            Balance = 100.0f, //TODO: Figure out a good starting balance for new clients
            Tier = "Average", //TODO: Figure out a good tier system
            Holdings = new List<HoldingData>(),
        };

        db.Clients.Add(client);
        Serialize(db);
        return;
    }

    //If you are a customer, you are also automatically a client. This relation does not go the other way.
    //For instance, brokers are clients, but do not need username/password for our system.
    public void AddClientCustomer(string name, string username, string password)
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
            Balance = 100.0f, //TODO: Figure out a good starting balance for new clients
            Tier = "Regular", //TODO: Figure out a good tier system
            Holdings = new List<HoldingData>(),
        };
        db.Clients.Add(client);

        Serialize(db);
        return;
    }

    public void AddTransaction(TransactionData transaction)
    {
        DatabaseData db = DeserializeDB();

        TransactionData trans = new TransactionData
        {
            TransactionId = Guid.NewGuid(),
            BuyerId = transaction.BuyerId,
            SellerId = transaction.SellerId,
            InstrumentId = transaction.InstrumentId,
            Size = transaction.Size,
            Price = transaction.Price,
            Time = DateTime.Now,
            Succeeded = transaction.Succeeded
        };
        db.Transactions.Add(trans);
        if(trans.Succeeded)
        {
            db = UpdateHoldings(db, trans);
            var buyer = db.Clients.Find(x => x.ClientId == trans.BuyerId);
            if(buyer != null)
            {
                db.Clients.Remove(buyer);
                buyer.Balance -= trans.Size * trans.Price;
                db.Clients.Add(buyer);
                Serialize(db);
                var topic = TopicGenerator.TopicForDBDataOfClient(buyer.ClientId.ToString());
                buyer.Holdings = GetClientHoldings(buyer.ClientId);
                Console.WriteLine($"Publishing buyer balance: {buyer.ClientId} {buyer.Balance}");
                _messageBus.Publish(topic, buyer);
            }
            var seller = db.Clients.Find(x => x.ClientId == trans.SellerId);
            if (seller != null)
            {
                db.Clients.Remove(seller);
                seller.Balance += trans.Size * trans.Price;
                db.Clients.Add(seller);
                Serialize(db);
                var topic = TopicGenerator.TopicForDBDataOfClient(seller.ClientId.ToString());
                seller.Holdings = GetClientHoldings(seller.ClientId);
                _messageBus.Publish(topic, seller);
            }
        }
        Serialize(db);
        return;
    }

    private DatabaseData UpdateHoldings(DatabaseData db, TransactionData trans)
    {
        Console.WriteLine($"DBHandler updating for {trans.InstrumentId}");
        var currentHoldBuyer = db.Holdings.Find(x => x.ClientId == trans.BuyerId && x.InstrumentId == trans.InstrumentId);
        if(currentHoldBuyer == null)
        {
            HoldingData newHolding = new HoldingData
            {
                ClientId = trans.BuyerId,
                InstrumentId = trans.InstrumentId,
                Size = trans.Size
            };
            db.Holdings.Add(newHolding);
        }
        else
        {
            db.Holdings.Remove(currentHoldBuyer);
            currentHoldBuyer.Size += trans.Size;
            db.Holdings.Add(currentHoldBuyer);
        }
        // var topicBuyer = TopicGenerator.TopicForHoldingOfClient(trans.BuyerId.ToString());
        // _messageBus.Publish(topicBuyer, GetClientHoldings(trans.BuyerId));
        var currentHoldSeller = db.Holdings.Find(x => x.ClientId == trans.SellerId && x.InstrumentId == trans.InstrumentId);
        if (currentHoldSeller == null)
        {
            HoldingData newHolding = new HoldingData
            {
                ClientId = trans.SellerId,
                InstrumentId = trans.InstrumentId,
                Size = -trans.Size
            };
            db.Holdings.Add(newHolding);
        }
        else
        {
            db.Holdings.Remove(currentHoldSeller);
            currentHoldSeller.Size -= trans.Size;
            db.Holdings.Add(currentHoldSeller);
        }
        // var topicSeller = TopicGenerator.TopicForHoldingOfClient(trans.SellerId.ToString());
        // _messageBus.Publish(topicSeller, GetClientHoldings(trans.SellerId));
        return db;
    }

    public string GetClientTier(string name)
    {
        DatabaseData db = DeserializeDB();
        var client = db.Clients.Find(x => x.Name.Equals(name));
        if(client == null)
        {
            return "Client not found"; //TODO: Proper error handling
        }
        return client.Tier;
    }

    public float GetClientBalance(string name)
    {
        DatabaseData db = DeserializeDB();
        var client = db.Clients.Find(x => x.Name.Equals(name));
        if (client == null)
        {
            return -1.0f; //TODO: Proper error handling
        }
        return client.Balance;
    }

    public Guid GetClientGuid(string name)
    {
        DatabaseData db = DeserializeDB();
        var client = db.Clients.Find(x => x.Name.Equals(name));
        if (client == null)
        {
            return Guid.NewGuid(); //TODO: Proper error handling
        }
        return client.ClientId;
    }

    public List<TransactionData> GetClientTransactions(string name)
    {
        DatabaseData db = DeserializeDB();
        var client = db.Clients.Find(x => x.Name.Equals(name));
        if (client == null)
        {
            return new List<TransactionData>(); //TODO: Proper error handling
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

    public List<ClientData> GetAllClients()
    {
        DatabaseData db = DeserializeDB();
        return db.Clients;
    }

    private void CheckLogin(LoginInfo info)
    {
        var db = DeserializeDB();

        var username = info.Username;
        var password = info.Password;

        var customer = db.Customers.Find(x => x.Username.Equals(username) && x.Password.Equals(hashPassword(password,x.ClientId, db)));
        info.IsAuthenticated = customer != null;
        info.ClientId = customer == null ?  Guid.Empty : customer.ClientId;

        var topic = TopicGenerator.TopicForLoginResponse();
        _messageBus.Publish(topic, info, isTransient: true);
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
            Salts = new List<SaltData>()
        };
        db.Clients.Add(new ClientData
        {
            ClientId = Guid.NewGuid(),
            Name = "Danske Bank",
            Balance = 1000000.0f,
            Tier = "internal"
        });

        //TODO: Add initial holdings
        Serialize(db);

        AddClientCustomer("Anders", "KP", "KP");
        AddClientCustomer("Mathias", "Dyberg", "KP");
        return;
    }

    private void Serialize(DatabaseData db)
    {
        string jsonString = JsonSerializer.Serialize(db);
        File.WriteAllText(databaseFilePath, jsonString);
        return;
    }

    private DatabaseData DeserializeDB()
    {
        string jsonString = File.ReadAllText(databaseFilePath);
        return JsonSerializer.Deserialize<DatabaseData>(jsonString)!;
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