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
            var topicClient = TopicGenerator.TopicForHoldingOfClient(client.clientId.ToString());
            _messageBus.Publish(topicClient, GetClientHoldings(client.clientId));
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
        Serialize(db);
        return;
    }

    //If you are a customer, you are also automatically a client. This relation does not go the other way.
    //For instance, brokers are clients, but do not need username/password for our system.
    public void AddClientCustomer(string name, string username, string password)
    {
        DatabaseData db = DeserializeDB();

        if (db.customers.Exists(x => x.username.Equals(username)) || db.clients.Exists(x => x.name.Equals(name)))
        {
            return;
        }

        Guid ID = Guid.NewGuid();

        //TODO: Hash password before storing
        CustomerData customer = new CustomerData
        {
            clientId = ID,
            username = username,
            password = password
        };
        db.customers.Add(customer);

        ClientData client = new ClientData
        {
            clientId = ID,
            name = name,
            balance = 100.0f, //TODO: Figure out a good starting balance for new clients
            tier = "Average" //TODO: Figure out a good tier system
        };
        db.clients.Add(client);

        Serialize(db);
        return;
    }

    public void AddTransaction(TransactionData transaction)
    {
        var db = DeserializeDB();
        transaction.TransactionId = Guid.NewGuid();
        transaction.Time = DateTime.UtcNow;
        
        db.transactions.Add(transaction);
        if(transaction.Succeeded)
        {
            db = UpdateHoldings(db, transaction);
            var buyer = db.clients.Find(x => x.clientId == transaction.BuyerId);
            if(buyer != null)
            {
                db.clients.Remove(buyer);
                buyer.balance -= transaction.Size * transaction.Price;
                db.clients.Add(buyer);
                var topic = TopicGenerator.TopicForHoldingOfClient(buyer.clientId.ToString());
                _messageBus.Publish(topic, GetClientHoldings(buyer.clientId), isTransient: true);
            }
            var seller = db.clients.Find(x => x.clientId == transaction.SellerId);
            if (seller != null)
            {
                db.clients.Remove(seller);
                seller.balance += transaction.Size * transaction.Price;
                db.clients.Add(seller);
                var topic = TopicGenerator.TopicForHoldingOfClient(seller.clientId.ToString());
                _messageBus.Publish(topic, GetClientHoldings(seller.clientId), isTransient: true);
            }
        }
        Serialize(db);
        return;
    }

    private DatabaseData UpdateHoldings(DatabaseData db, TransactionData trans)
    {
        var currentHoldBuyer = db.holdings.Find(x => x.clientId == trans.BuyerId && x.instrumentId == trans.InstrumentId);
        if(currentHoldBuyer == null)
        {
            HoldingData newHolding = new HoldingData
            {
                clientId = trans.BuyerId,
                instrumentId = trans.InstrumentId,
                amount = trans.Size
            };
            db.holdings.Add(newHolding);
        }
        else
        {
            db.holdings.Remove(currentHoldBuyer);
            currentHoldBuyer.amount += trans.Size;
            db.holdings.Add(currentHoldBuyer);
            Console.WriteLine($"Adding holding for client: {currentHoldBuyer.clientId}");
        }

        var topicBuyer = TopicGenerator.TopicForHoldingOfClient(trans.BuyerId.ToString());
        _messageBus.Publish(topicBuyer, GetClientHoldings(trans.BuyerId));
        var currentHoldSeller = db.holdings.Find(x => x.clientId == trans.SellerId);
        if (currentHoldSeller == null)
        {
            HoldingData newHolding = new HoldingData
            {
                clientId = trans.SellerId,
                instrumentId = trans.InstrumentId,
                amount = -trans.Size
            };
            db.holdings.Add(newHolding);
        }
        else
        {
            Console.WriteLine($"Adding holding for seller: {currentHoldSeller.clientId}");
            db.holdings.Remove(currentHoldSeller);
            currentHoldSeller.amount -= trans.Size;
            db.holdings.Add(currentHoldSeller);
        }
        var topicSeller = TopicGenerator.TopicForHoldingOfClient(trans.SellerId.ToString());
        _messageBus.Publish(topicSeller, GetClientHoldings(trans.SellerId));
        return db;
    }

    public string GetClientTier(string name)
    {
        var db = DeserializeDB();
        var client = db.clients.Find(x => x.name.Equals(name));
        if(client == null)
        {
            return "Client not found"; //TODO: Proper error handling
        }
        return client.tier;
    }

    public float GetClientBalance(string name)
    {
        var db = DeserializeDB();
        var client = db.clients.Find(x => x.name.Equals(name));
        if (client == null)
        {
            return -1.0f; //TODO: Proper error handling
        }
        return client.balance;
    }

    public Guid GetClientGuid(string name)
    {
        var db = DeserializeDB();
        var client = db.clients.Find(x => x.name.Equals(name));
        if (client == null)
        {
            return Guid.NewGuid(); //TODO: Proper error handling
        }
        return client.clientId;
    }

    public List<TransactionData> GetClientTransactions(string name)
    {
        var db = DeserializeDB();
        var client = db.clients.Find(x => x.name.Equals(name));
        if (client == null)
        {
            return new List<TransactionData>(); //TODO: Proper error handling
        }
        var transBuyer = db.transactions.FindAll(x => x.BuyerId == client.clientId);
        var transSeller = db.transactions.FindAll(x => x.SellerId == client.clientId);
        return transBuyer.Concat(transSeller).ToList();
    }

    public List<HoldingData> GetClientHoldings(Guid clientId)
    {
        var db = DeserializeDB();

        List<HoldingData> holdings = db.holdings.FindAll(x => x.clientId == clientId);
        return holdings;
    }

    public List<ClientData> GetAllClients()
    {
        var db = DeserializeDB();
        return db.clients;
    }

    private void CheckLogin(LoginInfo info)
    {
        var db = DeserializeDB();

        var username = info.Username;
        var password = info.Password;

        //TODO: Hash password with same algorithm as when the customer was created, before checking
        var customer = db.customers.Find(x => x.username.Equals(username) && x.password.Equals(password));
        info.IsAuthenticated = customer != null;
        info.ClientId = customer == null ?  Guid.Empty : customer.clientId;

        var topic = TopicGenerator.TopicForLoginResponse();
        _messageBus.Publish(topic, info, isTransient: true);
        return;
    }

    public void ResetDB()
    {
        DatabaseData db = new DatabaseData
        {
            clients = new List<ClientData>(),
            customers = new List<CustomerData>(),
            transactions = new List<TransactionData>(),
            holdings = new List<HoldingData>()
        };
        db.clients.Add(new ClientData
        {
            clientId = Guid.NewGuid(),
            name = "Danske Bank",
            balance = 1000000.0f,
            tier = "internal"
        });

        Guid user1 = Guid.NewGuid();
        db.clients.Add(new ClientData
        {
            clientId = user1,
            name = "Anders",
            balance = 100.0f,
            tier = "standard"
        });
        db.customers.Add(new CustomerData
        {
            clientId = user1,
            username = "KP",
            password = "KP"
        });

        Guid user2 = Guid.NewGuid();
        db.clients.Add(new ClientData
        {
            clientId = user2,
            name = "Mathias",
            balance = 100.0f,
            tier = "standard"
        });
        db.customers.Add(new CustomerData
        {
            clientId = user2,
            username = "Dyberg",
            password = "Dyberg"
        });

        //TODO: Add initial holdings
        Serialize(db);
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
}