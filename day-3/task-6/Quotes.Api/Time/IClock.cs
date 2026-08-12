namespace Quotes.Api.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
