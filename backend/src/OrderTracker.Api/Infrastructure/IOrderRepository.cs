using OrderTracker.Api.Domain;

namespace OrderTracker.Api.Infrastructure;

public sealed record OrderQuery(OrderStatus? Status = null, string? Search = null, int Page = 1, int PageSize = 50);

public sealed record OrderPage(IReadOnlyList<Order> Items, int TotalCount);

public interface IOrderRepository
{
    /// <summary>
    /// Atomically inserts <paramref name="candidate"/> unless an order with the same (normalized)
    /// external reference already exists. Returns the stored order and whether it was newly added.
    /// This is the single place duplicate prevention is enforced, so it holds under concurrent requests.
    /// </summary>
    (Order Order, bool Added) AddIfReferenceIsNew(Order candidate);

    Order? GetById(Guid id);

    /// <summary>Orders newest first (by creation time, then by order number).</summary>
    OrderPage List(OrderQuery query);

    /// <summary>
    /// Replaces the stored order only if it is still at <paramref name="expectedVersion"/>
    /// (optimistic concurrency). Returns false if someone else changed it first.
    /// </summary>
    bool TryUpdate(Order updated, int expectedVersion);
}
