using OrderTracker.Application.Abstractions;
using OrderTracker.Domain.Common;
using OrderTracker.Domain.Orders;

namespace OrderTracker.Application.Orders.GetOrder;

public sealed record GetOrderQuery(Guid OrderId);

public sealed class GetOrderHandler(IOrderQueries queries) : IQueryHandler<GetOrderQuery, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken = default) =>
        await queries.GetByIdAsync(query.OrderId, cancellationToken) is { } order
            ? order
            : OrderErrors.NotFound(query.OrderId);
}
