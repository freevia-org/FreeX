using System.Collections;
using System.Globalization;
using System.Resources;

namespace FreeX.App.Host;

internal static class UiText
{
    private const string ResourceBaseName = "FreeX.App.Host.Resources.Strings";
    private static readonly ResourceManager ResourceManager = new(ResourceBaseName, typeof(UiText).Assembly);

    public static string Ok => Get("Common_Ok");
    public static string Cancel => Get("Common_Cancel");
    public static string ErrorTitle => Get("Common_ErrorTitle");
    public static string WarningTitle => Get("Common_WarningTitle");
    public static string InformationTitle => Get("Common_InformationTitle");
    public static string ConfirmTitle => Get("Common_ConfirmTitle");

    public static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? CreateMissingText(key);
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

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

    public static string CreateAutomationName(string textWithAccessKey) =>
        textWithAccessKey.Replace("_", string.Empty, StringComparison.Ordinal);

    public static string CreateMissingText(string key) => "[[" + key + "]]";
}
