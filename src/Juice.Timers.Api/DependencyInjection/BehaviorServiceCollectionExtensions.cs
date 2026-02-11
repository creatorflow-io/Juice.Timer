using Juice.Timers.Api.Behaviors;
using Juice.Timers.Domain.AggregratesModel.TimerAggregrate;
using Microsoft.Extensions.DependencyInjection;

namespace Juice.Timers.Api
{
    public static class BehaviorServiceCollectionExtensions
    {
        /// <summary>
        /// Included AddTimerManagerBehavior and TimerTransactionBehavior 
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static MediatorBuilder AddTimerTransactionBehaviors(this MediatorBuilder builder)
        {
            builder.AddTimerManagerBehavior();
            builder.Services.AddScoped(typeof(IPipelineBehavior<CreateTimerCommand, TimerRequest>), typeof(TimerTransactionBehavior<CreateTimerCommand, TimerRequest>));
            builder.Services.AddScoped(typeof(IPipelineBehavior<CompleteTimerCommand, IOperationResult>), typeof(TimerTransactionBehavior<CompleteTimerCommand, IOperationResult>));
            return builder;
        }

    }
}
