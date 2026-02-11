using Juice.MediatR.Behaviors;
using Juice.Messaging.Outbox;
using Juice.Timers.EF;

namespace Juice.Timers.Api.Behaviors
{
    internal class TimerTransactionBehavior<T, R> : TransactionBehavior<T, R, TimerDbContext>
        where T : IRequest<R>
    {
        public TimerTransactionBehavior(TimerDbContext dbContext,
            IOutboxService<TimerDbContext> outboxService,
            IMediator mediator,
            ILogger<TimerTransactionBehavior<T, R>> logger) : base(dbContext, outboxService, mediator, logger)
        {
        }
    }
}
