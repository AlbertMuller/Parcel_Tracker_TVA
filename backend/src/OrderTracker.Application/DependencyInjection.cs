using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderTracker.Application.Abstractions;
using OrderTracker.Application.ExchangeRates;

namespace OrderTracker.Application;

public static class DependencyInjection
{
    /// <summary>Registers every use-case handler, domain-event handler and the exchange-rate cache.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ExchangeRateService>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        var types = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false });

        foreach (var type in types)
        {
            var interfaces = type.GetInterfaces().Where(i => i.IsGenericType).Select(i => i.GetGenericTypeDefinition()).ToHashSet();

            // Use-case handlers are injected by their concrete type (one per use case).
            if (interfaces.Contains(typeof(ICommandHandler<,>)) || interfaces.Contains(typeof(IQueryHandler<,>)))
                services.AddScoped(type);

            // Domain-event handlers are resolved by event type, so one class can handle several events.
            foreach (var eventHandler in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>)))
                services.AddScoped(eventHandler, type);
        }

        return services;
    }
}
