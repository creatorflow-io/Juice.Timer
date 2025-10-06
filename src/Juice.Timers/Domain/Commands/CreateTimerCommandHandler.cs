
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

    // Use for Idempotency in Command process
    public class CreateTimerIdentifiedCommandHandler
        : IdentifiedCommandHandler<CreateTimerCommand, TimerRequest?>
    {

        public CreateTimerIdentifiedCommandHandler(
            IMediator mediator,
            IRequestManager requestManager, ILogger<CreateTimerIdentifiedCommandHandler> logger)
            : base(mediator, requestManager, logger)
        {
        }

        protected override ValueTask<TimerRequest?> CreateResultForDuplicatedRequestAsync(CreateTimerCommand message)
        {
            return ValueTask.FromResult(default(TimerRequest?));
        }

        protected override (string IdProperty, string CommandId) ExtractDebugInfo(CreateTimerCommand command)
            => (nameof(command.CorrelationId), command.CorrelationId);
    }
}
