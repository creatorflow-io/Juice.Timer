namespace Juice.Timers.Domain.Events
{
    public record TimerExpiredDomainEvent : MessageBase, INotification
    {
        public TimerRequest Request { get; private set; }
        public TimerExpiredDomainEvent(TimerRequest request)
        {
            Request = request;
        }
    }
}
