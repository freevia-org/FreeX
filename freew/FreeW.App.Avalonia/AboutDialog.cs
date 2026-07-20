using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation;

namespace FreeW.App.Avalonia;

internal sealed class AboutDialog : AvaloniaAboutDialog
{
    public AboutDialog()
        : base(
            windowTitle: "About FreeW",
            aboutText: FreeWProductInfo.CreateAboutText(typeof(AboutDialog).Assembly, "Avalonia"),
            dialogAutomationId: "AboutFreeWDialog",
            textAutomationId: "AboutFreeWText",
            okAutomationId: "AboutFreeWOkButton",
            helpText: "View version, license, privacy, and source information about FreeW.")
    {
    }
}
