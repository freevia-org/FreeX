using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public enum HeaderFooterEditorScope
{
    Header,
    Footer,
    FirstPageHeader,
    FirstPageFooter,
    EvenPageHeader,
    EvenPageFooter
}

public enum HeaderFooterEditorSection
{
    Left,
    Center,
    Right
}

public readonly record struct HeaderFooterEditorTarget(
    HeaderFooterEditorScope Scope,
    HeaderFooterEditorSection Section);

public sealed record HeaderFooterEditorState(
    WorksheetHeaderFooter Header,
    WorksheetHeaderFooter Footer,
    WorksheetHeaderFooter FirstPageHeader,
    WorksheetHeaderFooter FirstPageFooter,
    WorksheetHeaderFooter EvenPageHeader,
    WorksheetHeaderFooter EvenPageFooter,
    WorksheetHeaderFooterPictureSet HeaderPictures,
    WorksheetHeaderFooterPictureSet FooterPictures,
    WorksheetHeaderFooterPictureSet FirstPageHeaderPictures,
    WorksheetHeaderFooterPictureSet FirstPageFooterPictures,
    WorksheetHeaderFooterPictureSet EvenPageHeaderPictures,
    WorksheetHeaderFooterPictureSet EvenPageFooterPictures,
    bool DifferentFirstPage,
    bool DifferentOddEvenPages,
    bool ScaleWithDocument,
    bool AlignWithMargins)
{
    public HeaderFooterEditorState()
        : this(
            new("", "", ""),
            new("", "", ""),
            new("", "", ""),
            new("", "", ""),
            new("", "", ""),
            new("", "", ""),
            WorksheetHeaderFooterPictureSet.Empty,
            WorksheetHeaderFooterPictureSet.Empty,
            WorksheetHeaderFooterPictureSet.Empty,
            WorksheetHeaderFooterPictureSet.Empty,
            WorksheetHeaderFooterPictureSet.Empty,
            WorksheetHeaderFooterPictureSet.Empty,
            DifferentFirstPage: false,
            DifferentOddEvenPages: false,
            ScaleWithDocument: true,
            AlignWithMargins: true)
    {
    }

    public static HeaderFooterEditorState Empty { get; } = new(
        new("", "", ""),
        new("", "", ""),
        new("", "", ""),
        new("", "", ""),
        new("", "", ""),
        new("", "", ""),
        WorksheetHeaderFooterPictureSet.Empty,
        WorksheetHeaderFooterPictureSet.Empty,
        WorksheetHeaderFooterPictureSet.Empty,
        WorksheetHeaderFooterPictureSet.Empty,
        WorksheetHeaderFooterPictureSet.Empty,
        WorksheetHeaderFooterPictureSet.Empty,
        DifferentFirstPage: false,
        DifferentOddEvenPages: false,
        ScaleWithDocument: true,
        AlignWithMargins: true);

    public WorksheetHeaderFooter GetValue(HeaderFooterEditorScope scope) =>
        scope switch
        {
            HeaderFooterEditorScope.Footer => Footer,
            HeaderFooterEditorScope.FirstPageHeader => FirstPageHeader,
            HeaderFooterEditorScope.FirstPageFooter => FirstPageFooter,
            HeaderFooterEditorScope.EvenPageHeader => EvenPageHeader,
            HeaderFooterEditorScope.EvenPageFooter => EvenPageFooter,
            _ => Header
        };

    public HeaderFooterEditorState WithValue(
        HeaderFooterEditorScope scope,
        WorksheetHeaderFooter value) =>
        scope switch
        {
            HeaderFooterEditorScope.Footer => this with { Footer = value },
            HeaderFooterEditorScope.FirstPageHeader => this with { FirstPageHeader = value },
            HeaderFooterEditorScope.FirstPageFooter => this with { FirstPageFooter = value },
            HeaderFooterEditorScope.EvenPageHeader => this with { EvenPageHeader = value },
            HeaderFooterEditorScope.EvenPageFooter => this with { EvenPageFooter = value },
            _ => this with { Header = value }
        };

    public WorksheetHeaderFooterPictureSet GetPictures(HeaderFooterEditorScope scope) =>
        scope switch
        {
            HeaderFooterEditorScope.Footer => FooterPictures,
            HeaderFooterEditorScope.FirstPageHeader => FirstPageHeaderPictures,
            HeaderFooterEditorScope.FirstPageFooter => FirstPageFooterPictures,
            HeaderFooterEditorScope.EvenPageHeader => EvenPageHeaderPictures,
            HeaderFooterEditorScope.EvenPageFooter => EvenPageFooterPictures,
            _ => HeaderPictures
        };

    public HeaderFooterEditorState WithPictures(
        HeaderFooterEditorScope scope,
        WorksheetHeaderFooterPictureSet pictures) =>
        scope switch
        {
            HeaderFooterEditorScope.Footer => this with { FooterPictures = pictures },
            HeaderFooterEditorScope.FirstPageHeader => this with { FirstPageHeaderPictures = pictures },
            HeaderFooterEditorScope.FirstPageFooter => this with { FirstPageFooterPictures = pictures },
            HeaderFooterEditorScope.EvenPageHeader => this with { EvenPageHeaderPictures = pictures },
            HeaderFooterEditorScope.EvenPageFooter => this with { EvenPageFooterPictures = pictures },
            _ => this with { HeaderPictures = pictures }
        };

    public static HeaderFooterEditorState FromSheet(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        return new(
            sheet.PageHeader,
            sheet.PageFooter,
            sheet.FirstPageHeader,
            sheet.FirstPageFooter,
            sheet.EvenPageHeader,
            sheet.EvenPageFooter,
            sheet.PageHeaderPictures.DeepClone(),
            sheet.PageFooterPictures.DeepClone(),
            sheet.FirstPageHeaderPictures.DeepClone(),
            sheet.FirstPageFooterPictures.DeepClone(),
            sheet.EvenPageHeaderPictures.DeepClone(),
            sheet.EvenPageFooterPictures.DeepClone(),
            sheet.DifferentFirstPageHeaderFooter,
            sheet.DifferentOddEvenHeaderFooter,
            sheet.HeaderFooterScaleWithDocument,
            sheet.HeaderFooterAlignWithMargins);
    }

    public static HeaderFooterEditorState FromPageSetupFields(PageSetupDialogFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return fields.HeaderFooter.DeepClone();
    }

    public PageSetupDialogFields ApplyTo(PageSetupDialogFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        return fields with
        {
            HeaderFooter = DeepClone(),
        };
    }

    public HeaderFooterEditorState DeepClone() => this with
    {
        HeaderPictures = HeaderPictures.DeepClone(),
        FooterPictures = FooterPictures.DeepClone(),
        FirstPageHeaderPictures = FirstPageHeaderPictures.DeepClone(),
        FirstPageFooterPictures = FirstPageFooterPictures.DeepClone(),
        EvenPageHeaderPictures = EvenPageHeaderPictures.DeepClone(),
        EvenPageFooterPictures = EvenPageFooterPictures.DeepClone(),
    };

    public HeaderFooterEditorState PrunePicturesWithoutTokens() => this with
    {
        HeaderPictures = HeaderFooterEditorPlanner.PrunePicturesWithoutTokens(Header, HeaderPictures),
        FooterPictures = HeaderFooterEditorPlanner.PrunePicturesWithoutTokens(Footer, FooterPictures),
        FirstPageHeaderPictures = HeaderFooterEditorPlanner.PrunePicturesWithoutTokens(FirstPageHeader, FirstPageHeaderPictures),
        FirstPageFooterPictures = HeaderFooterEditorPlanner.PrunePicturesWithoutTokens(FirstPageFooter, FirstPageFooterPictures),
        EvenPageHeaderPictures = HeaderFooterEditorPlanner.PrunePicturesWithoutTokens(EvenPageHeader, EvenPageHeaderPictures),
        EvenPageFooterPictures = HeaderFooterEditorPlanner.PrunePicturesWithoutTokens(EvenPageFooter, EvenPageFooterPictures),
    };
}

