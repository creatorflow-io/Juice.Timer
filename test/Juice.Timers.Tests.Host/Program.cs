using Juice.EF.Extensions;
using Juice.Timers;
using Juice.Timers.Api;
using Juice.Timers.Api.Domain.EventHandlers;
using Juice.Timers.Api.IntegrationEvents.Events;
using Juice.Timers.Api.IntegrationEvents.Handlers;
using Juice.Timers.BackgroundTasks;
using Juice.Timers.Domain.Events;
using Juice.Timers.EF;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

ConfigureTimer(builder.Services, "PostgreSQL", builder.Configuration);
ConfigureIntegrations(builder.Services, "PostgreSQL", builder.Configuration);

var app = builder.Build();

await MigrateDbAsync(app);

app.MapGet("/", async (context) =>
{
    var dbContext = context.RequestServices.GetRequiredService<TimerDbContext>();
    var pendingCount = await dbContext.TimerRequests.CountAsync(t => !t.IsCompleted);
    var expiredCount = await dbContext.TimerRequests.CountAsync(t => !t.IsCompleted && t.AbsoluteExpired < DateTimeOffset.Now);
    var completedCount = await dbContext.TimerRequests.CountAsync(t => t.IsCompleted);
    var totalCount = await dbContext.TimerRequests.CountAsync();
    await context.Response.WriteAsJsonAsync(new { pendingCount, expiredCount, completedCount, totalCount });
});

app.MapGet("/gc", async (context) =>
{
    GC.Collect();
    context.Response.StatusCode = StatusCodes.Status200OK;
});

app.Run();


static void ConfigureTimer(IServiceCollection services, string provider, IConfiguration configuration)
{
    services.AddTimerDbContext(configuration, options =>
    {
        options.Schema = "App";
        options.DatabaseProvider = provider;
    }).AddEFTimerRepo();
    services.AddTimerService(configuration.GetSection("Timer"));
    services.AddTimerBackgroundTasks(configuration.GetSection("Timer"));

    services.AddMediatR(options =>
    {
        options.RegisterServicesFromAssemblyContaining<TimerExpiredDomainEventHandler>();
        options.RegisterServicesFromAssemblyContaining<TimerExpiredDomainEvent>();
        options.AddOperationLoggingBehavior();
        options.AddIdempotencyRequestBehavior(idempotent => {
            idempotent.Messaging.AddIdempotencyRedis(redis =>
            {
                redis.ConnectionString = configuration.GetConnectionString("Redis");
            });
        });
        options.AddTimerTransactionBehaviors();
    });

}

static void ConfigureIntegrations(IServiceCollection services, string provider, IConfiguration configuration)
{
    services.AddMessaging()
        .AddOutbox()
        .AddPublishingPolicies(configuration.GetSection("EventBus:PublishingPolicies"))
        .AddDelivery(delivery =>
        {
            delivery.AddDeliveryPolicies(configuration.GetSection("EventBus:DeliveryPolicies"));
            delivery.AddDeliveryProcessor<TimerDbContext>("rabbitmq");
        })
        .AddEventBus()
        .AddRabbitMQ(cfg =>
        {
            cfg.AddConnection("rabbitmq", configuration.GetSection("EventBus:Connections:RabbitMQ"));
            cfg.AddProducer("rabbitmq", "rabbitmq");
            cfg.AddConsumer("rabbitmq.x.timerhost", "testhost_timer_queue", "rabbitmq", ccfg =>
            {
                ccfg.Subscribe<TimerStartIntegrationEvent, TimerStartIntegrationEventHandler>("timer.start.#");
            });
        });

}

static async Task MigrateDbAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<TimerDbContext>();
    await dbContext.MigrateAsync();
}
