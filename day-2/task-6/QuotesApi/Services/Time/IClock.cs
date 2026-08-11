namespace QuotesApi.Services.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
