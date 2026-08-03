using Free.Shared.Shell.Avalonia;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

internal sealed class AboutDialog : AvaloniaAboutDialog
{
    public AboutDialog()
        : base(
            windowTitle: "About FreeX",
            aboutText: AppHelpInfo.BuildAboutText(
                AppHelpInfo.GetVersionText(typeof(AboutDialog).Assembly),
                AppHelpInfo.AvaloniaPlatformSummary),
            dialogAutomationId: "AboutFreeXDialog",
            textAutomationId: "AboutFreeXText",
            okAutomationId: "AboutFreeXOkButton",
            helpText: "View version and license information about FreeX.")
    {
    }
}
