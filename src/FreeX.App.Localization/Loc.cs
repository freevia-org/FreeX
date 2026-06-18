using System.Collections;
using System.Globalization;
using System.Resources;

namespace FreeX.App.Localization;

/// <summary>
/// Portable, UI-framework-agnostic localization provider backed by a
/// <see cref="System.Resources.ResourceManager"/> over a <c>.resx</c> catalog owned by this
/// assembly. Mirrors the WPF host's <c>UiText</c> pattern so the macOS/Avalonia shell can become
/// culture-aware without depending on the host catalog (which is under concurrent change).
///
/// Lookups honour <see cref="CultureInfo.CurrentUICulture"/>. The synthetic
/// <c>qps-ploc</c> pseudo-localization culture expands neutral English so layout/format bugs
/// surface in tests and manual smoke runs.
/// </summary>
public static class Loc
{
    /// <summary>Synthetic culture name used to request pseudo-localized output.</summary>
    public const string PseudoLocalizationCultureName = "qps-ploc";

    private const string ResourceBaseName = "FreeX.App.Localization.Resources.Strings";
    private static readonly ResourceManager ResourceManager = new(ResourceBaseName, typeof(Loc).Assembly);

    /// <summary>
    /// Returns the localized string for <paramref name="key"/> in the current UI culture, falling
    /// back to neutral English when the active culture lacks a translation. Returns a visible
    /// <c>[[missing]]</c> marker when the key is unknown so gaps are obvious.
    /// </summary>
    public static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var culture = CultureInfo.CurrentUICulture;
        if (IsPseudoLocalizationCulture(culture.Name))
        {
            var neutral = ResourceManager.GetString(key, CultureInfo.InvariantCulture);
            return neutral is null ? CreateMissingText(key) : PseudoLocalization.Expand(neutral);
        }

        return ResourceManager.GetString(key, culture) ?? CreateMissingText(key);
    }

    /// <summary>Returns the neutral (invariant English) string for <paramref name="key"/>.</summary>
    public static string GetNeutral(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return ResourceManager.GetString(key, CultureInfo.InvariantCulture) ?? CreateMissingText(key);
    }

    /// <summary>
    /// Returns the localized format string for <paramref name="key"/> with <paramref name="args"/>
    /// substituted using the current culture's formatting rules.
    /// </summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>Returns the full set of keys defined in the neutral catalog.</summary>
    public static IReadOnlySet<string> GetNeutralResourceKeys()
    {
        var resourceSet = ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
        if (resourceSet is null)
            return new HashSet<string>(StringComparer.Ordinal);

        return resourceSet
            .Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>True when <paramref name="cultureName"/> requests pseudo-localized output.</summary>
    public static bool IsPseudoLocalizationCulture(string? cultureName) =>
        string.Equals(cultureName, PseudoLocalizationCultureName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Strips access-key markers from menu/label text to derive an automation name.</summary>
    public static string CreateAutomationName(string textWithAccessKey) =>
        textWithAccessKey.Replace("_", string.Empty, StringComparison.Ordinal);

    private static string CreateMissingText(string key) => "[[" + key + "]]";
}
