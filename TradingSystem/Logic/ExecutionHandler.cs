namespace TradingSystem.Logic;

public interface IExecutionHandler
{
    public void Start();
    
    public void Stop();
}

public class ExecutionHandler
{
    private readonly ILogger<ExecutionHandler> _logger;
    private readonly IMessageBus _messageBus;
    public ExecutionHandler(ILogger<ExecutionHandler> logger, IMessageBus messageBus)
    {
        _logger = logger;
        _messageBus = messageBus;
    }
}