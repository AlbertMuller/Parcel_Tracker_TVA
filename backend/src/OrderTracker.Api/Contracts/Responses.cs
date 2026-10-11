using OrderTracker.Api.Domain;

namespace OrderTracker.Api.Contracts;

public sealed record CustomerResponse(string Id, string Email, string Name);

/// <summary><c>BaseUnitPrice</c> is the price as submitted, in the order's base currency.</summary>
public sealed record LineItemResponse(string Sku, string Name, int Quantity, decimal UnitPrice, decimal LineTotal, decimal BaseUnitPrice);

public sealed record StatusChangeResponse(OrderStatus? From, OrderStatus To, DateTimeOffset At);

public sealed record CurrencyChangeResponse(string From, string To, decimal RateFromBase, DateOnly RateDate, string RateSource, DateTimeOffset At);

public sealed record OrderResponse(
    Guid Id,
    long Number,
    string ExternalReference,
    CustomerResponse Customer,
    string Currency,
    string BaseCurrency,
    bool CanChangeCurrency,
    string? Notes,
    IReadOnlyList<LineItemResponse> LineItems,
    decimal Subtotal,
    decimal Total,
    OrderStatus Status,
    IReadOnlyList<OrderStatus> AllowedTransitions,
    IReadOnlyList<StatusChangeResponse> StatusHistory,
    IReadOnlyList<CurrencyChangeResponse> CurrencyHistory,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static OrderResponse From(Order o) => new(
        o.Id,
        o.Number,
        o.ExternalReference,
        new CustomerResponse(o.Customer.Id, o.Customer.Email, o.Customer.Name),
        o.Currency,
        o.BaseCurrency,
        o.CanChangeCurrency,
        o.Notes,
        o.LineItems.Zip(o.BaseLineItems, (l, b) => new LineItemResponse(l.Sku, l.Name, l.Quantity, l.UnitPrice, l.LineTotal, b.UnitPrice)).ToList(),
        o.Subtotal,
        o.Total,
        o.Status,
        OrderStatusRules.AllowedTransitionsFrom(o.Status),
        o.StatusHistory.Select(h => new StatusChangeResponse(h.From, h.To, h.At)).ToList(),
        o.CurrencyHistory.Select(c => new CurrencyChangeResponse(c.From, c.To, c.RateFromBase, c.RateDate, c.RateSource, c.At)).ToList(),
        o.CreatedAt,
        o.UpdatedAt);
}

public sealed record OrderSummaryResponse(
    Guid Id,
    long Number,
    string ExternalReference,
    string CustomerName,
    string CustomerEmail,
    string Currency,
    int LineCount,
    decimal Total,
    OrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static OrderSummaryResponse From(Order o) => new(
        o.Id,
        o.Number,
        o.ExternalReference,
        o.Customer.Name,
        o.Customer.Email,
        o.Currency,
        o.LineItems.Count,
        o.Total,
        o.Status,
        o.CreatedAt,
        o.UpdatedAt);
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record ExchangeRatesResponse(
    string Base,
    DateOnly Date,
    string Source,
    DateTimeOffset FetchedAt,
    bool IsStale,
    IReadOnlyDictionary<string, decimal> Rates);
