using System.Collections.Concurrent;

namespace TradingSystem.Logic;

public interface IMessageBus
{
    public void Subscribe<T>(Action<T> handler);
    public void Unsubscribe<T>(Action<T> handler);
    public void Publish<T>(T message);
}

public class MessageBus : IMessageBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _subscribers = new();

    public void Subscribe<T>(Action<T> handler)
    {
        var handlers = _subscribers.GetOrAdd(typeof(T), _ => new List<Delegate>());
        handlers.Add(handler);
    }

    public void Unsubscribe<T>(Action<T> handler)
    {
        throw new NotImplementedException();
    }

    public void Publish<T>(T message)
    {
        if (_subscribers.TryGetValue(typeof(T), out var handlers))
        {
            foreach (var handler in handlers)
            {
                ((Action<T>)handler)(message);
            }
        }
    }
}