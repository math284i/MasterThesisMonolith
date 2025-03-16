using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradingSystem.Data;

namespace TradingSystem.Logic;

public interface IDBHandler
{
    public void Start();
    public void Stop();
    public void addClient(string name);
    public void addClientCustomer(string name, string username, string password); //All clients added are also customers
    public void addTransaction(Guid buyerId, Guid sellerId, string instrumentId, int size, float price, bool succeeded);
    //public void addHolding(Guid clientId, string instrumentId, int amount); //Probably not necessary
    public string getClientTier(string name);
    public float getClientBalance(string name);
    public Guid getClientGuid(string name);
    public List<TransactionData> getClientTransactions(string name);
    public List<HoldingData> getClientHoldings(string name);

    public List<ClientData> getAllClients();

    public void resetDB();
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
        resetDB();
        var topic = TopicGenerator.TopicForLoginRequest();
        _messageBus.Subscribe<LoginInfo>(topic, Id, checkLogin);
    }

    public void Stop()
    {
        var topic = TopicGenerator.TopicForLoginRequest();
        _messageBus.Unsubscribe(topic, Id);
    }

    public void addClient(string name)
    {
        DatabaseData db = deserializeDB();

        if(db.Clients.Exists(x => x.Name.Equals(name)))
        {
            return;
        }

        ClientData client = new ClientData
        {
            ClientId = Guid.NewGuid(),
            Name = name,
            Balance = 100.0f, //TODO: Figure out a good starting balance for new clients
            Tier = "Average" //TODO: Figure out a good tier system
        };
        db.Clients.Add(client);
        serialize(db);
        return;
    }

    //If you are a customer, you are also automatically a client. This relation does not go the other way.
    //For instance, brokers are clients, but do not need username/password for our system.
    public void addClientCustomer(string name, string username, string password)
    {
        DatabaseData db = deserializeDB();

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
            Tier = "Regular" //TODO: Figure out a good tier system
        };
        db.Clients.Add(client);

        serialize(db);
        return;
    }

    public void addTransaction(Guid buyerId, Guid sellerId, string instrumentId, int size, float price, bool succeeded)
    {
        DatabaseData db = deserializeDB();

        TransactionData trans = new TransactionData
        {
            TransactionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = sellerId,
            InstrumentId = instrumentId,
            Size = size,
            Price = price,
            Time = DateTime.Now,
            Succeeded = succeeded
        };
        db.Transactions.Add(trans);
        if(succeeded)
        {
            db = updateHoldings(db, trans);
            var buyer = db.Clients.Find(x => x.ClientId == buyerId);
            if(buyer != null)
            {
                db.Clients.Remove(buyer);
                buyer.Balance -= size * price;
                db.Clients.Add(buyer);
            }
            var seller = db.Clients.Find(x => x.ClientId == sellerId);
            if (seller != null)
            {
                db.Clients.Remove(seller);
                seller.Balance += size * price;
                db.Clients.Add(seller);
            }
        }
        serialize(db);
        return;
    }

    private DatabaseData updateHoldings(DatabaseData db, TransactionData trans)
    {
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

        var currentHoldSeller = db.Holdings.Find(x => x.ClientId == trans.SellerId);
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
        return db;
    }

    public string getClientTier(string name)
    {
        DatabaseData db = deserializeDB();
        var client = db.Clients.Find(x => x.Name.Equals(name));
        if(client == null)
        {
            return "Client not found"; //TODO: Proper error handling
        }
        return client.Tier;
    }

    public float getClientBalance(string name)
    {
        DatabaseData db = deserializeDB();
        var client = db.Clients.Find(x => x.Name.Equals(name));
        if (client == null)
        {
            return -1.0f; //TODO: Proper error handling
        }
        return client.Balance;
    }

    public Guid getClientGuid(string name)
    {
        DatabaseData db = deserializeDB();
        var client = db.Clients.Find(x => x.Name.Equals(name));
        if (client == null)
        {
            return Guid.NewGuid(); //TODO: Proper error handling
        }
        return client.ClientId;
    }

    public List<TransactionData> getClientTransactions(string name)
    {
        DatabaseData db = deserializeDB();
        var client = db.Clients.Find(x => x.Name.Equals(name));
        if (client == null)
        {
            return new List<TransactionData>(); //TODO: Proper error handling
        }
        List<TransactionData> transBuyer = db.Transactions.FindAll(x => x.BuyerId == client.ClientId);
        List<TransactionData> transSeller = db.Transactions.FindAll(x => x.SellerId == client.ClientId);
        return transBuyer.Concat(transSeller).ToList();
    }

    public List<HoldingData> getClientHoldings(string name)
    {
        DatabaseData db = deserializeDB();
        var client = db.Clients.Find(x => x.Name.Equals(name));
        if (client == null)
        {
            return new List<HoldingData>(); //TODO: Proper error handling
        }
        List<HoldingData> holdings = db.Holdings.FindAll(x => x.ClientId == client.ClientId);
        return holdings;
    }

    public List<ClientData> getAllClients()
    {
        DatabaseData db = deserializeDB();
        return db.Clients;
    }

    private void checkLogin(LoginInfo info)
    {
        DatabaseData db = deserializeDB();

        var username = info.Username;
        var password = info.Password;

        var customer = db.Customers.Find(x => x.Username.Equals(username) && x.Password.Equals(hashPassword(password,x.ClientId, db)));
        info.IsAuthenticated = customer != null;
        info.ClientId = customer == null ?  Guid.Empty : customer.ClientId;

        var topic = TopicGenerator.TopicForLoginResponse();
        _messageBus.Publish(topic, info, isTransient: true);
        return;
    }

    public void resetDB()
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
        serialize(db);

        addClientCustomer("Anders", "KP", "KP");
        addClientCustomer("Mathias", "Dyberg", "KP");
        return;
    }

    private void serialize(DatabaseData db)
    {
        string jsonString = JsonSerializer.Serialize(db);
        File.WriteAllText(databaseFilePath, jsonString);
        return;
    }

    private DatabaseData deserializeDB()
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