public static class HeaderFooterEditorPlanner
{
    public const string PictureToken = "&[Picture]";
    public const string LegacyPictureToken = "&G";

    private static readonly HeaderFooterEditorScope[] EditorScopes =
    [
        HeaderFooterEditorScope.Header,
        HeaderFooterEditorScope.Footer,
        HeaderFooterEditorScope.FirstPageHeader,
        HeaderFooterEditorScope.FirstPageFooter,
        HeaderFooterEditorScope.EvenPageHeader,
        HeaderFooterEditorScope.EvenPageFooter,
    ];

    public static string InsertToken(string? text, int caretIndex, string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        var source = text ?? string.Empty;
        var boundedCaretIndex = Math.Clamp(caretIndex, 0, source.Length);
        return source.Insert(boundedCaretIndex, token);
    }

    public static WorksheetHeaderFooter ApplyCenterPreset(WorksheetHeaderFooter value, string preset) =>
        value with { Center = preset };

    public static bool ContainsPictureToken(string? text) =>
        !string.IsNullOrEmpty(text) &&
        (text.Contains(PictureToken, StringComparison.OrdinalIgnoreCase) ||
         text.Contains(LegacyPictureToken, StringComparison.OrdinalIgnoreCase));

    public static WorksheetHeaderFooterPictureSet PrunePicturesWithoutTokens(
        WorksheetHeaderFooter text,
        WorksheetHeaderFooterPictureSet pictures) =>
        new(
            ContainsPictureToken(text.Left) ? pictures.Left : null,
            ContainsPictureToken(text.Center) ? pictures.Center : null,
            ContainsPictureToken(text.Right) ? pictures.Right : null);

