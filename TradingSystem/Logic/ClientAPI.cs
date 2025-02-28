using Microsoft.Extensions.Options;
using TradingSystem.Data;

namespace TradingSystem.Logic;

public interface IClient
{
    public HashSet<StockOptions> GetStockOptions<T>(Action<T> client);
}

public class ClientAPI : IClient
{
    private HashSet<StockOptions> _tradingOptions;
    private readonly List<Delegate> _clients;

    public ClientAPI(IMessageBus messageBus)
    {
        _tradingOptions = new HashSet<StockOptions>();
        _clients = new List<Delegate>();
        messageBus.Subscribe<HashSet<StockOptions>>("allInstruments", stockOptions =>
        {
            Console.WriteLine("ClientAPI found messages" + stockOptions);
            _tradingOptions = stockOptions;
            foreach (var client in _clients)
            {
                client.DynamicInvoke(stockOptions);
            }
        });
    }

    public HashSet<StockOptions> GetStockOptions<T>(Action<T> client)
    {
        _clients.Add(client);
        return _tradingOptions;
    }
}