namespace Free.Shared.Localization;

public abstract class LocalizedUiTextCatalog<TCatalog>
    where TCatalog : class
{
    protected LocalizedUiTextCatalog()
    {
    }

    public static string Ok => Get("Common_Ok");

    public static string Cancel => Get("Common_Cancel");

    public static string ErrorTitle => Get("Common_ErrorTitle");

    public static string WarningTitle => Get("Common_WarningTitle");

    public static string InformationTitle => Get("Common_InformationTitle");

    public static string ConfirmTitle => Get("Common_ConfirmTitle");

    public static string Get(string key) => LocalizedResourceCatalog<TCatalog>.Get(key);

    public static string GetNeutral(string key) => LocalizedResourceCatalog<TCatalog>.GetNeutral(key);

    public static string Format(string key, params object?[] args) =>
        LocalizedResourceCatalog<TCatalog>.Format(key, args);

    public static IReadOnlySet<string> GetNeutralResourceKeys() =>
        LocalizedResourceCatalog<TCatalog>.GetNeutralResourceKeys();

    public static string CreateAutomationName(string textWithAccessKey) =>
        LocalizedResourceCatalog<TCatalog>.CreateAutomationName(textWithAccessKey);

    public static string CreateMissingText(string key) =>
        LocalizedResourceCatalog<TCatalog>.CreateMissingText(key);
}
