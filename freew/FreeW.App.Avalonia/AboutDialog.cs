using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation;

namespace FreeW.App.Avalonia;

internal sealed class AboutDialog : AvaloniaAboutDialog
{
    public AboutDialog()
        : base(FreeWAboutDialogPresentation.Create(typeof(AboutDialog).Assembly, "Avalonia"))
    {
    }
}
