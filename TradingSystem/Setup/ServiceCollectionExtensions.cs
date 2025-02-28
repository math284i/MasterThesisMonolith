using TradingSystem.Data;
using TradingSystem.Logic;
using TradingSystem.Logic.ExternalBrokers;

namespace TradingSystem.Setup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection SetupLogging(this IServiceCollection services)
    {
        return services.AddScoped<JsLogger>();
    }

    public static IServiceCollection SetupExternalData(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BrokerStocks>(configuration.GetSection(BrokerStocks.SectionName));
        return services.Configure<TradingOptions>(configuration.GetSection(TradingOptions.SectionName));
    }

    public static IServiceCollection SetupTradingSystem(this IServiceCollection services)
    {
        services.AddSingleton<IMessageBus, MessageBus>();
        services.AddSingleton<INordea, NordeaAPI>();
        services.AddSingleton<IJPMorgan, JPMorganAPI>();
        services.AddSingleton<INASDAQ, NASDAQAPI>();
        services.AddSingleton<IClient, ClientAPI>();
        services.AddSingleton<IBrokerInteractor, BrokerInteractor>();
        services.AddSingleton<IPricerEngine, PricerEngine>();
        return services;
    }
}