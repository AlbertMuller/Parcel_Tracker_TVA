namespace OrderTracker.Domain.Orders;

public enum OrderStatus
{
    Pending,
    Confirmed,
    Fulfilled,
    Cancelled,
}

/// <summary>
/// The order lifecycle as a small state machine:
///
///   Pending ──► Confirmed ──► Fulfilled
///      │            │
///      └────────────┴──► Cancelled
///
/// Fulfilled and Cancelled are final.
/// </summary>
public static class OrderStatusPolicy
{
    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> Transitions =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
            [OrderStatus.Confirmed] = [OrderStatus.Fulfilled, OrderStatus.Cancelled],
            [OrderStatus.Fulfilled] = [],
            [OrderStatus.Cancelled] = [],
        };

    public static IReadOnlyList<OrderStatus> AllowedTransitionsFrom(OrderStatus current) => Transitions[current];

    public static bool CanTransition(OrderStatus from, OrderStatus to) => Transitions[from].Contains(to);

    public static bool IsFinal(OrderStatus status) => Transitions[status].Length == 0;

    /// <summary>A user-facing explanation of why <paramref name="from"/> → <paramref name="to"/> is not allowed.</summary>
    public static string ExplainRejection(OrderStatus from, OrderStatus to)
    {
        var reason = (from, to) switch
        {
            (OrderStatus.Fulfilled, OrderStatus.Cancelled) =>
                "This order has already been fulfilled, so it can no longer be cancelled.",
            (OrderStatus.Fulfilled, _) =>
                "This order has already been fulfilled. Fulfilled is a final status.",
            (OrderStatus.Cancelled, _) =>
                "This order has been cancelled. Cancelled is a final status; submit a new order with a new reference instead.",
            (OrderStatus.Pending, OrderStatus.Fulfilled) =>
                "This order must be confirmed before it can be fulfilled.",
            (_, OrderStatus.Pending) =>
                $"An order cannot be moved back to Pending once it is {from}.",
            _ => $"An order cannot move from {from} to {to}.",
        };

        var allowed = AllowedTransitionsFrom(from);
        return allowed.Count == 0
            ? reason
            : $"{reason} Allowed next statuses: {string.Join(", ", allowed)}.";
    }

    /// <summary>
    /// Parses a status name, case-insensitively. Stricter than Enum.TryParse, which would also
    /// accept "1", "99" or "Confirmed,Cancelled".
    /// </summary>
    public static bool TryParse(string? value, out OrderStatus status)
    {
        var trimmed = value?.Trim();
        foreach (var candidate in Enum.GetValues<OrderStatus>())
        {
            if (string.Equals(candidate.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;
                return true;
            }
        }

        status = default;
        return false;
    }
}
