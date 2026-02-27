using Juice.Messaging;
using Juice.Timers.Api.IntegrationEvents.Events;

namespace Juice.Timers.Api.IntegrationEvents.Handlers
{
    public class TimerStartIntegrationEventHandler : IIntegrationEventHandler<TimerStartIntegrationEvent>
    {
        private IMediator _mediator;
        private ILogger _logger;
        public TimerStartIntegrationEventHandler(ILogger<TimerStartIntegrationEventHandler> logger,
            IMediator mediator)
        {
            _mediator = mediator;
            _logger = logger;
        }

        public async Task HandleAsync(TimerStartIntegrationEvent @event)
        {
            var command = new CreateTimerCommand(@event.Issuer, @event.CorrelationId, @event.AbsoluteExpired);
            var rs = await _mediator.Send(command);
            if (rs == null)
            {
                _logger.LogWarning("Cannot create timer");
            }
            else
            {
                _logger.LogInformation("Create timer succeeded. Timer Id: " + rs.Id);
            }
        }
    }
}
