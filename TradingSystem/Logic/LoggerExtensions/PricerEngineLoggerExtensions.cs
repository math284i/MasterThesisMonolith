namespace TradingSystem.Logic.LoggerExtensions;

public static partial class PricerEngineLoggerExtensions
{
    [LoggerMessage(LogLevel.Information, "Pricer engine starting up...")]
    public static partial void PricerEngineStartUp(this ILogger logger);
    
    [LoggerMessage(LogLevel.Information, "Pricer engine started")]
    public static partial void PricerEngineStarted(this ILogger logger);
    
    [LoggerMessage(LogLevel.Information, "Pricer engine stopping...")]
    public static partial void PricerEngineStopping(this ILogger logger);
    
    [LoggerMessage(LogLevel.Information, "Pricer engine stopped")]
    public static partial void PricerEngineStopped(this ILogger logger);
    
    [LoggerMessage(LogLevel.Information, "Pricer engine received new price for {InstrumentId} with price {Price}")]
    public static partial void PricerEngineReceivedNewPrice(this ILogger logger, string instrumentId, float price);
    
}