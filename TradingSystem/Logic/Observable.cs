using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace TradingSystem.Logic
{
    public interface IObservable
    {
        void Subscribe<T>(string key, string subscriberId, Action<T> handler);
        void Unsubscribe(string key, string subscriberId);
        void Publish<T>(string key, T message, bool isTransient = false);
        void ClearDBPersistantMessages();

        Dictionary<string, List<string>> GetSubscribers();
        Dictionary<string, object> GetPersistentMessages();
        Dictionary<string, List<object>> GetTransientMessages();
    }

    public class Observable : IObservable
    {
        private readonly ConcurrentDictionary<string, Dictionary<string, Delegate>> _subscribers = new();
        private readonly ConcurrentDictionary<string, object> _persistentMessages = new();
        private readonly ConcurrentDictionary<string, ConcurrentQueue<object>> _transientMessages = new();

        public void Subscribe<T>(string key, string subscriberId, Action<T> handler)
        {
            var handlers = _subscribers.GetOrAdd(key, _ => new Dictionary<string, Delegate>());
            lock (handlers)
            {
                handlers[subscriberId] = handler;
            }

            // Deliver existing persistent message
            if (_persistentMessages.TryGetValue(key, out var existingMessage) && existingMessage is T typedMessage)
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

        public void Publish<T>(string key, T message, bool isTransient = false)
        {
            if (isTransient)
            {
                var queue = _transientMessages.GetOrAdd(key, _ => new ConcurrentQueue<object>());
                queue.Enqueue(message);
            }
            else
            {
                _persistentMessages.AddOrUpdate(key, message, (_, _) => message);
            }
            
            if (_subscribers.TryGetValue(key, out var handlers))
            {
                lock (handlers)
                {
                    foreach (var handler in handlers.Values)
                    {
                        if (isTransient && _transientMessages.TryGetValue(key, out var queue))
                        {
                            while(queue.TryDequeue(out var dequeuedMessage) && dequeuedMessage is T transientMessage)
                            {
                                ((Action<T>)handler)(transientMessage);
                            }
                        }
                        else
                        {
                            ((Action<T>)handler)(message);
                        }
                    }
                }
            }
        }

        public void ClearDBPersistantMessages()
        {
            foreach (KeyValuePair<string,object> entry in _persistentMessages)
            {
                var topic = TopicGenerator.TopicForDBDataOfClient("");
                if (entry.Key.Contains(topic)) {
                    _persistentMessages.TryRemove(entry);
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

        public Dictionary<string, object> GetPersistentMessages()
        {
            return new Dictionary<string, object>(_persistentMessages);
        }
        
        public Dictionary<string, List<object>> GetTransientMessages()
        {
            return _transientMessages.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToList()
            );
        }
    }
}
