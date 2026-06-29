using FreeW.App.Localization;

namespace FreeW.App.Host;

internal static class UiText
{
    public static string Ok => LocalizedUiText.Ok;

    public static string Cancel => LocalizedUiText.Cancel;

    public static string ErrorTitle => LocalizedUiText.ErrorTitle;

    public static string WarningTitle => LocalizedUiText.WarningTitle;

    public static string InformationTitle => LocalizedUiText.InformationTitle;

    public static string ConfirmTitle => LocalizedUiText.ConfirmTitle;

    public static string Get(string key) => LocalizedUiText.Get(key);

    public static string GetNeutral(string key) => LocalizedUiText.GetNeutral(key);

    public static string Format(string key, params object?[] args) => LocalizedUiText.Format(key, args);

    public static IReadOnlySet<string> GetNeutralResourceKeys() => LocalizedUiText.GetNeutralResourceKeys();

    public static string CreateAutomationName(string textWithAccessKey) =>
        LocalizedUiText.CreateAutomationName(textWithAccessKey);
}