    public static WorksheetHeaderFooterPicture? GetPicture(
        WorksheetHeaderFooterPictureSet pictures,
        HeaderFooterEditorSection section) =>
        section switch
        {
            HeaderFooterEditorSection.Left => pictures.Left,
            HeaderFooterEditorSection.Center => pictures.Center,
            HeaderFooterEditorSection.Right => pictures.Right,
            _ => null
        };

    public static WorksheetHeaderFooterPictureSet SetPicture(
        WorksheetHeaderFooterPictureSet pictures,
        HeaderFooterEditorSection section,
        WorksheetHeaderFooterPicture picture) =>
        section switch
        {
            HeaderFooterEditorSection.Left => pictures with { Left = picture },
            HeaderFooterEditorSection.Center => pictures with { Center = picture },
            HeaderFooterEditorSection.Right => pictures with { Right = picture },
            _ => pictures
        };

    public static bool IsScopeEnabled(
        HeaderFooterEditorScope scope,
        bool differentFirstPage,
        bool differentOddEvenPages) =>
        scope switch
        {
            HeaderFooterEditorScope.FirstPageHeader or HeaderFooterEditorScope.FirstPageFooter => differentFirstPage,
            HeaderFooterEditorScope.EvenPageHeader or HeaderFooterEditorScope.EvenPageFooter => differentOddEvenPages,
            _ => true
        };

    public static bool IsTargetEnabled(
        HeaderFooterEditorTarget target,
        bool differentFirstPage,
        bool differentOddEvenPages) =>
        IsScopeEnabled(target.Scope, differentFirstPage, differentOddEvenPages);

