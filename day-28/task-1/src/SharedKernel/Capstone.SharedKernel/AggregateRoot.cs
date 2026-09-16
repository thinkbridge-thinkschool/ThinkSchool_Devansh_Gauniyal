namespace Capstone.SharedKernel;

// An aggregate root records the domain events it raises but never dispatches them
// itself - it has no reference to a bus, a repository, or anything outside its own
// state. The application layer reads DomainEvents after a use case completes and
// decides what to do with them (this scaffold: nothing yet but recording; see
// README.md). This is what keeps the domain layer free of infrastructure concerns
// while still making "something happened" a first-class, inspectable fact.
public abstract class AggregateRoot<TId>(TId id) : Entity<TId>(id)
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
