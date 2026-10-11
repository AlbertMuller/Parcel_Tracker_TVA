using Microsoft.Extensions.Logging;
using OrderTracker.Application.Abstractions;
using OrderTracker.Domain.Common;
using OrderTracker.Domain.Orders;

namespace OrderTracker.Application.Orders.ChangeOrderStatus;

public sealed record ChangeOrderStatusCommand(Guid OrderId, string? Status);

/// <summary>
/// Moves an order through its lifecycle. The aggregate decides whether the move is allowed. If another
/// request changed the order in the meantime, the save is rejected (optimistic concurrency) and the
/// change is re-evaluated against the order's new state.
/// </summary>
public sealed class ChangeOrderStatusHandler(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ChangeOrderStatusHandler> logger) : ICommandHandler<ChangeOrderStatusCommand, Result<OrderDto>>
{
    internal const int MaxAttempts = 5;

    public async Task<Result<OrderDto>> HandleAsync(ChangeOrderStatusCommand command, CancellationToken cancellationToken = default)
    {
        if (!OrderStatusPolicy.TryParse(command.Status, out var target))
            return OrderErrors.UnknownStatus(command.Status);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var order = await orders.GetByIdAsync(new OrderId(command.OrderId), cancellationToken);
            if (order is null)
                return OrderErrors.NotFound(command.OrderId);

            var result = order.ChangeStatus(target, clock.GetUtcNowForStorage());
            if (result.IsFailure)
            {
                logger.LogInformation("Rejected status change of order #{Number} from {From} to {To} ({Code}).", order.Number, order.Status, target, result.Error.Code);
                return result.Error;
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return OrderDto.From(order);
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                logger.LogInformation("Order {OrderId} changed concurrently; retrying status change (attempt {Attempt}).", command.OrderId, attempt);
            }
        }

        throw new InvalidOperationException("Unreachable: the last attempt either returns or throws.");
    }
}
