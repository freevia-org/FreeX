namespace Free.Shared.Shell;

/// <summary>
/// Product-parameterised Legal Notices dialog content shared by sister apps. Every string has a
/// literal English default; an optional resolver (shaped like FreeX's <c>resolveText</c>: key in,
/// text out, <see langword="null"/> meaning "use the default") lets a product localise it later
/// without changing its call sites. Summary and help defaults contain a <c>{0}</c> product-name
/// placeholder; a resolved value may contain it too and has it replaced with the product name.
/// </summary>
public static class ProductLegalNoticesPresentation
{
    public const string WindowTitleKey = "LegalNotices_WindowTitle";
    public const string SummaryKey = "LegalNotices_Summary";
    public const string CloseButtonKey = "LegalNotices_CloseButton";
    public const string HelpKey = "LegalNotices_Help";
    public const string SummaryAutomationNameKey = "LegalNotices_SummaryAutomationName";
    public const string SectionsAutomationNameKey = "LegalNotices_SectionsAutomationName";
    public const string SectionLinkHelpKey = "LegalNotices_SectionLinkHelp";
    public const string ReadOnlyBodyHelpKey = "LegalNotices_ReadOnlyBodyHelp";

    public const string DefaultWindowTitle = "Legal Notices";
    public const string DefaultSummaryFormat = "These notices are packaged with {0} for offline review.";
    public const string DefaultCloseButtonContent = "Close";
    public const string DefaultHelpFormat = "Shows the legal, privacy, and third-party notices packaged with this {0} executable.";
    public const string DefaultSummaryAutomationName = "Legal Notices summary";
    public const string DefaultSectionsAutomationName = "Legal notice sections";
    public const string DefaultSectionLinkHelpText = "Choose a legal notice section to read and copy.";
    public const string DefaultReadOnlyBodyHelpText = "Read-only legal notice text. Use Ctrl+C to copy selected text.";

    public static IReadOnlyList<string> RequiredResourceKeys { get; } =
    [
        WindowTitleKey,
        SummaryKey,
        CloseButtonKey,
        HelpKey,
        SummaryAutomationNameKey,
        SectionsAutomationNameKey,
        SectionLinkHelpKey,
        ReadOnlyBodyHelpKey,
    ];

    public static LegalNoticesDialogPresentation Create(
        string productName,
        IReadOnlyList<LegalNoticeDocument> notices,
        Func<string, string?>? resolveText = null,
        LegalNoticesTextRenderingPolicy textRenderingPolicy = LegalNoticesTextRenderingPolicy.Default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentNullException.ThrowIfNull(notices);

        string Text(string key, string fallback) =>
            (resolveText?.Invoke(key) ?? fallback).Replace("{0}", productName, StringComparison.Ordinal);

        return new LegalNoticesDialogPresentation(
            windowTitle: Text(WindowTitleKey, DefaultWindowTitle),
            notices: notices,
            summaryText: Text(SummaryKey, DefaultSummaryFormat),
            closeButtonContent: Text(CloseButtonKey, DefaultCloseButtonContent),
            helpText: Text(HelpKey, DefaultHelpFormat),
            summaryAutomationName: Text(SummaryAutomationNameKey, DefaultSummaryAutomationName),
            sectionsAutomationName: Text(SectionsAutomationNameKey, DefaultSectionsAutomationName),
            sectionLinkHelpText: Text(SectionLinkHelpKey, DefaultSectionLinkHelpText),
            readOnlyBodyHelpText: Text(ReadOnlyBodyHelpKey, DefaultReadOnlyBodyHelpText),
            textRenderingPolicy: textRenderingPolicy);
    }

    /// <summary>Overload for callers that hold plain (title, text) pairs rather than embedded documents.</summary>
    public static LegalNoticesDialogPresentation Create(
        string productName,
        IReadOnlyList<(string Title, string Text)> notices,
        Func<string, string?>? resolveText = null,
        LegalNoticesTextRenderingPolicy textRenderingPolicy = LegalNoticesTextRenderingPolicy.Default)
    {
        ArgumentNullException.ThrowIfNull(notices);

        return Create(
            productName,
            notices
                .Select(notice => new LegalNoticeDocument(notice.Title, string.Empty, notice.Text))
                .ToArray(),
            resolveText,
            textRenderingPolicy);
    }
}

/// <summary>
/// The five-document embedded legal bundle every Free-family app ships, named
/// <c>{Product}.Legal.*</c>. Apps keep their own embedding; this owns the manifest shape.
/// </summary>
public static class ProductLegalNoticeManifest
{
    public static IReadOnlyList<LegalNoticeResource> Create(string productName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);

        return Array.AsReadOnly<LegalNoticeResource>(
        [
            new("Project License", $"{productName}.Legal.ProjectLicense.txt"),
            new("Legal Notices", $"{productName}.Legal.LegalNotices.md"),
            new("Privacy Notice", $"{productName}.Legal.PrivacyNotice.md"),
            new("Third-Party Notices", $"{productName}.Legal.ThirdPartyNotices.md"),
            new("Third-Party License Texts", $"{productName}.Legal.ThirdPartyLicenses.md"),
        ]);
    }
}
