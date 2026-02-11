namespace Juice.Timers.Domain.Commands
{
    public record CreateTimerCommand(string Issuer, string CorrelationId, DateTimeOffset AbsoluteExpired)
        : MessageBase, IRequest<TimerRequest>, ITimerCommand, IIdempotentRequest
    {
        public string IdempotencyKey => $"timer.create.{CorrelationId}";
    }
}
