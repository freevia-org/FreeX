using Free.Shared.Shell;

namespace FreeX.App.Host;

/// <summary>
/// FreeX implementation of the shared shell's <see cref="IShellStrings"/>, delegating to
/// the host's localized <see cref="UiText"/> catalog. Installed into
/// <see cref="ShellStrings.Current"/> at startup so the shared dialog helpers render FreeX's
/// localized button labels and message-box titles.
/// </summary>
internal sealed class FreeXShellStrings : IShellStrings
{
    public string Ok => UiText.Ok;
    public string Cancel => UiText.Cancel;
    public string ErrorTitle => UiText.ErrorTitle;
    public string WarningTitle => UiText.WarningTitle;
    public string InformationTitle => UiText.InformationTitle;
    public string ConfirmTitle => UiText.ConfirmTitle;

    public string CreateAutomationName(string textWithAccessKey) =>
        UiText.CreateAutomationName(textWithAccessKey);
}
