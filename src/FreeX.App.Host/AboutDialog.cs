using Free.Shared.Shell.Wpf;

namespace FreeX.App.Host;

/// <summary>
/// FreeX About dialog. Thin wrapper over <see cref="SharedAboutDialog"/> that supplies
/// FreeX-specific strings and automation IDs. All structural and interaction logic lives
/// in the shared base so it can be reused by FreeW (and future apps) without duplication.
/// </summary>
public sealed class AboutDialog : SharedAboutDialog
{
    public AboutDialog()
        : base(
            windowTitle: UiText.Get("MainWindowMessage_AboutFreeXTitle"),
            aboutText: AppInfo.AboutText,
            dialogAutomationId: "AboutFreeXDialog",
            textAutomationId: "AboutFreeXText",
            okAutomationId: "AboutFreeXOkButton",
            helpText: UiText.Get("MainWindow_TooltipDescription_ViewVersionAndLicenseInformationAboutFreeX"))
    {
    }
}
