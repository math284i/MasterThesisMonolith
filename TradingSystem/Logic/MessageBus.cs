using System.Collections.Concurrent;

namespace TradingSystem.Logic;

public interface IMessageBus
{
    public void Subscribe<T>(string key, Action<T> handler);
    public void Unsubscribe<T>(string key, Action<T> handler);
    public void Publish<T>(string key, T message);
}

public class MessageBus : IMessageBus
{
    private readonly ConcurrentDictionary<string, List<Delegate>> _subscribers = new();

    public void Subscribe<T>(string key, Action<T> handler)
    {
        var handlers = _subscribers.GetOrAdd(key, _ => new List<Delegate>());
        handlers.Add(handler);
    }
    public void Unsubscribe<T>(string key, Action<T> handler)
    {
        throw new NotImplementedException();
    }

    public void Publish<T>(string key, T message)
    {
        if (_subscribers.TryGetValue(key, out var handlers))
        {
            foreach (var handler in handlers)
            {
                ((Action<T>)handler)(message);
            }
        }
    }
}