using TradingSystem.Data;
using TradingSystem.Logic;

namespace TradingSystem.Setup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection SetupLogging(this IServiceCollection services)
    {
        return services.AddScoped<JsLogger>();
    }

    public static IServiceCollection SetupExternalData(this IServiceCollection services, IConfiguration configuration)
    {
        return services.Configure<TradingOptions>(configuration.GetSection(TradingOptions.SectionName));
    }

    public static IServiceCollection SetupTradingSystem(this IServiceCollection services)
    {
        services.AddSingleton<IMessageBus, MessageBus>();
        services.AddSingleton<IClient, ClientAPI>();
        services.AddSingleton<IBrokerInteractor, BrokerInteractor>();
        services.AddSingleton<IPricerEngine, PricerEngine>();
        return services;
    }
}