using Free.Shared.Localization;

namespace FreeX.App.Localization;

/// <summary>
/// Shared UI text facade for platform shells that keep local <c>UiText</c> entry points.
/// </summary>
public abstract class LocalizedUiText
{
    private static readonly LocalizedUiTextFacade Facade = new(Loc.Resources);

    protected LocalizedUiText()
    {
    }

    public static string Ok => Facade.Ok;

    public static string Cancel => Facade.Cancel;

    public static string ErrorTitle => Facade.ErrorTitle;

    public static string WarningTitle => Facade.WarningTitle;

    public static string InformationTitle => Facade.InformationTitle;

    public static string ConfirmTitle => Facade.ConfirmTitle;

    public static string Get(string key) => Facade.Get(key);

    public static string GetNeutral(string key) => Facade.GetNeutral(key);

    public static string Format(string key, params object?[] args) => Facade.Format(key, args);

    public static IReadOnlySet<string> GetNeutralResourceKeys() => Facade.GetNeutralResourceKeys();

    public static string CreateAutomationName(string textWithAccessKey) => Facade.CreateAutomationName(textWithAccessKey);

    public static string CreateMissingText(string key) => Facade.CreateMissingText(key);
}
