namespace ServiceBusDemo.Core;

public static class MessageProperties
{
    /// <summary>Application property flag used to mark a message as a deliberately poison one for the DLQ demo.</summary>
    public const string Poison = "Poison";
}

public sealed record OrderMessage(string OrderId, decimal Amount)
{
    public static OrderMessage NewRandom() =>
        new(Guid.NewGuid().ToString("N"), Math.Round((decimal)(Random.Shared.NextDouble() * 500), 2));
}
