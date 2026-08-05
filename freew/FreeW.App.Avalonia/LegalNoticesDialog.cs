using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation;

namespace FreeW.App.Avalonia;

internal sealed class LegalNoticesDialog : AvaloniaLegalNoticesDialog
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
        ApplyLegalNoticesAuthorityDocumentInset();
        Opened += (_, _) => ApplyLegalNoticesAuthorityDocumentInset();
    }

    internal LegalNoticesDialog(IReadOnlyList<(string Title, string Text)> notices)
        : base(
            windowTitle: "Legal Notices",
            notices: notices,
            introText: "These notices are packaged with FreeW for offline review.",
            closeButtonContent: "Close",
            helpText: "Shows the legal, privacy, and third-party notices packaged with this FreeW executable.")
    {
        ApplyLegalNoticesAuthorityDocumentInset();
        Opened += (_, _) => ApplyLegalNoticesAuthorityDocumentInset();
    }

    private void ApplyLegalNoticesAuthorityDocumentInset()
    {
        // The WPF read-only TextBox uses its eight-pixel content padding directly.
        // Avalonia's template adds four pixels before that shared padding; this route's
        // realized template contributes one of those pixels in the paired authority.
        if (Content is not Control root)
            return;

        foreach (var textBox in root.GetLogicalDescendants().OfType<TextBox>())
        {
            textBox.Padding = new Thickness(
                LegalNoticesDialogMetrics.TextPadding + 1,
                textBox.Padding.Top,
                LegalNoticesDialogMetrics.TextPadding,
                LegalNoticesDialogMetrics.TextPadding);
        }
    }
}
