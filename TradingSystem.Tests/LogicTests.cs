using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingSystem.Logic;

namespace TradingSystem.Tests;

public class LogicTests
{
    private Logic.Logic GetNewLogic()
    {
        var logger = new NullLogger<Logic.Logic>();
        var pricerEngine = new Mock<IPricerEngine>().Object;
        var marketDataGateway = new Mock<IMarketDataGateway>().Object;
        var hedgeService = new Mock<IHedgeService>().Object;
        var riskCalculator = new Mock<IRiskCalculator>().Object;
        var executionHandler = new Mock<IExecutionHandler>().Object;
        var dbHandler = new Mock<IDBHandler>().Object;
        var book = new Mock<IBook>().Object;

        return new Logic.Logic(logger
            , pricerEngine
            , marketDataGateway
            , hedgeService
            , riskCalculator
            , executionHandler
            , dbHandler
            , book);
    }
    
    [Fact]
    public void CanStartServices()
    {
        var logic = GetNewLogic();
        var token = CancellationToken.None;
        var task = logic.StartAsync(token);
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void CanStopServices()
    {
        var logic = GetNewLogic();
        var token = CancellationToken.None;
        var task = logic.StopAsync(token);
        Assert.True(task.IsCompletedSuccessfully);
    }
}