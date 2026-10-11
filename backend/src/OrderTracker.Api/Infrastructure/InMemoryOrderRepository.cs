using OrderTracker.Api.Domain;

namespace OrderTracker.Api.Infrastructure;

/// <summary>
/// Thread-safe in-memory store. A single lock keeps the "check reference, then insert" step atomic,
/// playing the role a unique index would in a database. Orders are immutable records, so snapshots
/// handed out to callers can't be mutated behind the lock's back.
/// </summary>
public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Order> _byId = new();
    private readonly Dictionary<string, Guid> _idByReference = new(StringComparer.Ordinal);
    private long _lastNumber;

    public (Order Order, bool Added) AddIfReferenceIsNew(Order candidate)
    {
        var key = ExternalReferences.Normalize(candidate.ExternalReference);
        lock (_gate)
        {
            if (_idByReference.TryGetValue(key, out var existingId))
                return (_byId[existingId], false);

            var stored = candidate with { Number = ++_lastNumber };
            _byId.Add(stored.Id, stored);
            _idByReference.Add(key, stored.Id);
            return (stored, true);
        }
    }

    public Order? GetById(Guid id)
    {
        lock (_gate)
            return _byId.GetValueOrDefault(id);
    }

    public OrderPage List(OrderQuery query)
    {
        Order[] snapshot;
        lock (_gate)
            snapshot = _byId.Values.ToArray();

        IEnumerable<Order> filtered = snapshot;
        if (query.Status is { } status)
            filtered = filtered.Where(o => o.Status == status);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            filtered = filtered.Where(o =>
                o.ExternalReference.Contains(term, StringComparison.OrdinalIgnoreCase)
                || o.Customer.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || o.Customer.Email.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = filtered
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Number)
            .ToList();

        var items = ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return new OrderPage(items, ordered.Count);
    }

    public bool TryUpdate(Order updated, int expectedVersion)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(updated.Id, out var current) || current.Version != expectedVersion)
                return false;

            _byId[updated.Id] = updated;
            return true;
        }
    }
}
