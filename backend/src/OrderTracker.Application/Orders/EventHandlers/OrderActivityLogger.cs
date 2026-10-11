using Microsoft.Extensions.Logging;
using OrderTracker.Application.Abstractions;
using OrderTracker.Domain.Orders.Events;

namespace OrderTracker.Application.Orders.EventHandlers;

/// <summary>Writes a structured log entry for every committed order change.</summary>
public sealed class OrderActivityLogger(ILogger<OrderActivityLogger> logger) :
    IDomainEventHandler<OrderSubmitted>,
    IDomainEventHandler<OrderStatusChanged>,
    IDomainEventHandler<OrderCurrencyConverted>
{
    public Task HandleAsync(OrderSubmitted e, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Order {OrderId} submitted with reference {Reference}: {Total} {Currency}.", e.OrderId, e.Reference, e.Total, e.Currency);
        return Task.CompletedTask;
    }

    public Task HandleAsync(OrderStatusChanged e, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Order {OrderId} moved from {From} to {To}.", e.OrderId, e.From, e.To);
        return Task.CompletedTask;
    }

    public Task HandleAsync(OrderCurrencyConverted e, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Order {OrderId} re-priced from {From} to {To} at {Rate}.", e.OrderId, e.From, e.To, e.RateFromBase);
        return Task.CompletedTask;
    }
}
