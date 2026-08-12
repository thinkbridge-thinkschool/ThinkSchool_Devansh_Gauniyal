namespace Quotes.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
