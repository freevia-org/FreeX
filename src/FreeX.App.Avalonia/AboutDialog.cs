using Free.Shared.Shell.Avalonia;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

internal sealed class AboutDialog : AvaloniaAboutDialog
{
    public AboutDialog()
        : base(FreeXAboutDialogPresentation.Create(typeof(AboutDialog).Assembly, "Avalonia"))
    {
    }
}
