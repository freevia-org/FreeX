using System.Reflection;

namespace Free.Shared.AppServices;

/// <summary>
/// Resolves the running app's display version from the entry assembly, preferring the
/// <see cref="AssemblyInformationalVersionAttribute"/> (the SemVer-style build string), then the assembly
/// version, then a <c>"0.0.0"</c> fallback. Shared by the sister apps' startup wiring so each tags its
/// diagnostics with the same version string the others do.
/// </summary>
public static class EntryAssemblyVersion
{
    /// <summary>The entry assembly's informational/assembly version, or <c>"0.0.0"</c> when unavailable.</summary>
    public static string Resolve()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        return entryAssembly is null
            ? "0.0.0"
            : AssemblyVersionMetadata.FromAssembly(entryAssembly).PreferredVersion ?? "0.0.0";
    }
}
