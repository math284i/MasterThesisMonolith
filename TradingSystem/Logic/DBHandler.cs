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

        if(db.clients.Exists(x => x.name.Equals(name)))
        {
            return;
        }

        ClientData client = new ClientData
        {
            clientId = Guid.NewGuid(),
            name = name,
            balance = 100.0f, //TODO: Figure out a good starting balance for new clients
            tier = "Average" //TODO: Figure out a good tier system
        };
        db.clients.Add(client);
        serialize(db);
        return;
    }

    //If you are a customer, you are also automatically a client. This relation does not go the other way.
    //For instance, brokers are clients, but do not need username/password for our system.
    public void addClientCustomer(string name, string username, string password)
    {
        DatabaseData db = deserializeDB();
        /*
        if (db.customers.Exists(x => x.username.Equals(username)) || db.clients.Exists(x => x.name.Equals(name)))
        {
            return;
        }
        */

        Guid ID = Guid.NewGuid();

        //TODO: Hash password before storing
        CustomerData customer = new CustomerData
        {
            clientId = ID,
            username = username,
            password = hashPassword(password, ID, db)
        };
        db.customers.Add(customer);

        ClientData client = new ClientData
        {
            clientId = ID,
            name = name,
            balance = 100.0f, //TODO: Figure out a good starting balance for new clients
            tier = "Regular" //TODO: Figure out a good tier system
        };
        db.clients.Add(client);

        serialize(db);
        return;
    }

    public void addTransaction(Guid buyerId, Guid sellerId, string instrumentId, int size, float price, bool succeeded)
    {
        DatabaseData db = deserializeDB();

        TransactionData trans = new TransactionData
        {
            transactionId = Guid.NewGuid(),
            buyerId = buyerId,
            sellerId = sellerId,
            instrumentId = instrumentId,
            size = size,
            price = price,
            time = DateTime.Now,
            succeeded = succeeded
        };
        db.transactions.Add(trans);
        if(succeeded)
        {
            db = updateHoldings(db, trans);
            var buyer = db.clients.Find(x => x.clientId == buyerId);
            if(buyer != null)
            {
                db.clients.Remove(buyer);
                buyer.balance -= size * price;
                db.clients.Add(buyer);
            }
            var seller = db.clients.Find(x => x.clientId == sellerId);
            if (seller != null)
            {
                db.clients.Remove(seller);
                seller.balance += size * price;
                db.clients.Add(seller);
            }
        }
        serialize(db);
        return;
    }

    private DatabaseData updateHoldings(DatabaseData db, TransactionData trans)
    {
        var currentHoldBuyer = db.holdings.Find(x => x.clientId == trans.buyerId && x.instrumentId == trans.instrumentId);
        if(currentHoldBuyer == null)
        {
            HoldingData newHolding = new HoldingData
            {
                clientId = trans.buyerId,
                instrumentId = trans.instrumentId,
                amount = trans.size
            };
            db.holdings.Add(newHolding);
        }
        else
        {
            db.holdings.Remove(currentHoldBuyer);
            currentHoldBuyer.amount += trans.size;
            db.holdings.Add(currentHoldBuyer);
        }

        var currentHoldSeller = db.holdings.Find(x => x.clientId == trans.sellerId);
        if (currentHoldSeller == null)
        {
            HoldingData newHolding = new HoldingData
            {
                clientId = trans.sellerId,
                instrumentId = trans.instrumentId,
                amount = -trans.size
            };
            db.holdings.Add(newHolding);
        }
        else
        {
            db.holdings.Remove(currentHoldSeller);
            currentHoldSeller.amount -= trans.size;
            db.holdings.Add(currentHoldSeller);
        }
        return db;
    }

    public string getClientTier(string name)
    {
        DatabaseData db = deserializeDB();
        var client = db.clients.Find(x => x.name.Equals(name));
        if(client == null)
        {
            return "Client not found"; //TODO: Proper error handling
        }
        return client.tier;
    }

    public float getClientBalance(string name)
    {
        DatabaseData db = deserializeDB();
        var client = db.clients.Find(x => x.name.Equals(name));
        if (client == null)
        {
            return -1.0f; //TODO: Proper error handling
        }
        return client.balance;
    }

    public Guid getClientGuid(string name)
    {
        DatabaseData db = deserializeDB();
        var client = db.clients.Find(x => x.name.Equals(name));
        if (client == null)
        {
            return Guid.NewGuid(); //TODO: Proper error handling
        }
        return client.clientId;
    }

    public List<TransactionData> getClientTransactions(string name)
    {
        DatabaseData db = deserializeDB();
        var client = db.clients.Find(x => x.name.Equals(name));
        if (client == null)
        {
            return new List<TransactionData>(); //TODO: Proper error handling
        }
        List<TransactionData> transBuyer = db.transactions.FindAll(x => x.buyerId == client.clientId);
        List<TransactionData> transSeller = db.transactions.FindAll(x => x.sellerId == client.clientId);
        return transBuyer.Concat(transSeller).ToList();
    }

    public List<HoldingData> getClientHoldings(string name)
    {
        DatabaseData db = deserializeDB();
        var client = db.clients.Find(x => x.name.Equals(name));
        if (client == null)
        {
            return new List<HoldingData>(); //TODO: Proper error handling
        }
        List<HoldingData> holdings = db.holdings.FindAll(x => x.clientId == client.clientId);
        return holdings;
    }

    public List<ClientData> getAllClients()
    {
        DatabaseData db = deserializeDB();
        return db.clients;
    }

    private void checkLogin(LoginInfo info)
    {
        DatabaseData db = deserializeDB();

        var username = info.Username;
        var password = info.Password;

        //TODO: Hash password with same algorithm as when the customer was created, before checking
        var customer = db.customers.Find(x => x.username.Equals(username) && x.password.Equals(hashPassword(password,x.clientId, db)));
        info.IsAuthenticated = customer != null;
        info.ClientId = customer == null ?  Guid.Empty : customer.clientId;

        var topic = TopicGenerator.TopicForLoginResponse();
        _messageBus.Publish(topic, info, isTransient: true);
        return;
    }

    public void resetDB()
    {
        DatabaseData db = new DatabaseData
        {
            clients = new List<ClientData>(),
            customers = new List<CustomerData>(),
            transactions = new List<TransactionData>(),
            holdings = new List<HoldingData>(),
            Salts = new List<SaltData>()
        };
        db.clients.Add(new ClientData
        {
            clientId = Guid.NewGuid(),
            name = "Danske Bank",
            balance = 1000000.0f,
            tier = "internal"
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
            Console.WriteLine("User: " + clientId.ToString() + " Made a new salt");
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