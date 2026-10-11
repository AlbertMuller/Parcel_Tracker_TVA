using OrderTracker.Application.Abstractions;
using OrderTracker.Domain.Common;
using OrderTracker.Domain.Orders;

namespace OrderTracker.Application.Orders.ListOrders;

public sealed record ListOrdersQuery(string? Status = null, string? Search = null, int? Page = null, int? PageSize = null);

/// <summary>Orders newest first, optionally filtered by status and searched by reference, customer name or email.</summary>
public sealed class ListOrdersHandler(IOrderQueries queries) : IQueryHandler<ListOrdersQuery, Result<PagedResult<OrderSummaryDto>>>
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public async Task<Result<PagedResult<OrderSummaryDto>>> HandleAsync(ListOrdersQuery query, CancellationToken cancellationToken = default)
    {
        OrderStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!OrderStatusPolicy.TryParse(query.Status, out var parsed))
                return OrderErrors.UnknownStatus(query.Status);
            status = parsed;
        }

        var filter = new OrderListFilter(
            status,
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            Page: Math.Max(1, query.Page ?? 1),
            PageSize: Math.Clamp(query.PageSize ?? DefaultPageSize, 1, MaxPageSize));

        return await queries.ListAsync(filter, cancellationToken);
    }
}
