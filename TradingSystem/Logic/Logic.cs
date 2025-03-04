namespace TradingSystem.Logic;

public class Logic : IHostedService
{
    private readonly IPricerEngine _pricerEngine;
    private readonly IMarketDataGateway _marketDataGateway;

    public Logic(IPricerEngine pricerEngine, IMarketDataGateway marketDataGateway)
    {
        _pricerEngine = pricerEngine;
        _marketDataGateway = marketDataGateway;
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _pricerEngine.Start();
        _marketDataGateway.Start();
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _pricerEngine.Stop();
        _marketDataGateway.Stop();
        
        return Task.CompletedTask;
    }
}