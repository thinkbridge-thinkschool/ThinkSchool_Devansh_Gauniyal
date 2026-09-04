namespace QuotesApi.Resilience;

// Thrown only by the fault-injection switch below (never by real dependency code),
// so a log line or a test assertion can tell an intentionally-injected failure apart
// from a genuine bug at a glance.
public sealed class InjectedFaultException(string dependencyName)
    : Exception($"Injected failure for '{dependencyName}' (fault-injection switch set to Failing).")
{
    public string DependencyName { get; } = dependencyName;
}
