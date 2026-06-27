using FreeX.App.Localization;

namespace FreeX.App.Host;

internal static class UiText
{
    public static string Ok => Get("Common_Ok");
    public static string Cancel => Get("Common_Cancel");
    public static string ErrorTitle => Get("Common_ErrorTitle");
    public static string WarningTitle => Get("Common_WarningTitle");
    public static string InformationTitle => Get("Common_InformationTitle");
    public static string ConfirmTitle => Get("Common_ConfirmTitle");

    public static string Get(string key) => Loc.Get(key);

    internal static string GetNeutral(string key) => Loc.GetNeutral(key);

    public static string Format(string key, params object?[] args) => Loc.Format(key, args);

    public static IReadOnlySet<string> GetNeutralResourceKeys() => Loc.GetNeutralResourceKeys();

    public static string CreateAutomationName(string textWithAccessKey) => Loc.CreateAutomationName(textWithAccessKey);

    public static string CreateMissingText(string key) => Loc.CreateMissingText(key);
}
