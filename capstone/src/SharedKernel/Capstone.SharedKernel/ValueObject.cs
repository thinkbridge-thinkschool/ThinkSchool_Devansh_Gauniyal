namespace Capstone.SharedKernel;

// Structural equality helper for value objects (Money, snapshots, policies) that
// aren't C# `record`s - kept for the cases where a record's positional-parameter
// shape doesn't fit. Most value objects in this codebase are records instead, which
// get this for free from the language; this exists so the choice is deliberate, not
// because value equality was unavailable.
public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other)
    {
        if (other is null || other.GetType() != GetType())
        {
            return false;
        }

        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override bool Equals(object? obj) => Equals(obj as ValueObject);

    public override int GetHashCode() =>
        GetEqualityComponents().Aggregate(0, HashCode.Combine);
}
