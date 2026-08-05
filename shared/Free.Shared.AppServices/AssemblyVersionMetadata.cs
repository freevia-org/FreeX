using System.Reflection;

namespace Free.Shared.AppServices;

/// <summary>Portable assembly version metadata used by About, diagnostics, and startup surfaces.</summary>
public sealed record AssemblyVersionMetadata(
    string? InformationalVersion,
    string? AssemblyVersion)
{
    public string? PreferredVersion =>
        !string.IsNullOrWhiteSpace(InformationalVersion)
            ? InformationalVersion
            : AssemblyVersion;

    public static AssemblyVersionMetadata FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return new AssemblyVersionMetadata(
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            assembly.GetName().Version?.ToString());
    }
}
