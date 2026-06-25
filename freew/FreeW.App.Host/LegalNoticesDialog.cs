using Free.Shared.Shell.Wpf;

namespace FreeW.App.Host;

/// <summary>
/// FreeW Legal Notices dialog. Thin wrapper over <see cref="SharedLegalNoticesDialog"/> that
/// supplies FreeW-specific strings. All structural and interaction logic lives in the shared
/// base so it can be reused across apps without duplication.
/// </summary>
public sealed partial class LegalNoticesDialog : SharedLegalNoticesDialog
{
    public LegalNoticesDialog()
        : this(FreeWLegalNoticeProvider.GetDocuments())
    {
    }

    internal LegalNoticesDialog(IReadOnlyList<(string Title, string Text)> notices)
        : base(
            windowTitle: "Legal Notices",
            notices: notices,
            introText: "These notices are packaged with FreeW for offline review.",
            closeButtonContent: "Close",
            helpText: "Shows the legal, privacy, and third-party notices packaged with this FreeW executable.")
    {
    }
}
