using CollectionApi.Exceptions;
using CollectionApi.ValueObjects;

namespace CollectionApi.Models;

public sealed class Collection
{
    public const int MaximumItems = 50;

    private readonly List<CollectionItem> _items = [];

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int OwnerId { get; private set; }
    public IReadOnlyCollection<CollectionItem> Items => _items.AsReadOnly();

    private Collection()
    {
    }

    public Collection(string? name, int ownerId)
    {
        Name = ValidateName(name);
        OwnerId = ownerId;
    }

    public void AddItem(int quoteId, DateTimeOffset addedAt)
    {
        if (_items.Any(item => item.QuoteId == quoteId))
        {
            throw new CollectionInvariantException(
                $"Quote {quoteId} is already in this collection.");
        }

        if (_items.Count >= MaximumItems)
        {
            throw new CollectionInvariantException(
                $"A collection cannot contain more than {MaximumItems} items.");
        }

        _items.Add(new CollectionItem(quoteId, addedAt));
    }

    public void RemoveItem(int quoteId)
    {
        var item = _items.SingleOrDefault(item => item.QuoteId == quoteId)
            ?? throw new CollectionInvariantException(
                $"Quote {quoteId} is not in this collection.");

        _items.Remove(item);
    }

    private static string ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CollectionInvariantException("Collection name cannot be empty.");
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length is < 3 or > 80)
        {
            throw new CollectionInvariantException(
                "Collection name must be between 3 and 80 characters.");
        }

        return trimmedName;
    }
}
