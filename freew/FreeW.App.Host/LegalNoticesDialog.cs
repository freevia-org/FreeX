using Free.Shared.Shell.Wpf;
using Free.Shared.Shell;
using FreeW.App.Presentation;

namespace FreeW.App.Host;

/// <summary>
/// FreeW Legal Notices dialog. Thin wrapper over <see cref="SharedLegalNoticesDialog"/> that
/// supplies FreeW-specific strings. All structural and interaction logic lives in the shared
/// base so it can be reused across apps without duplication.
/// </summary>
public sealed partial class LegalNoticesDialog : SharedLegalNoticesDialog
{
    public LegalNoticesDialog()
        : this(FreeWLegalNoticeProvider.GetDocuments(typeof(LegalNoticesDialog).Assembly))
    {
    }

    internal LegalNoticesDialog(IReadOnlyList<LegalNoticeDocument> notices)
        : base(
            windowTitle: "Legal Notices",
            notices: notices,
            introText: "These notices are packaged with FreeW for offline review.",
            closeButtonContent: "Close",
            helpText: "Shows the legal, privacy, and third-party notices packaged with this FreeW executable.")
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
