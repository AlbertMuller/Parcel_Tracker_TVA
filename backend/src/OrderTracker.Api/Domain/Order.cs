namespace OrderTracker.Api.Domain;

public sealed record Customer(string Id, string Email, string Name);

public sealed record LineItem(string Sku, string Name, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record StatusChange(OrderStatus? From, OrderStatus To, DateTimeOffset At);

/// <summary>A currency conversion. <paramref name="RateFromBase"/> is units of <paramref name="To"/> per 1 unit of the order's base currency.</summary>
public sealed record CurrencyChange(string From, string To, decimal RateFromBase, DateOnly RateDate, string RateSource, DateTimeOffset At);

/// <summary>
/// Immutable snapshot of an order. State changes produce a new snapshot with a bumped
/// <see cref="Version"/>, which the repository uses for optimistic concurrency.
/// </summary>
public sealed record Order
{
    public required Guid Id { get; init; }

    /// <summary>Human-friendly sequential number, assigned by the repository on insert.</summary>
    public long Number { get; init; }

    /// <summary>Client-provided reference (e.g. the rep's PO number). Unique, case-insensitive.</summary>
    public required string ExternalReference { get; init; }

    public required Customer Customer { get; init; }
    /// <summary>The currency the order is currently priced in.</summary>
    public required string Currency { get; init; }

    /// <summary>The currency the order was submitted in. Conversions always start from these prices.</summary>
    public required string BaseCurrency { get; init; }

    /// <summary>Line items as submitted, in <see cref="BaseCurrency"/>.</summary>
    public required IReadOnlyList<LineItem> BaseLineItems { get; init; }

    public required IReadOnlyList<CurrencyChange> CurrencyHistory { get; init; }
    public string? Notes { get; init; }
    public required IReadOnlyList<LineItem> LineItems { get; init; }
    public required decimal Subtotal { get; init; }
    public required decimal Total { get; init; }
    public required OrderStatus Status { get; init; }
    public required IReadOnlyList<StatusChange> StatusHistory { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required int Version { get; init; }

    /// <summary>Hash of the normalized submission, used to tell a harmless resubmit from a conflicting one.</summary>
    public required string SubmissionFingerprint { get; init; }

    public static Order Create(OrderDraft draft, Guid id, DateTimeOffset now)
    {
        var lines = draft.LineItems
            .Select(l => new LineItem(l.Sku, l.Name, l.Quantity, l.UnitPrice, Pricing.LineTotal(l.Quantity, l.UnitPrice)))
            .ToList();
        var subtotal = Pricing.Subtotal(lines);

        return new Order
        {
            Id = id,
            ExternalReference = draft.ExternalReference,
            Customer = new Customer(CustomerIds.FromEmail(draft.CustomerEmail), draft.CustomerEmail, draft.CustomerName),
            Currency = draft.Currency,
            BaseCurrency = draft.Currency,
            BaseLineItems = lines,
            CurrencyHistory = [],
            Notes = draft.Notes,
            LineItems = lines,
            Subtotal = subtotal,
            Total = Pricing.Total(subtotal),
            Status = OrderStatus.Pending,
            StatusHistory = [new StatusChange(null, OrderStatus.Pending, now)],
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
            SubmissionFingerprint = draft.Fingerprint(),
        };
    }

    /// <summary>Returns the order in its new status. Callers must check <see cref="OrderStatusRules.CanTransition"/> first.</summary>
    public Order TransitionTo(OrderStatus target, DateTimeOffset now)
    {
        if (!OrderStatusRules.CanTransition(Status, target))
            throw new InvalidOperationException(OrderStatusRules.ExplainRejection(Status, target));

        return this with
        {
            Status = target,
            StatusHistory = [.. StatusHistory, new StatusChange(Status, target, now)],
            UpdatedAt = now,
            Version = Version + 1,
        };
    }

    /// <summary>Prices are locked once an order is confirmed: the customer has agreed to them.</summary>
    public bool CanChangeCurrency => Status == OrderStatus.Pending;

    /// <summary>
    /// Re-prices the order in <paramref name="target"/>. Unit prices are always converted from the original
    /// (base) prices, never from a previous conversion, so rounding never compounds and converting back to
    /// the base currency restores the exact submitted prices.
    /// </summary>
    public Order ConvertCurrency(string target, decimal rateFromBase, DateOnly rateDate, string rateSource, DateTimeOffset now)
    {
        if (!CanChangeCurrency)
            throw new InvalidOperationException(CurrencyRules.ExplainLocked(Status));
        if (rateFromBase <= 0)
            throw new ArgumentOutOfRangeException(nameof(rateFromBase), "Exchange rate must be positive.");

        var backToBase = target == BaseCurrency;
        var lines = BaseLineItems
            .Select(b =>
            {
                var unitPrice = backToBase ? b.UnitPrice : Pricing.Round(b.UnitPrice * rateFromBase);
                return b with { UnitPrice = unitPrice, LineTotal = Pricing.LineTotal(b.Quantity, unitPrice) };
            })
            .ToList();
        var subtotal = Pricing.Subtotal(lines);

        return this with
        {
            Currency = target,
            LineItems = lines,
            Subtotal = subtotal,
            Total = Pricing.Total(subtotal),
            CurrencyHistory = [.. CurrencyHistory, new CurrencyChange(Currency, target, backToBase ? 1m : rateFromBase, rateDate, rateSource, now)],
            UpdatedAt = now,
            Version = Version + 1,
        };
    }
}

public static class CurrencyRules
{
    public static string ExplainLocked(OrderStatus status) =>
        $"The currency can only be changed while an order is Pending. This order is {status}, so its prices are locked.";
}
