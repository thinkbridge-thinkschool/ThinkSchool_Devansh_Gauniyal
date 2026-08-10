namespace CollectionApi.ValueObjects;

public sealed record CollectionItem
{
    public int QuoteId { get; }
    public DateTimeOffset AddedAt { get; }

    private CollectionItem()
    {
    }

    internal CollectionItem(int quoteId, DateTimeOffset addedAt)
    {
        QuoteId = quoteId;
        AddedAt = addedAt;
    }
}
