using System.Collections;
using System.Globalization;
using System.Resources;

namespace FreeX.App.Localization;

/// <summary>
/// Shared resource lookup and pseudo-localization behavior for app-specific UI text catalogs.
/// </summary>
public sealed class LocalizedTextCatalog(ResourceManager resourceManager)
{
    public const string PseudoLocalizationCultureName = "qps-ploc";

    private readonly ResourceManager _resourceManager =
        resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));

    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var culture = CultureInfo.CurrentUICulture;
        if (IsPseudoLocalizationCulture(culture.Name))
        {
            var neutral = _resourceManager.GetString(key, CultureInfo.InvariantCulture);
            return neutral is null ? CreateMissingText(key) : PseudoLocalization.Expand(neutral);
        }

        return _resourceManager.GetString(key, culture) ?? CreateMissingText(key);
    }

    public string GetNeutral(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _resourceManager.GetString(key, CultureInfo.InvariantCulture) ?? CreateMissingText(key);
    }

    public string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    public IReadOnlySet<string> GetNeutralResourceKeys()
    {
        var resourceSet = _resourceManager.GetResourceSet(
            CultureInfo.InvariantCulture,
            createIfNotExists: true,
            tryParents: true);
        if (resourceSet is null)
            return new HashSet<string>(StringComparer.Ordinal);

        return resourceSet
            .Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    public static bool IsPseudoLocalizationCulture(string? cultureName) =>
        string.Equals(cultureName, PseudoLocalizationCultureName, StringComparison.OrdinalIgnoreCase);

    public static string CreateAutomationName(string textWithAccessKey) =>
        textWithAccessKey.Replace("_", string.Empty, StringComparison.Ordinal);

    public static string CreateMissingText(string key) => "[[" + key + "]]";
}
