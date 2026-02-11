using Juice.Timers.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace Juice.Timers
{
    public static class BehaviorServiceCollectionExtensions
    {
        /// <summary>
        /// Try to start timer after created
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static MediatorBuilder AddTimerManagerBehavior(this MediatorBuilder builder)
        {
            builder.Services.AddScoped<IPipelineBehavior<CreateTimerCommand, TimerRequest>, TimerManagerBehavior<CreateTimerCommand, TimerRequest>>();
            return builder;
        }

    }
}
