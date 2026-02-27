
using System.Collections.Generic;
using System.Threading;
using Juice.EventBus;
using Juice.Messaging;
using Juice.Services;
using Juice.Timers.Api;
using Juice.Timers.Api.IntegrationEvents.Events;
using Juice.Timers.Domain.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace Juice.Timers.Tests
{
    [TestCaseOrderer("Juice.XUnit.PriorityOrderer", "Juice.XUnit")]
    [InitializeMessageContext]
    public class CommandTests
    {
        private ITestOutputHelper _output;

        public CommandTests(ITestOutputHelper output)
        {
            _output = output;
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        }

        private IServiceProvider CreateServiceProvider(string provider, Action<IServiceCollection, IConfiguration>? configure = default)
        {
            var resolver = DependencyResolver.Create((services, configuration) =>
            {
                services.AddLogging(builder =>
                {
                    builder.ClearProviders()
                    .AddTestOutputLogger(_output)
                    .AddConfiguration(configuration.GetSection("Logging"));
                });

                services.AddTimerService(configuration.GetSection("Timer"));

                services.AddTimerDbContext(configuration, options =>
                {
                    options.Schema = "App";
                    options.DatabaseProvider = provider;
                }).AddEFTimerRepo();

                services.AddMediatR(options =>
                {
                    options.RegisterServicesFromAssemblyContaining<TimerExpiredIntegrationEventHandler>();
                    options.RegisterServicesFromAssemblyContaining<TimerExpiredDomainEvent>();
                    options.AddOperationLoggingBehavior();
                    options.AddIdempotencyRequestBehavior(idempotency =>
                    {
                        idempotency.Messaging.AddIdempotencyRedis(options =>
                        {
                            options.ConnectionString = configuration.GetConnectionString("Redis");
                        });
                    });
                    options.AddTimerTransactionBehaviors();
                });

                services.AddEventBus()
                    .AddRabbitMQ(cfg =>
                    {
                        cfg.Messaging.AddOutbox();
                        cfg.Messaging.AddPublishingPolicies(configuration.GetSection("EventBus:PublishingPolicies"));
                        cfg.Messaging.AddDelivery(delivery =>
                        {
                            delivery.AddDeliveryProcessor<TimerDbContext>("rabbitmq");
                        });
                        cfg.AddConnection(name: "rabbitmq", configuration.GetSection("EventBus:Connections:RabbitMQ"))
                            .AddProducer("rabbitmq", "rabbitmq", cfg =>
                            {
                                cfg.PoolCapacity(3);
                            })
                            ;
                    });

                services.AddSingleton<SharedToken>();

                configure?.Invoke(services, configuration);
            }, default);
            return resolver.ServiceProvider;
        }
        #region Prerequisites
        [IgnoreOnCITheory(DisplayName = "Migrate Timer DB"), TestPriority(999)]
        [InlineData("SqlServer")]
        [InlineData("PostgreSQL")]
        public async Task Should_migrate_Async(string provider)
        {
            var resolver = new DependencyResolver
            {
                CurrentDirectory = AppContext.BaseDirectory
            };

            resolver.ConfigureServices(services =>
            {
                var configService = services.BuildServiceProvider().GetRequiredService<IConfigurationService>();
                var configuration = configService.GetConfiguration();

                services.AddSingleton(provider => _output);

                services.AddLogging(builder =>
                {
                    builder.ClearProviders()
                    .AddTestOutputLogger()
                    .AddConfiguration(configuration.GetSection("Logging"));
                });

                services.AddTimerDbContext(configuration, options =>
                {
                    options.Schema = "App";
                    options.DatabaseProvider = provider;
                });

            });

            using var scope = resolver.ServiceProvider.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<TimerDbContext>();
            await dbContext.MigrateAsync();
        }

        [IgnoreOnCIFact(DisplayName = "Init RabbitMQ"), TestPriority(998)]
        public async Task Should_init_rabbitmq_Async()
        {
            var resolver = DependencyResolver.Create((services, configuration) =>
            {
                services.AddSingleton(_output);
                services.AddLogging(builder =>
                {
                    builder.ClearProviders()
                    .AddTestOutputLogger()
                    .AddConfiguration(configuration.GetSection("Logging"));
                });

                services.AddEventBus()
                    .AddRabbitMQ(cfg =>
                    {
                        cfg.AddConnection(name: "rabbitmq", configuration.GetSection("EventBus:Connections:RabbitMQ"))
                            .AddInfrastructureTopology("rabbitmq", icfg =>
                            {
                                icfg.DeclareExchange("x.timer.integration", ExchangeType.Topic)
                                    .DeclareQueue("x_timer_queue")
                                    .BindQueue("x_timer_queue", "x.timer.integration", "timer.start.#")
                                    .BindQueue("x_timer_queue", "x.timer.integration", "timer.expired.#")
                                    .DeclareQueue("testhost_timer_queue")
                                    .BindQueue("testhost_timer_queue", "x.timer.integration", "timer.start.#")
                                    ;
                            })
                            ;
                    });
            }, default);
            var serviceProvider = resolver.ServiceProvider;
            await serviceProvider.InitRabbitMQInfrastructureAsync();
        }
        #endregion
        [IgnoreOnCITheory(DisplayName = "Create TimerRequest"), TestPriority(900)]
        [InlineData("SqlServer")]
        [InlineData("PostgreSQL")]
        public async Task Should_create_Async(string provider)
        {
            using var scope = CreateServiceProvider(provider).CreateScope();

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var id = StringIdGenerator.Instance.GenerateRandomId(6);

            var request = await mediator.Send(new CreateTimerCommand("xunit", id, DateTimeOffset.Now.AddSeconds(2)));

            request.Should().NotBeNull();
            request.Id.Should().NotBeEmpty();
            request.CorrelationId.Should().Be(id);

        }

        [IgnoreOnCITheory(DisplayName = "Create 100 TimerRequests"), TestPriority(900)]
        [InlineData("SqlServer")]
        public async Task Should_create_100_Async(string provider)
        {
            using var scope = CreateServiceProvider(provider).CreateScope();

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var ids = new HashSet<string>();
            for (var i = 0; i < 100; i++)
            {
                var id = StringIdGenerator.Instance.GenerateRandomId(6);
                var request = await mediator.Send(new CreateTimerCommand("xunit", id, DateTimeOffset.Now.AddSeconds(2)));
                if (request != null)
                {
                    ids.Add(id);
                }
            }

            ids.Count.Should().Be(100);
        }

        [IgnoreOnCIFact(DisplayName = "Complete via EventBus"), TestPriority(900)]
        public async Task Should_complete_via_eventbus_Async()
        {
            var hostBuilder = WebApplication.CreateBuilder();

            var services = hostBuilder.Services;
            var configuration = hostBuilder.Configuration;
            configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.Development.json", optional: true, reloadOnChange: true)
                .AddUserSecrets(GetType().Assembly)
                .AddEnvironmentVariables();

            // Register DbContext class

            services.AddDefaultStringIdGenerator();

            services.AddLogging(builder =>
            {
                builder.ClearProviders()
                .AddTestOutputLogger(_output)
                .AddConfiguration(configuration.GetSection("Logging"));
            });

            services.AddSingleton<SharedToken>();

            services.AddMessaging()
                .AddIdempotencyRedis(redis => redis.ConnectionString = configuration.GetConnectionString("Redis"))
                .AddPublishingPolicies(configuration.GetSection("EventBus:PublishingPolicies"))
                .AddEventBus()
                    .AddPublishingServices()
                    .AddRabbitMQ(cfg =>
                    {
                        cfg.AddConnection(name: "rabbitmq", configuration.GetSection("EventBus:Connections:RabbitMQ"))
                            .AddProducer("rabbitmq", "rabbitmq", cfg =>
                            {
                                cfg.PoolCapacity(3);
                            })
                            .AddConsumer("rabbitmq.x.timer", "x_timer_queue", "rabbitmq", ccfg =>
                            {
                                ccfg.Subscribe<TimerExpiredIntegrationEvent, TimerExpiredIntegrationEventHandler>();
                            });
                        ;
                    });

            var host = hostBuilder.Build();
            host.Urls.Add("http://localhost:5005");

            var eventBus = host.Services.GetRequiredService<IEventBus>();
            await host.StartAsync();

            var expiredTime = DateTimeOffset.Now.AddSeconds(2);

            int numberOfEvent = 10;
            var ids = new HashSet<string>();
            for (var i = 0; i < numberOfEvent; i++)
            {
                var id = StringIdGenerator.Instance.GenerateRandomId(6);
                var request = new TimerStartIntegrationEvent("xunit", id, DateTimeOffset.Now.AddSeconds(2));
                await eventBus.PublishAsync(request);
                ids.Add(id);
            }

            var cts = new CancellationTokenSource(10000);
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(300);
                _output.WriteLine("Waiting for event");
            }

            await host.StopAsync();
        }

        [IgnoreOnCITheory(DisplayName = "Complete 100 TimerRequests"), TestPriority(900)]
        [InlineData("PostgreSQL")]
        public async Task Should_complete_100_Async(string provider)
        {
            using var scope = CreateServiceProvider(provider).CreateScope();

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var ids = new List<string>();
            for (var i = 0; i < 100; i++)
            {
                var id = StringIdGenerator.Instance.GenerateRandomId(6);

                var request = await mediator.Send(new CreateTimerCommand("xunit", id, DateTimeOffset.Now.AddSeconds(2)));
                ids.Add(id);
            }

            await Task.Delay(5000);
            var dbContext = scope.ServiceProvider.GetRequiredService<TimerDbContext>();
            var incompleted = await dbContext.TimerRequests.AnyAsync(t => ids.Contains(t.CorrelationId) && !t.IsCompleted);
            incompleted.Should().BeFalse();
        }

        [IgnoreOnCITheory(DisplayName = "Complete TimerRequest"), TestPriority(800)]
        [InlineData("SqlServer")]
        [InlineData("PostgreSQL")]
        public async Task Should_complete_Async(string provider)
        {
            using var scope = CreateServiceProvider(provider).CreateScope();

            var timerContext = scope.ServiceProvider.GetRequiredService<TimerDbContext>();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var request = await timerContext.TimerRequests.FirstOrDefaultAsync(t => !t.IsCompleted && t.AbsoluteExpired < DateTimeOffset.Now);

            if (request != null)
            {
                var result = await mediator.Send(new CompleteTimerCommand(request.Id));
                result.Succeeded.Should().BeTrue();
            }

        }

        [IgnoreOnCITheory(DisplayName = "Timer work!"), TestPriority(800)]
        //[InlineData("SqlServer")]
        [InlineData("PostgreSQL")]
        public async Task Should_exit_after_timeout_Async(string provider)
        {
            using var scope = CreateServiceProvider(provider, (services, configuration) =>
            {
                services.AddEventBus()
                    .AddPublishingServices()
                    .AddRabbitMQ(cfg =>
                    {
                        cfg.AddConsumer("rabbitmq.x.timer", "timer_expired_queue", "rabbitmq", ccfg =>
                            {
                                ccfg.Subscribe<TimerExpiredIntegrationEvent, TimerExpiredIntegrationEventHandler>("timer.expired.#");
                                ccfg.Subscribe<TimerStartIntegrationEvent, Api.IntegrationEvents.Handlers.TimerStartIntegrationEventHandler>("timer.start.#");
                            });
                        ;

                    });
            }).CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();
            var sharedToken = scope.ServiceProvider.GetRequiredService<SharedToken>();

            var id = StringIdGenerator.Instance.GenerateRandomId(6);

            var expiredTime = DateTimeOffset.Now.AddSeconds(2);
            var request = await mediator.Send(new CreateTimerCommand("xunit", id, expiredTime));

            while (!sharedToken.CTS.IsCancellationRequested)
            {
                _output.WriteLine("Waiting for timer");
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(300), sharedToken.CTS.Token);
                }
                catch { }
            }

            var delayTime = (DateTimeOffset.Now - expiredTime);
            _output.WriteLine($"Delayed: {delayTime}");

            sharedToken.CTS.IsCancellationRequested.Should().BeTrue();
            delayTime.Should().BeLessThan(TimeSpan.FromSeconds(1)); // timer interval option

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

    }

    public class SharedToken
    {
        public CancellationTokenSource CTS { get; } = new CancellationTokenSource(10000);// should timeout after 10s
    }

    public class TimerExpiredIntegrationEventHandler : IIntegrationEventHandler<TimerExpiredIntegrationEvent>
    {
        private SharedToken _sharedToken;
        private ILogger _logger;
        private static int _globalCount = 0;
        public TimerExpiredIntegrationEventHandler(ILogger<TimerExpiredIntegrationEventHandler> logger,
            SharedToken sharedToken)
        {
            _logger = logger;
            _sharedToken = sharedToken;
        }
        public async Task HandleAsync(TimerExpiredIntegrationEvent @event)
        {
            Interlocked.Increment(ref _globalCount);
            _logger.LogInformation("Received {Count} timer event {CorrelationId}. Delayed {Delayed}", _globalCount, @event.CorrelationId, (DateTimeOffset.Now - @event.AbsoluteExpired));
            _sharedToken.CTS.Cancel();
        }
    }

    public class SelfTimerExipredDomainEventHandler : INotificationHandler<TimerExpiredDomainEvent>
    {
        private SharedToken _sharedToken;
        private ILogger _logger;
        public SelfTimerExipredDomainEventHandler(ILogger<SelfTimerExipredDomainEventHandler> logger,
            SharedToken sharedToken)
        {
            _logger = logger;
            _sharedToken = sharedToken;
        }

        public ValueTask Handle(TimerExpiredDomainEvent notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Timeout event {CorrelationId}", notification.Request.CorrelationId);
            _sharedToken.CTS.Cancel();
            return ValueTask.CompletedTask;
        }
    }

    public class TimerStartIntegrationEventHandler : IIntegrationEventHandler<TimerStartIntegrationEvent>
    {
        private ILogger _logger;
        static int _globalCount = 0;
        public TimerStartIntegrationEventHandler(ILogger<TimerStartIntegrationEventHandler> logger)
        {
            _logger = logger;
        }
        public async Task HandleAsync(TimerStartIntegrationEvent @event)
        {
            Interlocked.Increment(ref _globalCount);
            _logger.LogInformation("Received timer start event {CorrelationId} {Count}", @event.CorrelationId, _globalCount);
        }
    }
}