    public static HeaderFooterEditorTarget CoerceToEnabledTarget(
        HeaderFooterEditorTarget target,
        bool differentFirstPage,
        bool differentOddEvenPages) =>
        IsTargetEnabled(target, differentFirstPage, differentOddEvenPages)
            ? target
            : target.Scope is HeaderFooterEditorScope.FirstPageFooter or HeaderFooterEditorScope.EvenPageFooter
                ? new HeaderFooterEditorTarget(HeaderFooterEditorScope.Footer, HeaderFooterEditorSection.Center)
                : new HeaderFooterEditorTarget(HeaderFooterEditorScope.Header, HeaderFooterEditorSection.Center);

    public static HeaderFooterEditorTarget CoerceToEnabledTargetForTab(
        HeaderFooterEditorTarget target,
        HeaderFooterEditorScope selectedTabScope,
        bool differentFirstPage,
        bool differentOddEvenPages)
    {
        var selectedTabIsFooter = selectedTabScope == HeaderFooterEditorScope.Footer;
        if (IsFooterScope(target.Scope) != selectedTabIsFooter)
        {
            target = new HeaderFooterEditorTarget(
                selectedTabIsFooter ? HeaderFooterEditorScope.Footer : HeaderFooterEditorScope.Header,
                HeaderFooterEditorSection.Center);
        }

        return CoerceToEnabledTarget(target, differentFirstPage, differentOddEvenPages);
    }

    public static HeaderFooterEditorState BuildResult(
        HeaderFooterEditorState state,
        Func<HeaderFooterEditorScope, WorksheetHeaderFooter> valueProvider,
        bool differentFirstPage,
        bool differentOddEvenPages,
        bool scaleWithDocument,
        bool alignWithMargins)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(valueProvider);

        foreach (var scope in EditorScopes)
            state = state.WithValue(scope, valueProvider(scope));

