using NATS.Client.JetStream;
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
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.ServiceStartingUp();
        
        await SetupJetStream();
        
        _pricerEngine.Start();
        _dbHandler.Start();
        _book.Start();
        _marketDataGateway.Start();
        _hedgeService.Start();
        _executionHandler.Start();
        _riskCalculator.Start();
        
        _logger.ServiceStarted();
        
        //PlayWithNats();
        //NatsConsumerExample();
        
        //return Task.CompletedTask;
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
    
    private async Task SetupJetStream()
    {
        var url = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://127.0.0.1:4222";


        await using var nc = new NatsClient(url);
        var clientPricesStream = nc.CreateJetStreamContext();
        var streamConfig = new StreamConfig
        {
            Name = "StreamClientPrices",
            Subjects = ["clientPrices.*"],
            Description = "This is where client prices will be streamed",
            AllowDirect = true,
            MaxConsumers = -1,
            MaxMsgsPerSubject = 1
        };

        var orderConfig = new StreamConfig
        {
            Name = "streamOrders",
            Subjects = ["clientOrders.*"],
            Description = "This is where client orders will be streamed",
            AllowDirect = true,
        };
        
        var marketConfig = new StreamConfig
        {
            Name = "StreamMarketPrices",
            Subjects = ["marketPrices.*"],
            Description = "This is where market prices will be streamed",
            AllowDirect = true,
            MaxConsumers = -1,
            MaxMsgsPerSubject = 1
        };
        
        // TODO Stream for DBData
        var dbConfig = new StreamConfig
        {
            Name = "StreamDBData",
            Subjects = ["StreamDBData.*"],
            Description = "Here you will find most of our DB data that is used throughout the system, be careful who has access to this",
            AllowDirect = true,
            MaxConsumers = -1,
            MaxMsgsPerSubject = 1
        };
        
        // TODO Stream for AllData
        // streamMISC
        var miscConfig = new StreamConfig
        {
            Name = "StreamMisc",
            Subjects = ["StreamMisc.*"],
            Description = "This is a stream for random persistent data that is needed by multiple services",
            AllowDirect = true,
            MaxConsumers = -1,
            MaxMsgsPerSubject = 1
        };
        
        var loginConfig = new StreamConfig
        {
            Name = "streamLoginRequest",
            Subjects = ["loginRequest.*"],
            Description = "This is where login requests will be streamed",
            AllowDirect = true,
        };
        
        // Create stream
        await clientPricesStream.CreateOrUpdateStreamAsync(streamConfig);
        await clientPricesStream.CreateOrUpdateStreamAsync(orderConfig);
        await clientPricesStream.CreateOrUpdateStreamAsync(marketConfig);
        await clientPricesStream.CreateOrUpdateStreamAsync(miscConfig);
        await clientPricesStream.CreateOrUpdateStreamAsync(dbConfig);
        await clientPricesStream.CreateOrUpdateStreamAsync(loginConfig);
    }

    private async void NatsConsumerExample()
    {

        var streamName = "EVENTS";
        var url = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://127.0.0.1:4222";


        await using var nc = new NatsClient(url); // Will fail if server isn't there. Figure out a smart way to deal with it. 
        var js = nc.CreateJetStreamContext();

        var stream = await js.CreateOrUpdateStreamAsync(new StreamConfig(streamName, subjects: ["events.>"]));

        await js.PublishAsync(subject: "events.1", data: "event-data-1");
        await js.PublishAsync(subject: "events.2", data: "event-data-2");
        await js.PublishAsync(subject: "events.3", data: "event-data-3");

        var durable = await stream.CreateOrUpdateConsumerAsync(new ConsumerConfig("processor"));


        await foreach (var msg in durable.FetchAsync<string>(opts: new NatsJSFetchOpts { MaxMsgs = 1 }))
        {
            Console.WriteLine($"Received {msg.Subject} from durable consumer");
        }

        await js.PublishAsync(subject: "events.1", data: "event-data-10");
        
        await stream.DeleteConsumerAsync("processor");
        
        try
        {
            await stream.GetConsumerAsync("processor");
        }
        catch (NatsJSApiException e)
        {
            if (e.Error.Code == 404)
            {
                Console.WriteLine("Consumer is gone");
            }
        }


        Console.WriteLine("Bye!");
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
        var subscriptionTask = Task.Run(async () =>
        {
            await foreach (var msg in nc.SubscribeAsync<Order>("orders.>", cancellationToken: cts.Token))
            {
                var order = msg.Data;
                Console.WriteLine($"Subscriber received {msg.Subject}: {order}");
            }
        
        
            Console.WriteLine("Unsubscribed");
        });


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