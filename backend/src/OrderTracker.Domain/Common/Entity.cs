namespace OrderTracker.Domain.Common;

/// <summary>Something that happened in the domain that other parts of the system may react to.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

/// <summary>An object defined by its identity rather than its attributes.</summary>
public abstract class Entity<TId> where TId : notnull
{
    public TId Id { get; protected set; } = default!;

    public override bool Equals(object? obj) =>
        obj is Entity<TId> other && other.GetType() == GetType() && EqualityComparer<TId>.Default.Equals(Id, other.Id);

    public override int GetHashCode() => EqualityComparer<TId>.Default.GetHashCode(Id);
}

/// <summary>Raised domain events are dispatched by the unit of work after the aggregate is saved.</summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}

/// <summary>
/// The entry point to a consistency boundary. Everything inside the aggregate is changed only through
/// its methods, which keep the invariants true, and it's loaded and saved as a whole.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}

/// <summary>
/// Thrown when code tries to put the domain into an invalid state. User input is validated before it
/// reaches the domain, so this signals a programming error, not a user mistake.
/// </summary>
public sealed class DomainException(string message) : Exception(message);
