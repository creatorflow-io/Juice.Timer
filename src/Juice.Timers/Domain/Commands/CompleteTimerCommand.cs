namespace Juice.Timers.Domain.Commands
{
    /// <summary>
    /// Use IdentifiedCommand<CompleteTimerCommand, IOperationResult> instead CompleteTimerCommand directly
    /// </summary>
    public record CompleteTimerCommand(Guid TimerRequestId)
        : MessageBase, IRequest<IOperationResult>, IIdempotentRequest, ITimerCommand
    {
        public string IdempotencyKey => $"timer.complete.{TimerRequestId}";
    }
}
