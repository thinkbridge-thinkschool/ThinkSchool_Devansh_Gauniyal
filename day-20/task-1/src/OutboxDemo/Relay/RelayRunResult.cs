namespace OutboxDemo.Relay;

public record RelayRunResult(
    IReadOnlyList<Guid> Published,
    IReadOnlyList<Guid> Failed,
    IReadOnlyList<Guid> SkippedClaimedByOther);
