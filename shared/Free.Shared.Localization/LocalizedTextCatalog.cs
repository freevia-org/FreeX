using System.Collections;
using System.Globalization;
using System.Resources;

namespace Free.Shared.Localization;

/// <summary>
/// Shared resource lookup and pseudo-localization behavior for app-specific UI text catalogs.
/// </summary>
public sealed class LocalizedTextCatalog(
    ResourceManager resourceManager,
    ResourceManager? sharedResourceManager = null)
{
    public const string PseudoLocalizationCultureName = "qps-ploc";

    private readonly ResourceManager _resourceManager =
        resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));

    private readonly ResourceManager? _sharedResourceManager = sharedResourceManager;

    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var culture = CultureInfo.CurrentUICulture;
        if (IsPseudoLocalizationCulture(culture.Name))
        {
            var neutral = GetNeutralString(key);
            return neutral is null ? CreateMissingText(key) : PseudoLocalization.Expand(neutral);
        }

        return _resourceManager.GetString(key, culture)
            ?? _sharedResourceManager?.GetString(key, culture)
            ?? CreateMissingText(key);
    }

    public string GetNeutral(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return GetNeutralString(key) ?? CreateMissingText(key);
    }

    public string Format(string key, params object?[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            // Translation drift: the localized string has more placeholders than supplied args.
            // Fall back to the raw template rather than crashing on a localized build.
            return template;
        }
    }

    public IReadOnlySet<string> GetNeutralResourceKeys()
    {
        var keys = GetResourceKeys(_resourceManager);
        if (_sharedResourceManager is not null)
            keys.UnionWith(GetResourceKeys(_sharedResourceManager));

        return keys;
    }

    private string? GetNeutralString(string key) =>
        _resourceManager.GetString(key, CultureInfo.InvariantCulture)
        ?? _sharedResourceManager?.GetString(key, CultureInfo.InvariantCulture);

    private static HashSet<string> GetResourceKeys(ResourceManager resourceManager)
    {
        var resourceSet = resourceManager.GetResourceSet(
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
        LocalizedFallbackTextResolver.StripMnemonicMarkers(textWithAccessKey);

    public static string CreateMissingText(string key) => "[[" + key + "]]";
}
