using Juice.EventBus;

namespace Juice.Timers.Api.IntegrationEvents.Events
{
    public record TimerExpiredIntegrationEvent(string Issuer, string CorrelationId, DateTimeOffset AbsoluteExpired) : IntegrationEvent
    {
        public override string EventName => "timer.expired." + Issuer.ToLower();
    }
}
