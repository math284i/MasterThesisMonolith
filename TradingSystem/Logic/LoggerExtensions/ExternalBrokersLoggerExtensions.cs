namespace TradingSystem.Logic.LoggerExtensions;

public static partial class ExternalBrokersLoggerExtensions
{
    [LoggerMessage(LogLevel.Debug, "JPMorganAPI updating price for {InstrumentId} old price {OldPrice} new price {NewPrice}")]
    public static partial void JpMorganApiUpdatePrice(this ILogger logger, string instrumentId, decimal oldPrice, decimal newPrice);
    
    [LoggerMessage(LogLevel.Debug, "NASDAQAPI updating price for {InstrumentId} old price {OldPrice} new price {NewPrice}")]
    public static partial void NasdaqApiUpdatePrice(this ILogger logger, string instrumentId, decimal oldPrice, decimal newPrice);
    
    [LoggerMessage(LogLevel.Debug, "NordeaAPI updating price for {InstrumentId} old price {OldPrice} new price {NewPrice}")]
    public static partial void NordeaApiUpdatePrice(this ILogger logger, string instrumentId, decimal oldPrice, decimal newPrice);
    
}