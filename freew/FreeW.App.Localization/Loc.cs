using System.Resources;
using Free.Shared.Localization;

namespace FreeW.App.Localization;

/// <summary>
/// Portable, UI-framework-agnostic localization provider for FreeW-owned shell,
/// backstage, and common UI text.
/// </summary>
public static class Loc
{
    public const string PseudoLocalizationCultureName = LocalizedTextCatalog.PseudoLocalizationCultureName;

    private const string ResourceBaseName = "FreeW.App.Localization.Resources.Strings";
    private static readonly ResourceManager ResourceManager = new(ResourceBaseName, typeof(Loc).Assembly);
    private static readonly LocalizedTextCatalog Catalog = new(ResourceManager);

    public static string Get(string key) => Catalog.Get(key);

    public static string GetNeutral(string key) => Catalog.GetNeutral(key);

    public static string Format(string key, params object?[] args) => Catalog.Format(key, args);

    public static IReadOnlySet<string> GetNeutralResourceKeys() => Catalog.GetNeutralResourceKeys();

    public static bool IsPseudoLocalizationCulture(string? cultureName) =>
        LocalizedTextCatalog.IsPseudoLocalizationCulture(cultureName);

    public static string CreateAutomationName(string textWithAccessKey) =>
        LocalizedTextCatalog.CreateAutomationName(textWithAccessKey);

    public static string CreateMissingText(string key) => LocalizedTextCatalog.CreateMissingText(key);
}
