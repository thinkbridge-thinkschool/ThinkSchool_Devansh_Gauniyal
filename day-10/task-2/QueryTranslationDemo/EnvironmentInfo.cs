using System.Reflection;
using System.Runtime.InteropServices;

namespace QueryTranslationDemo;

public static class EnvironmentInfo
{
    public static string EfCoreVersion()
    {
        var assembly = typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "unknown";
    }

    public static string DotNetVersion() => Environment.Version.ToString();

    public static string RuntimeIdentifier() => RuntimeInformation.RuntimeIdentifier;

    public static string Architecture() => RuntimeInformation.ProcessArchitecture.ToString();
}
