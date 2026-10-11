using OrderTracker.Domain.Common;

namespace OrderTracker.Application.Abstractions;

/// <summary>A use case that changes state.</summary>
public interface ICommandHandler<in TCommand, TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>A use case that only reads.</summary>
public interface IQueryHandler<in TQuery, TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Commits the changes made to loaded and added aggregates as one transaction, then dispatches their
/// domain events. Implemented by the persistence adapter.
/// </summary>
public interface IUnitOfWork
{
    /// <exception cref="DuplicateReferenceException">Another order with the same reference was saved first.</exception>
    /// <exception cref="ConcurrencyConflictException">An aggregate was changed by someone else since it was loaded.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>The store's uniqueness guard rejected a second order with the same external reference.</summary>
public sealed class DuplicateReferenceException(Exception inner)
    : Exception("An order with this external reference already exists.", inner);

/// <summary>An aggregate was saved from a stale version (optimistic concurrency).</summary>
public sealed class ConcurrencyConflictException(Exception inner)
    : Exception("The order was changed by someone else.", inner);

/// <summary>Reacts to a domain event after the change that raised it has been committed.</summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}

public static class ClockExtensions
{
    /// <summary>
    /// Current time truncated to microseconds, the precision PostgreSQL stores, so an order returned
    /// straight after a write is identical to the same order read back later.
    /// </summary>
    public static DateTimeOffset GetUtcNowForStorage(this TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
