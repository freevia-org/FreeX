using Free.Shared.Shell;

namespace FreeP.App.Compositor;

/// <summary>FreeP Legal Notices content contract shared by both desktop renderers.</summary>
public static class FreePLegalNoticesPresentation
{
    public const string WindowTitle = "Legal Notices";
    public const string SummaryText = "These notices are packaged with FreeP for offline review.";
    public const string CloseButtonContent = "Close";
    public const string HelpText = "Shows the legal, privacy, and third-party notices packaged with this FreeP executable.";
    public const string SummaryAutomationName = "Legal Notices summary";
    public const string SectionsAutomationName = "Legal notice sections";
    public const string SectionLinkHelpText = "Choose a legal notice section to read and copy.";
    public const string ReadOnlyBodyHelpText = "Read-only legal notice text. Use Ctrl+C to copy selected text.";

    public static LegalNoticesDialogPresentation Create(IReadOnlyList<LegalNoticeDocument> notices) =>
        new(
            WindowTitle,
            notices,
            SummaryText,
            CloseButtonContent,
            HelpText,
            SummaryAutomationName,
            SectionsAutomationName,
            SectionLinkHelpText,
            ReadOnlyBodyHelpText,
            textRenderingPolicy: LegalNoticesTextRenderingPolicy.GrayscaleAntialias);

    public static LegalNoticesDialogPresentation Create(
        IReadOnlyList<(string Title, string Text)> notices)
    {
        ArgumentNullException.ThrowIfNull(notices);

        return Create(notices
            .Select(notice => new LegalNoticeDocument(notice.Title, string.Empty, notice.Text))
            .ToArray());
    }
}
