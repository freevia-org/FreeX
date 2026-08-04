using Free.Shared.Shell.Wpf;
using FreeX.App.Services;

namespace FreeX.App.Host;

/// <summary>
/// FreeX About dialog. Host structure and interaction remain in the shared WPF realization.
/// </summary>
public sealed class AboutDialog : SharedAboutDialog
{
    public AboutDialog()
        : base(FreeXAboutDialogPresentation.Create(
            typeof(AboutDialog).Assembly,
            "WPF",
            UiText.Get("MainWindowMessage_AboutFreeXTitle"),
            UiText.Get("MainWindow_TooltipDescription_ViewVersionAndLicenseInformationAboutFreeX"),
            AppInfo.ThirdPartyRuntimeNotice))
    {
    }
}
