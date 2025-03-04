using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace TradingSystem.Logic
{
    public interface IMessageBus
    {
        void Subscribe<T>(string key, string subscriberId, Action<T> handler);
        void Unsubscribe(string key, string subscriberId);
        void Publish<T>(string key, T message);

        public Dictionary<string, List<string>> GetSubscribers();
        public Dictionary<string, object> GetMessages();
    }

    public class MessageBus : IMessageBus
    {
        private readonly ConcurrentDictionary<string, Dictionary<string, Delegate>> _subscribers = new();
        private readonly ConcurrentDictionary<string, object> _messagesOnTheBus = new();

        public void Subscribe<T>(string key, string subscriberId, Action<T> handler)
        {
            var handlers = _subscribers.GetOrAdd(key, _ => new Dictionary<string, Delegate>());
            lock (handlers)
            {
                handlers[subscriberId] = handler;
            }

            // If a message already exists on the bus for this key, deliver it immediately
            if (_messagesOnTheBus.TryGetValue(key, out var existingMessage) && existingMessage is T typedMessage)
            {
                handler(typedMessage);
            }
        }

        public void Unsubscribe(string key, string subscriberId)
        {
            if (_subscribers.TryGetValue(key, out var handlers))
            {
                lock (handlers)
                {
                    handlers.Remove(subscriberId);
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
                    foreach (var handler in handlers.Values)
                    {
                        ((Action<T>)handler)(message);
                    }
                }
            }
        }
        
        public Dictionary<string, List<string>> GetSubscribers()
        {
            return _subscribers.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Keys.OrderBy(id => id).ToList()
            );
        }

        public Dictionary<string, object> GetMessages()
        {
            return new Dictionary<string, object>(_messagesOnTheBus);
        }
        
    }
}