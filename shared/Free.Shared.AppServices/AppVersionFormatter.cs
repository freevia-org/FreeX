namespace Free.Shared.AppServices;

/// <summary>
/// Cross-app version-string formatting helpers shared between FreeX, FreeW, and FreeP.
/// All methods are pure (no I/O, no reflection) so they can be called from constants
/// or static constructors without side-effects.
/// </summary>
public static class AppVersionFormatter
{
    /// <summary>
    /// Formats <paramref name="informationalVersion"/> as a display version string.
    /// Build metadata (the <c>+…</c> suffix) is stripped before display.
    /// </summary>
    /// <param name="informationalVersion">
    /// The raw <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/> value,
    /// or <see langword="null"/> / whitespace to get the default fallback "0.5.0".
    /// </param>
    /// <param name="dropTrailingZeroPatch">
    /// When <see langword="true"/>, a three-part version whose third component is exactly
    /// <c>"0"</c> and whose first two components are all-digit (e.g. <c>0.5.0</c>) is
    /// shortened to two parts (<c>0.5</c>). FreeX uses this to produce "Version 0.5"
    /// instead of "Version 0.5.0". FreeW leaves this <see langword="false"/> to preserve
    /// its existing three-part display.
    /// </param>
    /// <returns>A string of the form <c>"Version &lt;ver&gt; (Tester Release)"</c>.</returns>
    public static string FormatVersionText(string? informationalVersion, bool dropTrailingZeroPatch = false)
    {
        var displayVersion = NormalizeVersionForDisplay(informationalVersion);

        if (dropTrailingZeroPatch)
        {
            var versionParts = displayVersion.Split('.');
            if (versionParts.Length == 3 &&
                versionParts[2] == "0" &&
                versionParts[0].All(char.IsDigit) &&
                versionParts[1].All(char.IsDigit))
            {
                displayVersion = $"{versionParts[0]}.{versionParts[1]}";
            }
        }

        return $"Version {displayVersion} (Tester Release)";
    }

    /// <summary>
    /// Formats <paramref name="informationalVersion"/> and <paramref name="assemblyVersion"/>
    /// into a build-version string that includes both when they differ.
    /// </summary>
    /// <returns>
    /// <c>"Version &lt;ver&gt; (Tester Release)"</c> when both versions are equal (ignoring
    /// case), or <c>"Version &lt;ver&gt; (build &lt;build&gt;, Tester Release)"</c> when
    /// they differ.
    /// </returns>
    public static string FormatBuildVersionText(string? informationalVersion, string? assemblyVersion = null)
    {
        var displayVersion = NormalizeVersionForDisplay(informationalVersion);
        var buildVersion = NormalizeVersionForDisplay(assemblyVersion);

        return string.Equals(displayVersion, buildVersion, StringComparison.OrdinalIgnoreCase)
            ? $"Version {displayVersion} (Tester Release)"
            : $"Version {displayVersion} (build {buildVersion}, Tester Release)";
    }

    /// <summary>
    /// Strips build metadata (<c>+…</c>) from <paramref name="version"/> and trims whitespace.
    /// Returns <c>"0.5.0"</c> when the input is null, empty, or reduces to empty after trimming.
    /// </summary>
    public static string NormalizeVersionForDisplay(string? version)
    {
        var displayVersion = string.IsNullOrWhiteSpace(version)
            ? "0.5.0"
            : version.Trim();
        var metadataIndex = displayVersion.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
            displayVersion = displayVersion[..metadataIndex];

        return string.IsNullOrWhiteSpace(displayVersion) ? "0.5.0" : displayVersion;
    }
}
