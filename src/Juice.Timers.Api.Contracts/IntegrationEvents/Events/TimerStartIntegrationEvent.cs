using Juice.Messaging;

namespace Juice.Timers.Api.IntegrationEvents.Events
{
    public record TimerStartIntegrationEvent(string Issuer, string CorrelationId, DateTimeOffset AbsoluteExpired) : IntegrationEvent
    {
        public override string EventName => "timer.start." + Issuer.ToLower();
    }
}
