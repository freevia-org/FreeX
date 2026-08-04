using Free.Shared.Shell.Wpf;

namespace FreeW.App.Host;

/// <summary>
/// FreeW About dialog. Thin wrapper over <see cref="SharedAboutDialog"/> that supplies
/// FreeW-specific strings and automation IDs. All structural and interaction logic lives
/// in the shared base so it can be reused across apps without duplication.
/// </summary>
public sealed class AboutDialog : SharedAboutDialog
{
    public AboutDialog()
        : base(FreeWAppInfo.AboutPresentation)
    {
    }
}
