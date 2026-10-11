using Microsoft.Extensions.DependencyInjection;
using OrderTracker.Domain.Common;

namespace OrderTracker.Application.Abstractions;

/// <summary>Sends each domain event to every registered <see cref="IDomainEventHandler{TEvent}"/>, in process.</summary>
internal sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            foreach (var handler in services.GetServices(handlerType))
            {
                var task = (Task)handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!
                    .Invoke(handler, [domainEvent, cancellationToken])!;
                await task;
            }
        }
    }
}
