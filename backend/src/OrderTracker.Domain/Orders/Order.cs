using OrderTracker.Domain.Common;
using OrderTracker.Domain.Orders.Events;

namespace OrderTracker.Domain.Orders;

/// <summary>
/// Aggregate root for a customer purchase order. All changes go through its methods, which keep these
/// invariants true:
/// <list type="bullet">
/// <item>totals are always derived from the lines (never set from outside);</item>
/// <item>status only moves along <see cref="OrderStatusPolicy"/>;</item>
/// <item>the currency can only change while Pending, and always re-prices from the submitted prices;</item>
/// <item>every change is recorded in the history and bumps <see cref="Version"/> (optimistic concurrency).</item>
/// </list>
/// </summary>
public sealed class Order : AggregateRoot<OrderId>
{
    public const int MaxLines = 100;
    public const int MaxNotesLength = 1000;

    private readonly List<OrderLine> _lines = [];
    private readonly List<OrderStatusChange> _statusHistory = [];
    private readonly List<OrderCurrencyChange> _currencyHistory = [];

    private Order() { } // for EF Core

    /// <summary>Human-friendly sequential number, assigned by the database on insert.</summary>
    public long Number { get; private set; }

    public ExternalReference Reference { get; private set; } = null!;
    public CustomerInfo Customer { get; private set; } = null!;

    /// <summary>The currency the order is currently priced in.</summary>
    public CurrencyCode Currency { get; private set; } = null!;

    /// <summary>The currency the order was submitted in. Conversions always start from the prices in it.</summary>
    public CurrencyCode BaseCurrency { get; private set; } = null!;

    public string? Notes { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal Total { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Incremented by every change; the database rejects an update made from a stale version.</summary>
    public int Version { get; private set; }

    /// <summary>Hash of the submission, used to tell a harmless resubmit from a conflicting one.</summary>
    public string SubmissionFingerprint { get; private set; } = "";

    public IReadOnlyList<OrderLine> Lines => _lines;
    public IReadOnlyList<OrderStatusChange> StatusHistory => _statusHistory;
    public IReadOnlyList<OrderCurrencyChange> CurrencyHistory => _currencyHistory;

    public IReadOnlyList<OrderStatus> AllowedTransitions => OrderStatusPolicy.AllowedTransitionsFrom(Status);

    /// <summary>Prices are locked once an order is confirmed: the customer has agreed to them.</summary>
    public bool CanChangeCurrency => Status == OrderStatus.Pending;

    public static Order Submit(
        OrderId id,
        ExternalReference reference,
        CustomerInfo customer,
        CurrencyCode currency,
        string? notes,
        IReadOnlyList<NewOrderLine> lines,
        DateTimeOffset now)
    {
        if (lines.Count is 0 or > MaxLines)
            throw new DomainException($"An order needs 1 to {MaxLines} lines.");

        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (trimmedNotes?.Length > MaxNotesLength)
            throw new DomainException($"Notes must be at most {MaxNotesLength} characters.");

        var order = new Order
        {
            Id = id,
            Reference = reference,
            Customer = customer,
            Currency = currency,
            BaseCurrency = currency,
            Notes = trimmedNotes,
            Status = OrderStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        order._lines.AddRange(lines.Select((line, i) => new OrderLine(i + 1, line)));
        order.RecalculateTotals();
        order._statusHistory.Add(new OrderStatusChange(null, OrderStatus.Pending, now));
        order.SubmissionFingerprint = Orders.SubmissionFingerprint.Compute(reference, customer, currency, trimmedNotes, order._lines);
        order.Raise(new OrderSubmitted(id, reference.Value, order.Total, currency.Value, now));
        return order;
    }

    /// <summary>True if <paramref name="other"/> is the same submission (same reference and same content).</summary>
    public bool IsSameSubmissionAs(Order other) => SubmissionFingerprint == other.SubmissionFingerprint;

    /// <summary>
    /// Moves the order to <paramref name="target"/>. Asking for the current status is a harmless no-op,
    /// so a double-clicked button isn't an error.
    /// </summary>
    public Result ChangeStatus(OrderStatus target, DateTimeOffset now)
    {
        if (Status == target)
            return Result.Success();

        if (!OrderStatusPolicy.CanTransition(Status, target))
            return OrderErrors.TransitionNotAllowed(Status, target);

        var from = Status;
        Status = target;
        _statusHistory.Add(new OrderStatusChange(from, target, now));
        Touch(now);
        Raise(new OrderStatusChanged(Id, from, target, now));
        return Result.Success();
    }

    /// <summary>
    /// Re-prices the order at <paramref name="rate"/>, which must be quoted from <see cref="BaseCurrency"/>.
    /// Prices are always converted from the submitted prices, never from a previous conversion, so rounding
    /// never compounds; converting back to the base currency restores the exact submitted prices.
    /// </summary>
    public Result ConvertCurrency(ExchangeRate rate, DateTimeOffset now)
    {
        if (rate.To == Currency)
            return Result.Success();

        if (!CanChangeCurrency)
            return OrderErrors.CurrencyLocked(Status);

        if (rate.From != BaseCurrency)
            throw new DomainException($"Rates must be quoted from the base currency {BaseCurrency}, not {rate.From}.");

        var backToBase = rate.To == BaseCurrency;
        foreach (var line in _lines)
            line.PriceAt(backToBase ? null : rate.Rate);
        RecalculateTotals();

        var from = Currency;
        Currency = rate.To;
        _currencyHistory.Add(new OrderCurrencyChange(from, rate.To, backToBase ? 1m : rate.Rate, rate.Date, rate.Source, now));
        Touch(now);
        Raise(new OrderCurrencyConverted(Id, from.Value, rate.To.Value, rate.Rate, now));
        return Result.Success();
    }

    private void RecalculateTotals()
    {
        Subtotal = Pricing.Subtotal(_lines.Select(l => l.LineTotal));
        Total = Pricing.Total(Subtotal);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

/// <summary>One status change in an order's history (append-only).</summary>
public sealed class OrderStatusChange
{
    private OrderStatusChange() { } // for EF Core

    internal OrderStatusChange(OrderStatus? from, OrderStatus to, DateTimeOffset at) => (From, To, At) = (from, to, at);

    public long Id { get; private set; }
    public OrderStatus? From { get; private set; }
    public OrderStatus To { get; private set; }
    public DateTimeOffset At { get; private set; }
}

/// <summary>One currency conversion in an order's history (append-only).</summary>
public sealed class OrderCurrencyChange
{
    private OrderCurrencyChange() { } // for EF Core

    internal OrderCurrencyChange(CurrencyCode from, CurrencyCode to, decimal rateFromBase, DateOnly rateDate, string rateSource, DateTimeOffset at) =>
        (From, To, RateFromBase, RateDate, RateSource, At) = (from, to, rateFromBase, rateDate, rateSource, at);

    public long Id { get; private set; }
    public CurrencyCode From { get; private set; } = null!;
    public CurrencyCode To { get; private set; } = null!;

    /// <summary>Units of <see cref="To"/> per 1 unit of the order's base currency.</summary>
    public decimal RateFromBase { get; private set; }

    public DateOnly RateDate { get; private set; }
    public string RateSource { get; private set; } = "";
    public DateTimeOffset At { get; private set; }
}
