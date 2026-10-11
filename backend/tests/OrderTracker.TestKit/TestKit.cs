using System.Reflection;
using OrderTracker.Application.Abstractions;
using OrderTracker.Application.ExchangeRates;
using OrderTracker.Application.Orders;
using OrderTracker.Application.Orders.SubmitOrder;
using OrderTracker.Domain.Common;
using OrderTracker.Domain.Orders;

namespace OrderTracker.TestKit;

public static class TestData
{
    public static readonly DateTimeOffset Start = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    /// <summary>3 × 19.99 + 1 × 5.50 USD = 65.47 USD.</summary>
    public static SubmitOrderCommand ValidCommand(string reference = "PO-1001", params LineItemInput[] lines) => new(
        ExternalReference: reference,
        Customer: new CustomerInput("Jane.Doe@Example.com", "Jane Doe"),
        Currency: "usd",
        Notes: "Deliver to loading bay 2",
        LineItems: lines.Length > 0
            ? lines.ToList<LineItemInput?>()
            : [new LineItemInput("SKU-A", "Widget", 3, 19.99m), new LineItemInput("SKU-B", "Gadget", 1, 5.50m)]);

    /// <summary>A valid order built straight from the domain, for persistence tests.</summary>
    public static Order NewOrder(string reference, DateTimeOffset? at = null, string customerName = "Jane Doe", params NewOrderLine[] lines)
    {
        var email = EmailAddress.Create("jane@example.com").Value;
        return Order.Submit(
            OrderId.New(),
            ExternalReference.Create(reference).Value,
            CustomerInfo.Create(customerName, email).Value,
            CurrencyCode.Create("USD").Value,
            "Deliver to loading bay 2",
            lines.Length > 0 ? lines : [new NewOrderLine("SKU-A", "Widget", 3, 19.99m), new NewOrderLine("SKU-B", "Gadget", 1, 5.50m)],
            at ?? Start);
    }
}

/// <summary>Deterministic clock for tests.</summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public ManualTimeProvider() : this(TestData.Start) { }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>In-memory rates with a switch to simulate an outage. Rates are fixed so expected totals are exact.</summary>
public sealed class FakeExchangeRateProvider : IExchangeRateProvider
{
    public static readonly DateOnly RateDate = new(2026, 1, 14);

    private readonly Dictionary<string, Dictionary<string, decimal>> _rates = new()
    {
        ["USD"] = new() { ["EUR"] = 0.9m, ["GBP"] = 0.75m, ["JPY"] = 150m },
        ["EUR"] = new() { ["USD"] = 1.1m, ["GBP"] = 0.85m },
        ["GBP"] = new() { ["USD"] = 1.3m, ["EUR"] = 1.2m },
    };

    public bool Fail { get; set; }
    public int RateCalls { get; private set; }

    public Task<ExchangeRateTable?> GetLatestAsync(string baseCurrency, CancellationToken cancellationToken)
    {
        RateCalls++;
        if (Fail) throw new ExchangeRatesUnavailableException("Simulated outage.");
        return Task.FromResult(_rates.TryGetValue(baseCurrency, out var rates)
            ? new ExchangeRateTable(baseCurrency, RateDate, rates, "Test rates")
            : null);
    }

    public Task<IReadOnlyDictionary<string, string>> GetCurrenciesAsync(CancellationToken cancellationToken)
    {
        if (Fail) throw new ExchangeRatesUnavailableException("Simulated outage.");
        IReadOnlyDictionary<string, string> currencies = new Dictionary<string, string>
        {
            ["USD"] = "US Dollar", ["EUR"] = "Euro", ["GBP"] = "British Pound", ["JPY"] = "Japanese Yen",
        };
        return Task.FromResult(currencies);
    }
}

/// <summary>
/// Fast in-memory stand-in for the persistence adapter (repository, unit of work and read queries), so
/// use cases can be unit tested without a database. It enforces reference uniqueness at save time, like
/// the real unique index, and records dispatched domain events. Real PostgreSQL behaviour is covered by
/// the integration tests.
/// </summary>
public sealed class InMemoryOrderStore : IOrderRepository, IUnitOfWork, IOrderQueries
{
    private static readonly PropertyInfo NumberProperty = typeof(Order).GetProperty(nameof(Order.Number))!;

