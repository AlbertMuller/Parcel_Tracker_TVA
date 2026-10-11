namespace OrderTracker.Domain.Orders;

/// <summary>
/// Loads and stores whole <see cref="Order"/> aggregates. Changes are saved by the unit of work, so a
/// repository never commits on its own.
/// </summary>
public interface IOrderRepository
{
    /// <summary>The order, ready to be changed, or null.</summary>
    Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default);

    /// <summary>The order with this reference (compared after normalizing), or null.</summary>
    Task<Order?> GetByReferenceAsync(ExternalReference reference, CancellationToken cancellationToken = default);

    void Add(Order order);
}
