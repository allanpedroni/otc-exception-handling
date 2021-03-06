using System;
using System.Collections.Generic;
using System.Linq;
using Otc.ExceptionHandling.Abstractions;

namespace Otc.ExceptionHandling
{
    public class ExceptionHandlerConfiguration : IExceptionHandlerConfiguration
    {
        public ExceptionHandlerConfiguration(Action<IExceptionHandlerConfigurationExpression> action)
        {
            this.Events = new List<IExceptionHandlerEvent>();
            behaviors = new Dictionary<Type, ForExceptionBehavior>();
            Build(action);
        }

        private Dictionary<Type, ForExceptionBehavior> behaviors;

        public List<IExceptionHandlerEvent> Events { get; }

        public bool HasBehaviors => behaviors.Any();

        public ForExceptionBehavior ValidateBehavior(Exception ex)
        {
            foreach (var behavior in behaviors)
            {
                if (behavior.Key.IsAssignableFrom(ex.GetType()))
                    return behavior.Value;
            }

            return null;
        }

        private void Build(Action<IExceptionHandlerConfigurationExpression> action)
        {
            var configurationExpression = new ExceptionHandlerConfigurationExpression();

            action(configurationExpression);

            this.Events.AddRange(configurationExpression.Events);

            this.behaviors = new Dictionary<Type, ForExceptionBehavior>(
                configurationExpression.Behaviors);
        }
    }
}
