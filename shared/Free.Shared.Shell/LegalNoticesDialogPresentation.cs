using System.Text.RegularExpressions;

namespace Free.Shared.Shell;

/// <summary>Renderer-neutral heading, body, source, and navigation identity for one legal notice.</summary>
public sealed record LegalNoticeSectionPresentation(
    string Heading,
    string Body,
    string SourceResourceName,
    string LinkAutomationId,
    string BodyAutomationId);

public enum LegalNoticesTextRenderingPolicy
{
    Default,
    GrayscaleAntialias
}

/// <summary>Renderer-neutral content and accessibility semantics for a Legal Notices dialog.</summary>
public sealed class LegalNoticesDialogPresentation
{
    private static readonly Regex NonAutomationIdCharacter =
        new("[^A-Za-z0-9]+", RegexOptions.Compiled);

    public LegalNoticesDialogPresentation(
        string windowTitle,
        IReadOnlyList<LegalNoticeDocument> notices,
        string summaryText,
        string closeButtonContent,
        string helpText,
        string summaryAutomationName,
        string sectionsAutomationName,
        string sectionLinkHelpText,
        string readOnlyBodyHelpText,
        bool closeIsDefault = true,
        bool closeIsCancel = true,
        LegalNoticesTextRenderingPolicy textRenderingPolicy = LegalNoticesTextRenderingPolicy.Default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowTitle);
        ArgumentNullException.ThrowIfNull(notices);
        ArgumentNullException.ThrowIfNull(summaryText);
        ArgumentNullException.ThrowIfNull(closeButtonContent);
        ArgumentNullException.ThrowIfNull(helpText);
        ArgumentNullException.ThrowIfNull(summaryAutomationName);
        ArgumentNullException.ThrowIfNull(sectionsAutomationName);
        ArgumentNullException.ThrowIfNull(sectionLinkHelpText);
        ArgumentNullException.ThrowIfNull(readOnlyBodyHelpText);

        WindowTitle = windowTitle;
        SummaryText = summaryText;
        CloseButtonContent = closeButtonContent;
        HelpText = helpText;
        SummaryAutomationName = summaryAutomationName;
        SectionsAutomationName = sectionsAutomationName;
        SectionLinkHelpText = sectionLinkHelpText;
        ReadOnlyBodyHelpText = readOnlyBodyHelpText;
        CloseIsDefault = closeIsDefault;
        CloseIsCancel = closeIsCancel;
        TextRenderingPolicy = textRenderingPolicy;
        Sections = notices.Select(CreateSection).ToArray();
    }

    public const string DialogAutomationId = "LegalNoticesDialog";
    public const string SummaryAutomationId = "LegalNoticesSummaryText";
    public const string SectionsAutomationId = "LegalNoticesSectionTabs";
    public const string CloseButtonAutomationId = "LegalNoticesCloseButton";

    public string WindowTitle { get; }
    public string SummaryText { get; }
    public string CloseButtonContent { get; }
    public string HelpText { get; }
    public string SummaryAutomationName { get; }
    public string SectionsAutomationName { get; }
    public string SectionLinkHelpText { get; }
    public string ReadOnlyBodyHelpText { get; }
    public bool CloseIsDefault { get; }
    public bool CloseIsCancel { get; }
    public LegalNoticesTextRenderingPolicy TextRenderingPolicy { get; }
    public IReadOnlyList<LegalNoticeSectionPresentation> Sections { get; }

    private static LegalNoticeSectionPresentation CreateSection(LegalNoticeDocument notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        var automationIdSegment = NonAutomationIdCharacter.Replace(notice.Title, string.Empty);
        if (string.IsNullOrWhiteSpace(automationIdSegment))
            automationIdSegment = "Document";

        return new LegalNoticeSectionPresentation(
            notice.Title,
            notice.Text,
            notice.ResourceName,
            $"LegalNotices{automationIdSegment}Tab",
            $"LegalNotices{automationIdSegment}Text");
    }
}
