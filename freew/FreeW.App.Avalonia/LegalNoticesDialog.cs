using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;

namespace FreeW.App.Avalonia;

internal static class FreeWLegalNoticeProvider
{
    private static readonly (string Title, string ResourceName)[] Resources =
    [
        ("Project License", "FreeW.Legal.ProjectLicense.txt"),
        ("Legal Notices", "FreeW.Legal.LegalNotices.md"),
        ("Privacy Notice", "FreeW.Legal.PrivacyNotice.md"),
        ("Third-Party Notices", "FreeW.Legal.ThirdPartyNotices.md"),
        ("Third-Party License Texts", "FreeW.Legal.ThirdPartyLicenses.md"),
    ];

    internal static IReadOnlyList<(string Title, string ResourceName)> ExpectedEmbeddedResources =>
        Resources;

    public static IReadOnlyList<(string Title, string Text)> GetDocuments() =>
        GetDocuments(typeof(FreeWLegalNoticeProvider).Assembly);

    internal static IReadOnlyList<(string Title, string Text)> GetDocuments(Assembly assembly) =>
        EmbeddedLegalNoticeLoader.GetDocuments(assembly, Resources);
}

internal sealed class LegalNoticesDialog : AvaloniaLegalNoticesDialog
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
