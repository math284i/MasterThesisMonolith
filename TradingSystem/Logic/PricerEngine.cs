using Microsoft.Extensions.Options;
using TradingSystem.Data;
using TradingSystem.DTO;
using TradingSystem.Setup;

namespace TradingSystem.Logic;

public interface IPricerEngineClient
{
    public void Stream(string clientId, string instrumentId, bool enableLivePrices);
}

public interface IPricerEngineInternal
{
    public void ThereIsUpdate(string instrumentId);
}

public class PricerEngine : IPricerEngineClient, IPricerEngineInternal
{
    public const string SUBSCRIBE_TO_PRICE_STREAM = "pricerEngine-subscribe-to-price-stream";
    private readonly Dictionary<string, HashSet<StockOptions>> _clientsDict = new();
    private readonly HashSet<StockOptions> _referencePrices;
    private readonly IMessageBus _messageBus;

    public PricerEngine(IOptions<TradingOptions> stocksOptions, IMessageBus messageBus)
    {
        _referencePrices = new HashSet<StockOptions>();
        _messageBus = messageBus;
        var stocks = stocksOptions.Value.Stocks;

        foreach (var stock in stocks)
        {
            var newStock = new StockOptions
            {
                InstrumentId = stock,
                EnableLivePrices = false,
                Price = GeneratePrice(stock)
            };
            _referencePrices.Add(newStock);
        }
        SetupMessageBus();
    }

    private void SetupMessageBus()
    {
        _messageBus.Publish("executionHandler-referencePrice", _referencePrices);
        _messageBus.Publish("client-referencePrice", _referencePrices);
        _messageBus.Subscribe<StreamInformation>(SUBSCRIBE_TO_PRICE_STREAM, Stream);
    }

    private void Stream(StreamInformation info)
    {
        if (!_clientsDict.ContainsKey(info.ClientId))
        {
            var tmpStocks = _referencePrices;
            tmpStocks.First(o => o.InstrumentId == info.InstrumentId).EnableLivePrices = info.EnableLivePrices;
            _clientsDict.Add(info.ClientId, tmpStocks);
        }
        else
        {
            var stockOption = _clientsDict[info.ClientId].First(o => o.InstrumentId == info.InstrumentId);
            stockOption.EnableLivePrices = info.EnableLivePrices;
        }

        if (info.EnableLivePrices)
        {
            _messageBus.Publish<float>(Client.CLIENT_STREAM_PRICE + info.ClientId,
                _clientsDict[info.ClientId].First(o => o.InstrumentId == info.InstrumentId).Price);
        }
    }

    public void ThereIsUpdate(string instrumentId)
    {
        var newPrice = GeneratePrice(instrumentId);
        UpdatePrice(instrumentId, newPrice);
    }

    private void UpdatePrice(string instrumentId, float price)
    {
        foreach (var client in _clientsDict)
        {
            // if (client.Value[instrumentId])
        }
    }

    private float GeneratePrice(string instrumentId)
    {
        CheckMarketPrice(instrumentId);
        CheckBook(instrumentId);
        return 0.0f;
    }

    private void CheckMarketPrice(string instrumentId)
    {
        
    }

    private void CheckBook(string instrumentId)
    {
        
    }
    
}