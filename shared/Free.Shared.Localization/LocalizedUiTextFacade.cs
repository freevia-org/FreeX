namespace Free.Shared.Localization;

public sealed class LocalizedUiTextFacade(LocalizedResourceFacade resources)
{
    public string Ok => Get("Common_Ok");

    public string Cancel => Get("Common_Cancel");

    public string ErrorTitle => Get("Common_ErrorTitle");

    public string WarningTitle => Get("Common_WarningTitle");

    public string InformationTitle => Get("Common_InformationTitle");

    public string ConfirmTitle => Get("Common_ConfirmTitle");

    public string Get(string key) => resources.Get(key);

    public string GetNeutral(string key) => resources.GetNeutral(key);

    public string Format(string key, params object?[] args) => resources.Format(key, args);

    public IReadOnlySet<string> GetNeutralResourceKeys() => resources.GetNeutralResourceKeys();

    public string CreateAutomationName(string textWithAccessKey) =>
        resources.CreateAutomationName(textWithAccessKey);

    public string CreateMissingText(string key) => resources.CreateMissingText(key);
}