    private readonly object _gate = new();
    private readonly Dictionary<OrderId, Order> _orders = new();
    private readonly List<Order> _pending = [];
    private long _lastNumber;

    public List<IDomainEvent> DispatchedEvents { get; } = [];

    /// <summary>Runs just before a save commits: lets a test simulate another request winning a race.</summary>
    public Action? BeforeSave { get; set; }

    /// <summary>Number of upcoming saves that fail as if another request changed the order first.</summary>
    public int ConcurrencyConflictsToSimulate { get; set; }

    public IReadOnlyCollection<Order> All
    {
        get { lock (_gate) return _orders.Values.ToList(); }
    }

    // ── IOrderRepository ──────────────────────────────────────────────

    public Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(_orders.GetValueOrDefault(id));
    }

    public Task<Order?> GetByReferenceAsync(ExternalReference reference, CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(_orders.Values.FirstOrDefault(o => o.Reference.Normalized == reference.Normalized));
    }

    public void Add(Order order)
    {
        lock (_gate) _pending.Add(order);
    }

    /// <summary>Stores an order directly, bypassing the handler (e.g. to set up a race).</summary>
    public void Seed(Order order)
    {
        lock (_gate)
        {
            Commit(order);
            order.ClearDomainEvents();
        }
    }

    // ── IUnitOfWork ───────────────────────────────────────────────────

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        BeforeSave?.Invoke();
        BeforeSave = null;

        List<IDomainEvent> events;
        lock (_gate)
        {
            if (ConcurrencyConflictsToSimulate > 0)
            {
                ConcurrencyConflictsToSimulate--;
                throw new ConcurrencyConflictException(new InvalidOperationException("Simulated concurrent change."));
            }

            foreach (var order in _pending)
            {
                if (_orders.Values.Any(o => o.Reference.Normalized == order.Reference.Normalized))
                {
                    _pending.Clear();
                    throw new DuplicateReferenceException(new InvalidOperationException("Simulated unique index violation."));
                }
                Commit(order);
            }
            _pending.Clear();

            var aggregates = _orders.Values.Where(o => o.DomainEvents.Count > 0).ToList();
            events = aggregates.SelectMany(o => o.DomainEvents).ToList();
            aggregates.ForEach(o => o.ClearDomainEvents());
            DispatchedEvents.AddRange(events);
        }
        return Task.CompletedTask;
    }

    // ── IOrderQueries ─────────────────────────────────────────────────

    async Task<OrderDto?> IOrderQueries.GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await GetByIdAsync(new OrderId(id), cancellationToken) is { } order ? OrderDto.From(order) : null;

    public Task<PagedResult<OrderSummaryDto>> ListAsync(OrderListFilter filter, CancellationToken cancellationToken = default)
    {
        IEnumerable<Order> orders = All;
        if (filter.Status is { } status)
            orders = orders.Where(o => o.Status == status);
        if (filter.Search is { } term)
            orders = orders.Where(o =>
                o.Reference.Value.Contains(term, StringComparison.OrdinalIgnoreCase)
                || o.Customer.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || o.Customer.Email.Contains(term, StringComparison.OrdinalIgnoreCase));

        var ordered = orders.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Number).ToList();
        var page = ordered.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(o => new OrderSummaryDto(o.Id.Value, o.Number, o.Reference.Value, o.Customer.Name, o.Customer.Email,
                o.Currency.Value, o.Lines.Count, o.Total, o.Status, o.CreatedAt, o.UpdatedAt))
            .ToList();
        return Task.FromResult(new PagedResult<OrderSummaryDto>(page, filter.Page, filter.PageSize, ordered.Count));
    }

    private void Commit(Order order)
    {
        NumberProperty.SetValue(order, ++_lastNumber);
        _orders[order.Id] = order;
    }
}
