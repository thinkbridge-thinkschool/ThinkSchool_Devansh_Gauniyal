namespace OutboxDemo.Publishing;

/// <summary>
/// Marks a deterministic, test-injected process death. The relay never
/// catches this type — it must propagate all the way out, exactly like a
/// real crash would, so the message is left exactly where the crash left it.
/// </summary>
public class SimulatedCrashException : Exception
{
    public SimulatedCrashException(string message) : base(message)
    {
    }
}
