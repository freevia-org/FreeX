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

public static class HeaderFooterEditorPlanner
{
    public const string PictureToken = "&[Picture]";
    public const string LegacyPictureToken = "&G";

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
}
