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
    internal static readonly LocalizedResourceFacade Resources = new(ResourceBaseName, typeof(Loc).Assembly);

    public static string Get(string key) => Resources.Get(key);

    public static string GetNeutral(string key) => Resources.GetNeutral(key);

    public static string Format(string key, params object?[] args) => Resources.Format(key, args);

    public static IReadOnlySet<string> GetNeutralResourceKeys() => Resources.GetNeutralResourceKeys();

    public static bool IsPseudoLocalizationCulture(string? cultureName) =>
        Resources.IsPseudoLocalizationCulture(cultureName);

    public static string CreateAutomationName(string textWithAccessKey) =>
        Resources.CreateAutomationName(textWithAccessKey);

    public static string CreateMissingText(string key) => Resources.CreateMissingText(key);
}
