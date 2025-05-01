using NATS.Client.JetStream.Models;
using NATS.Net;
using TradingSystem.Data;
using TradingSystem.Logic.LoggerExtensions;

namespace TradingSystem.Logic;

public class Logic : IHostedService
{
    private readonly ILogger<Logic> _logger;
    private readonly IPricerEngine _pricerEngine;
    private readonly IMarketDataGateway _marketDataGateway;
    private readonly IHedgeService _hedgeService;
    private readonly IRiskCalculator _riskCalculator;
    private readonly IExecutionHandler _executionHandler;
    private readonly IDBHandler _dbHandler;
    private readonly IBook _book;

    public Logic(ILogger<Logic> logger
        , IPricerEngine pricerEngine
        , IMarketDataGateway marketDataGateway
        , IHedgeService hedgeService
        , IRiskCalculator riskCalculator
        , IExecutionHandler executionHandler
        , IDBHandler dbHandler
        , IBook book)
    {
        _logger = logger;
        _pricerEngine = pricerEngine;
        _marketDataGateway = marketDataGateway;
        _hedgeService = hedgeService;
        _riskCalculator = riskCalculator;
        _executionHandler = executionHandler;
        _dbHandler = dbHandler;
        _book = book;
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.ServiceStartingUp();
        
        _dbHandler.Start();
        _book.Start();
        _pricerEngine.Start();
        _marketDataGateway.Start();
        _hedgeService.Start();
        _executionHandler.Start();
        _riskCalculator.Start();
        
        _logger.ServiceStarted();
        
        //PlayWithNats();
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.ServiceStopped();
        
        _dbHandler.Stop();
        _book.Stop();
        _pricerEngine.Stop();
        _marketDataGateway.Stop();
        _hedgeService.Stop();
        _riskCalculator.Stop();
        _executionHandler.Stop();
        
        _logger.ServiceStopped();
        
        return Task.CompletedTask;
    }

    private async void PlayWithNats()
    {
        _logger.LogInformation("Starting to play with nats");
        
        var url = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://127.0.0.1:4222";


        await using var nc = new NatsClient(url); // Will fail if server isn't there. Figure out a smart way to deal with it. 
        var jetStream = nc.CreateJetStreamContext(); // To keep messages on 
        var streamConfig = new StreamConfig
        {
          Name = "StreamPrices",
          Subjects = ["prices.*"],
          Description = "This is where client prices will be streamed",
          AllowDirect = true,
          MaxConsumers = -1,
          MaxMsgsPerSubject = 1
        };

        // Create stream
        var tmp = await jetStream.CreateStreamAsync(streamConfig);
        var subject = "prices.GME";
        var stock = new Stock
        {
            InstrumentId = "GME",
            Price = 10.99M,
        };

        await jetStream.PublishAsync(subject, stock);
        Console.WriteLine("Waiting for messages...");
        var cts = new CancellationTokenSource();
        // var subscriptionTask = Task.Run(async () =>
        // {
        //     await foreach (var msg in nc.SubscribeAsync<Order>("orders.>", cancellationToken: cts.Token))
        //     {
        //         var order = msg.Data;
        //         Console.WriteLine($"Subscriber received {msg.Subject}: {order}");
        //     }
        //
        //
        //     Console.WriteLine("Unsubscribed");
        // });


        await Task.Delay(1000);
        Console.WriteLine($"ping {await nc.PingAsync()}");

        for (int i = 0; i < 5; i++)
        {
            Console.WriteLine($"Publishing order {i}...");
            await nc.PublishAsync($"orders.new.{i}", new Order(OrderId: i));
            await Task.Delay(500);
        }

        Console.WriteLine("Canceling token");
        await cts.CancelAsync();
        //await subscriptionTask;


        Console.WriteLine("Bye!");
        
        
        
    }
    public record Order(int OrderId);
}