        return (state with
        {
            DifferentFirstPage = differentFirstPage,
            DifferentOddEvenPages = differentOddEvenPages,
            ScaleWithDocument = scaleWithDocument,
            AlignWithMargins = alignWithMargins,
        }).PrunePicturesWithoutTokens();
    }

    public static string ScopeLabelResourceKey(HeaderFooterEditorScope scope) =>
        scope switch
        {
            HeaderFooterEditorScope.Header => "HeaderFooter_Header",
            HeaderFooterEditorScope.Footer => "HeaderFooter_Footer",
            HeaderFooterEditorScope.FirstPageHeader => "HeaderFooter_FirstPageHeader",
            HeaderFooterEditorScope.FirstPageFooter => "HeaderFooter_FirstPageFooter",
            HeaderFooterEditorScope.EvenPageHeader => "HeaderFooter_EvenPageHeader",
            HeaderFooterEditorScope.EvenPageFooter => "HeaderFooter_EvenPageFooter",
            _ => string.Empty
        };

    public static string SectionLabelResourceKey(HeaderFooterEditorSection section) =>
        section switch
        {
            HeaderFooterEditorSection.Left => "HeaderFooterPicture_LeftSection",
            HeaderFooterEditorSection.Center => "HeaderFooterPicture_CenterSection",
            HeaderFooterEditorSection.Right => "HeaderFooterPicture_RightSection",
            _ => "HeaderFooterPicture_CurrentSection"
        };

    /// <summary>
    /// Resource key for the format string that joins an already-localized scope label
    /// (e.g. "Header") with an already-localized section label (e.g. "left section") into
    /// a single composite target label. Each locale's resx can define its own value for this
    /// key with the two placeholders reordered (or otherwise composed) so translators control
    /// word order instead of the caller hardcoding scope-then-section concatenation.
    /// </summary>
    public const string TargetLabelFormatResourceKey = "HeaderFooterPicture_TargetLabelFormat";

    /// <summary>
    /// Combines an already-localized scope label and section label into the single composite
    /// label substituted into the picture-target tooltip/status format strings. The join itself
    /// is resolved through <paramref name="formatResource"/> against <see cref="TargetLabelFormatResourceKey"/>
    /// so each locale controls word order rather than the caller hardcoding a fixed
    /// scope-then-section concatenation.
    /// </summary>
    public static string ComposeTargetLabel(
        string scopeLabel,
        string sectionLabel,
        Func<string, object?[], string> formatResource)
    {
        ArgumentNullException.ThrowIfNull(formatResource);
        return string.IsNullOrWhiteSpace(scopeLabel)
            ? sectionLabel
            : formatResource(TargetLabelFormatResourceKey, [scopeLabel, sectionLabel]);
    }

    public static string EditorFieldLabelResourceKey(HeaderFooterEditorTarget target) =>
        target switch
        {
            { Scope: HeaderFooterEditorScope.Header, Section: HeaderFooterEditorSection.Left } => "HeaderFooter_HeaderLeft",
            { Scope: HeaderFooterEditorScope.Header, Section: HeaderFooterEditorSection.Center } => "HeaderFooter_HeaderCenter",
            { Scope: HeaderFooterEditorScope.Header, Section: HeaderFooterEditorSection.Right } => "HeaderFooter_HeaderRight",
            { Scope: HeaderFooterEditorScope.Footer, Section: HeaderFooterEditorSection.Left } => "HeaderFooter_FooterLeft",
            { Scope: HeaderFooterEditorScope.Footer, Section: HeaderFooterEditorSection.Center } => "HeaderFooter_FooterCenter",
            { Scope: HeaderFooterEditorScope.Footer, Section: HeaderFooterEditorSection.Right } => "HeaderFooter_FooterRight",
            { Scope: HeaderFooterEditorScope.FirstPageHeader, Section: HeaderFooterEditorSection.Left } => "HeaderFooter_FirstHeaderLeft",
            { Scope: HeaderFooterEditorScope.FirstPageHeader, Section: HeaderFooterEditorSection.Center } => "HeaderFooter_FirstHeaderCenter",
            { Scope: HeaderFooterEditorScope.FirstPageHeader, Section: HeaderFooterEditorSection.Right } => "HeaderFooter_FirstHeaderRight",
            { Scope: HeaderFooterEditorScope.FirstPageFooter, Section: HeaderFooterEditorSection.Left } => "HeaderFooter_FirstFooterLeft",
            { Scope: HeaderFooterEditorScope.FirstPageFooter, Section: HeaderFooterEditorSection.Center } => "HeaderFooter_FirstFooterCenter",
            { Scope: HeaderFooterEditorScope.FirstPageFooter, Section: HeaderFooterEditorSection.Right } => "HeaderFooter_FirstFooterRight",
            { Scope: HeaderFooterEditorScope.EvenPageHeader, Section: HeaderFooterEditorSection.Left } => "HeaderFooter_EvenHeaderLeft",
            { Scope: HeaderFooterEditorScope.EvenPageHeader, Section: HeaderFooterEditorSection.Center } => "HeaderFooter_EvenHeaderCenter",
            { Scope: HeaderFooterEditorScope.EvenPageHeader, Section: HeaderFooterEditorSection.Right } => "HeaderFooter_EvenHeaderRight",
            { Scope: HeaderFooterEditorScope.EvenPageFooter, Section: HeaderFooterEditorSection.Left } => "HeaderFooter_EvenFooterLeft",
            { Scope: HeaderFooterEditorScope.EvenPageFooter, Section: HeaderFooterEditorSection.Center } => "HeaderFooter_EvenFooterCenter",
            { Scope: HeaderFooterEditorScope.EvenPageFooter, Section: HeaderFooterEditorSection.Right } => "HeaderFooter_EvenFooterRight",
            _ => SectionLabelResourceKey(target.Section)
        };

    private static bool IsFooterScope(HeaderFooterEditorScope scope) =>
        scope is HeaderFooterEditorScope.Footer
            or HeaderFooterEditorScope.FirstPageFooter
            or HeaderFooterEditorScope.EvenPageFooter;
}
