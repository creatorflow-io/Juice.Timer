
namespace Juice.Timers.Domain.Commands
{
    public class CreateTimerCommandHandler
        : IRequestHandler<CreateTimerCommand, TimerRequest?>
    {
        private ITimerRepository _repository;

        public CreateTimerCommandHandler(ITimerRepository repository)
        {
            _repository = repository;
        }

        public async ValueTask<TimerRequest?> Handle(CreateTimerCommand request, CancellationToken cancellationToken)
        {
            var timerRequest = new TimerRequest(request.Issuer, request.CorrelationId, request.AbsoluteExpired);
            await _repository.CreateAsync(timerRequest, cancellationToken);
            return timerRequest;
        }
    }

}
