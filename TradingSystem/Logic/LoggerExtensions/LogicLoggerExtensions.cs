namespace TradingSystem.Logic.LoggerExtensions;

public static partial class LogicLoggerExtensions
{
    [LoggerMessage(LogLevel.Information, "Service starting up...")]
    public static partial void ServiceStartingUp(this ILogger logger);
    
    [LoggerMessage(LogLevel.Information, "Service started")]
    public static partial void ServiceStarted(this ILogger logger);
    
    [LoggerMessage(LogLevel.Information, "Service stopping...")]
    public static partial void ServiceStopping(this ILogger logger);
    
    [LoggerMessage(LogLevel.Information, "Service stopped")]
    public static partial void ServiceStopped(this ILogger logger);
    
}