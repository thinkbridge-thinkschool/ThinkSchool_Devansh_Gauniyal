namespace Capstone.SharedKernel;

// Marker for an in-process domain event. Aggregates raise these; the application
// layer inspects and reacts to them after a use case completes. No dispatcher, no
// bus - wiring one is Day 28+ work (see capstone/README.md, "what's deliberately
// not built yet").
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
