using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace TradingSystem.Logic
{
    public interface IMessageBus
    {
        void Subscribe<T>(string key, Action<T> handler);
        void Unsubscribe<T>(string key, Action<T> handler);
        void Publish<T>(string key, T message);
    }

    public class MessageBus : IMessageBus
    {
        private readonly ConcurrentDictionary<string, List<Delegate>> _subscribers = new();
        private readonly ConcurrentDictionary<string, object> _messagesOnTheBus = new();

        public void Subscribe<T>(string key, Action<T> handler)
        {
            var handlers = _subscribers.GetOrAdd(key, _ => new List<Delegate>());
            lock (handlers)
            {
                handlers.Add(handler);
            }
            
            if (_messagesOnTheBus.TryGetValue(key, out var existingMessage) && existingMessage is T typedMessage)
            {
                handler(typedMessage);
            }
        }

        public void Unsubscribe<T>(string key, Action<T> handler)
        {
            if (_subscribers.TryGetValue(key, out var handlers))
            {
                lock (handlers)
                {
                    handlers.Remove(handler);
                    if (handlers.Count == 0)
                    {
                        _subscribers.TryRemove(key, out _);
                    }
                }
            }
        }

        public void Publish<T>(string key, T message)
        {
            _messagesOnTheBus.AddOrUpdate(key, message, (_, _) => message);

            if (_subscribers.TryGetValue(key, out var handlers))
            {
                lock (handlers)
                {
                    foreach (var handler in handlers)
                    {
                        ((Action<T>)handler)(message);
                    }
                }
            }

            foreach (var messages in _messagesOnTheBus)
            {
                Console.WriteLine($"Message on the bus: [{messages.Key}]: {messages.Value}");
            }
            
        }
    }
}
