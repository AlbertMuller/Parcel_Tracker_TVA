using OrderTracker.Domain.Common;

namespace OrderTracker.Domain.Orders;

/// <summary>The order-related business errors, each with a stable code.</summary>
public static class OrderErrors
{
    public static Error NotFound(Guid id) =>
        new("order.not_found", "Order not found", $"No order exists with id {id}.", ErrorType.NotFound);

    public static Error ReferenceConflict(Order existing) =>
        new("order.reference.conflict",
            "External reference already used",
            $"Reference '{existing.Reference.Value}' was already used for order #{existing.Number} with different details. " +
            "No new order was created. Use a new reference for a different order.",
            ErrorType.Conflict)
        {
            Metadata = new Dictionary<string, object?>
            {
                ["existingOrderId"] = existing.Id.Value,
                ["existingOrderNumber"] = existing.Number,
            },
        };

    public static Error TransitionNotAllowed(OrderStatus from, OrderStatus to) =>
        new(OrderStatusPolicy.IsFinal(from) ? "order.status.final" : "order.status.invalid_transition",
            "Status change not allowed",
            OrderStatusPolicy.ExplainRejection(from, to),
            ErrorType.Conflict)
        {
            Metadata = new Dictionary<string, object?>
            {
                ["currentStatus"] = from.ToString(),
                ["requestedStatus"] = to.ToString(),
                ["allowedTransitions"] = OrderStatusPolicy.AllowedTransitionsFrom(from).Select(s => s.ToString()).ToArray(),
            },
        };

    public static Error CurrencyLocked(OrderStatus status) =>
        new("order.currency.locked",
            "Currency change not allowed",
            $"The currency can only be changed while an order is Pending. This order is {status}, so its prices are locked.",
            ErrorType.Conflict);

    public static ValidationError UnknownStatus(string? value) =>
        ValidationError.For("status", $"'{value}' is not a valid status. Valid statuses: {string.Join(", ", Enum.GetNames<OrderStatus>())}.");
}
