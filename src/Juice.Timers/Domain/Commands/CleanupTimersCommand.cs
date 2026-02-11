namespace Juice.Timers.Domain.Commands
{
    public record CleanupTimersCommand : MessageBase, IRequest<IOperationResult>
    {
        public DateTimeOffset Before { get; private set; }
        public CleanupTimersCommand(DateTimeOffset beforeTime)
        {
            Before = beforeTime;
        }
    }
}
