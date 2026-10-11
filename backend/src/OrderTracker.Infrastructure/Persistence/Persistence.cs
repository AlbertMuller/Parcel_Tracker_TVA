using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderTracker.Application.Abstractions;
using OrderTracker.Application.Orders;
using OrderTracker.Domain.Common;
using OrderTracker.Domain.Orders;

namespace OrderTracker.Infrastructure.Persistence;

/// <summary>Loads and tracks whole order aggregates. Owned lines and history are always loaded with the order.</summary>
internal sealed class OrderRepository(OrdersDbContext db) : IOrderRepository
{
    public Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default) =>
        db.Orders.AsSplitQuery().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Order?> GetByReferenceAsync(ExternalReference reference, CancellationToken cancellationToken = default) =>
        db.Orders.AsSplitQuery().FirstOrDefaultAsync(o => o.Reference.Normalized == reference.Normalized, cancellationToken);

    public void Add(Order order) => db.Orders.Add(order);
}

/// <summary>
/// Commits tracked changes in one transaction, translates the database's answers into application
/// exceptions, and dispatches domain events only after the commit succeeded.
/// </summary>
internal sealed class UnitOfWork(OrdersDbContext db, IDomainEventDispatcher dispatcher) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var aggregates = db.ChangeTracker.Entries<IHasDomainEvents>().Select(e => e.Entity).ToList();
        var events = aggregates.SelectMany(a => a.DomainEvents).ToList();

        try
        {
            // `orders.version` is a concurrency token: updates run as UPDATE … WHERE version = @original.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            db.ChangeTracker.Clear(); // the caller reloads and retries
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (IsDuplicateReference(ex))
        {
            db.ChangeTracker.Clear(); // the caller loads the order that won
            throw new DuplicateReferenceException(ex);
        }

        foreach (var aggregate in aggregates)
            aggregate.ClearDomainEvents();
        await dispatcher.DispatchAsync(events, cancellationToken);
    }

    private static bool IsDuplicateReference(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: OrdersDbContext.UniqueReferenceIndex,
        };
}

/// <summary>Read side: no change tracking, and the list is projected in SQL.</summary>
internal sealed class OrderQueries(OrdersDbContext db) : IOrderQueries
{
    public async Task<OrderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var orderId = new OrderId(id);
        var order = await db.Orders.AsNoTracking().AsSplitQuery().FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        return order is null ? null : OrderDto.From(order);
    }

    public async Task<PagedResult<OrderSummaryDto>> ListAsync(OrderListFilter filter, CancellationToken cancellationToken = default)
    {
        var orders = db.Orders.AsNoTracking();

        if (filter.Status is { } status)
            orders = orders.Where(o => o.Status == status);

        if (filter.Search is { Length: > 0 } search)
        {
            var pattern = $"%{EscapeLike(search)}%";
            orders = orders.Where(o =>
                EF.Functions.ILike(o.Reference.Value, pattern, @"\")
                || EF.Functions.ILike(o.Customer.Name, pattern, @"\")
                || EF.Functions.ILike(o.Customer.Email, pattern, @"\"));
        }

        var totalCount = await orders.CountAsync(cancellationToken);
        var rows = await orders
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Number)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(o => new
            {
                o.Id,
                o.Number,
                Reference = o.Reference.Value,
                o.Customer.Name,
                o.Customer.Email,
                o.Currency,
                LineCount = o.Lines.Count,
                o.Total,
                o.Status,
                o.CreatedAt,
                o.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new OrderSummaryDto(r.Id.Value, r.Number, r.Reference, r.Name, r.Email, r.Currency.Value, r.LineCount, r.Total, r.Status, r.CreatedAt, r.UpdatedAt))
            .ToList();
        return new PagedResult<OrderSummaryDto>(items, filter.Page, filter.PageSize, totalCount);
    }

    private static string EscapeLike(string term) =>
        term.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
