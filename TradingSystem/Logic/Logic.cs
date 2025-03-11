using TradingSystem.Logic.LoggerExtensions;

namespace TradingSystem.Logic;

public class Logic : IHostedService
{
    private readonly ILogger<Logic> _logger;
    private readonly IPricerEngine _pricerEngine;
    private readonly IMarketDataGateway _marketDataGateway;
    private readonly IRiskCalculator _riskCalculator;
    private readonly IExecutionHandler _executionHandler;

    public Logic(ILogger<Logic> logger
        , IPricerEngine pricerEngine
        , IMarketDataGateway marketDataGateway
        , IRiskCalculator riskCalculator
        , IExecutionHandler executionHandler)
    {
        _logger = logger;
        _pricerEngine = pricerEngine;
        _marketDataGateway = marketDataGateway;
        _riskCalculator = riskCalculator;
        _executionHandler = executionHandler;
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.ServiceStartingUp();
        
        _pricerEngine.Start();
        _marketDataGateway.Start();
        _riskCalculator.Start();
        _executionHandler.Start();
        
        _logger.ServiceStarted();
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.ServiceStopped();
        
        _pricerEngine.Stop();
        _marketDataGateway.Stop();
        _riskCalculator.Stop();
        _executionHandler.Stop();
        
        _logger.ServiceStopped();
        
        return Task.CompletedTask;
    }
}