using System.Resources;
using FreeX.App.Localization;

namespace FreeX.App.Host;

internal static class UiText
{
    private const string ResourceBaseName = "FreeX.App.Host.Resources.Strings";
    private static readonly ResourceManager ResourceManager = new(ResourceBaseName, typeof(UiText).Assembly);
    private static readonly LocalizedTextCatalog Catalog = new(ResourceManager);

    public static string Ok => Get("Common_Ok");
    public static string Cancel => Get("Common_Cancel");
    public static string ErrorTitle => Get("Common_ErrorTitle");
    public static string WarningTitle => Get("Common_WarningTitle");
    public static string InformationTitle => Get("Common_InformationTitle");
    public static string ConfirmTitle => Get("Common_ConfirmTitle");

    public static string Get(string key) => Catalog.Get(key);

    internal static string GetNeutral(string key) => Catalog.GetNeutral(key);

    public static string Format(string key, params object?[] args) => Catalog.Format(key, args);

    public static IReadOnlySet<string> GetNeutralResourceKeys() => Catalog.GetNeutralResourceKeys();

    public static string CreateAutomationName(string textWithAccessKey) =>
        LocalizedTextCatalog.CreateAutomationName(textWithAccessKey);

    public static string CreateMissingText(string key) => LocalizedTextCatalog.CreateMissingText(key);
}
