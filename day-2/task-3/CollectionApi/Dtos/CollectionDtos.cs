namespace CollectionApi.Dtos;

public sealed record CreateCollectionRequest(string? Name, int OwnerId);

public sealed record AddQuoteRequest(int QuoteId);

public sealed record CollectionItemResponse(int QuoteId, DateTimeOffset AddedAt);

public sealed record CollectionResponse(
    int Id,
    string Name,
    int OwnerId,
    IReadOnlyCollection<CollectionItemResponse> Items);
