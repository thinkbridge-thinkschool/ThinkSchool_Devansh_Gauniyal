namespace Capstone.SharedKernel;

// Deliberately minimal - a shared kernel is easy to let become a dumping ground.
// Identity-based equality only; no behaviour.
public abstract class Entity<TId>(TId id) : IEquatable<Entity<TId>>
    where TId : notnull
{
    public TId Id { get; } = id;

    public bool Equals(Entity<TId>? other) =>
        other is not null && (ReferenceEquals(this, other) || Id.Equals(other.Id));

    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    public override int GetHashCode() => Id.GetHashCode();
}
