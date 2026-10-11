using OrderTracker.Domain.Orders;

namespace OrderTracker.Application.Orders;

// Read models returned by the use cases. Their shape is the API's JSON contract.

public sealed record CustomerDto(string Id, string Email, string Name);

/// <summary><c>BaseUnitPrice</c> is the price as submitted, in the order's base currency.</summary>
public sealed record LineItemDto(string Sku, string Name, int Quantity, decimal UnitPrice, decimal LineTotal, decimal BaseUnitPrice);

public sealed record StatusChangeDto(OrderStatus? From, OrderStatus To, DateTimeOffset At);

public sealed record CurrencyChangeDto(string From, string To, decimal RateFromBase, DateOnly RateDate, string RateSource, DateTimeOffset At);

public sealed record OrderDto(
    Guid Id,
    long Number,
    string ExternalReference,
    CustomerDto Customer,
    string Currency,
    string BaseCurrency,
    bool CanChangeCurrency,
    string? Notes,
    IReadOnlyList<LineItemDto> LineItems,
    decimal Subtotal,
    decimal Total,
    OrderStatus Status,
    IReadOnlyList<OrderStatus> AllowedTransitions,
    IReadOnlyList<StatusChangeDto> StatusHistory,
    IReadOnlyList<CurrencyChangeDto> CurrencyHistory,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static OrderDto From(Order o) => new(
        o.Id.Value,
        o.Number,
        o.Reference.Value,
        new CustomerDto(o.Customer.Id, o.Customer.Email, o.Customer.Name),
        o.Currency.Value,
        o.BaseCurrency.Value,
        o.CanChangeCurrency,
        o.Notes,
        o.Lines.OrderBy(l => l.LineNo)
            .Select(l => new LineItemDto(l.Sku, l.Name, l.Quantity, l.UnitPrice, l.LineTotal, l.BaseUnitPrice))
            .ToList(),
        o.Subtotal,
        o.Total,
        o.Status,
        o.AllowedTransitions,
        o.StatusHistory.OrderBy(h => h.Id).ThenBy(h => h.At)
            .Select(h => new StatusChangeDto(h.From, h.To, h.At))
            .ToList(),
        o.CurrencyHistory.OrderBy(c => c.Id).ThenBy(c => c.At)
            .Select(c => new CurrencyChangeDto(c.From.Value, c.To.Value, c.RateFromBase, c.RateDate, c.RateSource, c.At))
            .ToList(),
        o.CreatedAt,
        o.UpdatedAt);
}

public sealed record OrderSummaryDto(
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
    DateTimeOffset UpdatedAt);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record OrderListFilter(OrderStatus? Status, string? Search, int Page, int PageSize);

/// <summary>Read-side queries, answered straight from the store without loading aggregates for change.</summary>
public interface IOrderQueries
{
    Task<OrderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Orders newest first (creation time, then order number).</summary>
    Task<PagedResult<OrderSummaryDto>> ListAsync(OrderListFilter filter, CancellationToken cancellationToken = default);
}
