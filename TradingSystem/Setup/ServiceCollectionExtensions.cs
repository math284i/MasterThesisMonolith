using TradingSystem.Data;
using TradingSystem.Logic;
using TradingSystem.Logic.ExternalBrokers;

namespace TradingSystem.Setup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection SetupJsLogging(this IServiceCollection services)
    {
        return services.AddScoped<JsLogger>();
    }

    public static IServiceCollection SetupExternalData(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BrokerStocks>(configuration.GetSection(BrokerStocks.SectionName));
        return services.Configure<InstrumentsOptions>(configuration.GetSection(InstrumentsOptions.SectionName));
    }

    public static IServiceCollection SetupTradingSystem(this IServiceCollection services)
    {
        services.AddSingleton<IObservable, Observable>();
        services.AddSingleton<IDBHandler, DBHandler>();
        services.AddSingleton<IBook, Book>();
        services.AddSingleton<IRiskCalculator, RiskCalculator>();
        services.AddSingleton<IExecutionHandler, ExecutionHandler>();
        services.AddSingleton<INordea, NordeaAPI>();
        services.AddSingleton<IJPMorgan, JPMorganAPI>();
        services.AddSingleton<INASDAQ, NASDAQAPI>();
        services.AddSingleton<IClient, ClientAPI>();
        services.AddSingleton<IMarketDataGateway, MarketDataGateway>();
        services.AddSingleton<IPricerEngine, PricerEngine>();
        services.AddSingleton<IHedgeService, HedgeService>();
        services.AddHostedService<Logic.Logic>();
        return services;

    }
}