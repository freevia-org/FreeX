namespace FreeX.App.Presentation.PageLayout;

/// <summary>A stable identity for a header/footer preset, independent of its label and token text.</summary>
public enum HeaderFooterPresetId
{
    None,
    PageNumber,
    PageNumberOfPages,
    Sheet,
    Book,
    BookXlsx,
    BookXlsxSheet,
    ConfidentialPage,
    DatePage,
    SheetName,
    FileName,
    FilePath,
    Date,
    Time
}

/// <summary>A renderer-neutral header/footer choice. Shells localize the appropriate resource key.</summary>
public sealed record HeaderFooterPresetChoice(
    HeaderFooterPresetId Id,
    string LabelResourceKey,
    string EditorLabelResourceKey)
{
    public string Value => HeaderFooterPresetCatalog.ValueFor(Id);
}

/// <summary>
/// Canonical header/footer preset identities, token values, ordering, and localization keys.
/// The editor key preserves the dedicated WPF editor wording while Page Setup uses the primary key.
/// </summary>
public static class HeaderFooterPresetCatalog
{
    public static IReadOnlyList<HeaderFooterPresetChoice> HeaderChoices { get; } =
    [
        Choice(HeaderFooterPresetId.None, "PageSetup_None", "HeaderFooter_None"),
        Choice(HeaderFooterPresetId.PageNumber, "PageSetup_Page1", "HeaderFooter_Page1"),
        Choice(HeaderFooterPresetId.PageNumberOfPages, "PageSetup_Page1Of", "HeaderFooter_Page1Of"),
        Choice(HeaderFooterPresetId.Sheet, "PageSetup_Sheet1", "HeaderFooter_Sheet1"),
        Choice(HeaderFooterPresetId.Book, "PageSetup_Book1", "HeaderFooter_Book1"),
        Choice(HeaderFooterPresetId.BookXlsx, "PageSetup_Book1Xlsx", "HeaderFooter_Book1Xlsx"),
        Choice(HeaderFooterPresetId.BookXlsxSheet, "PageSetup_Book1XlsxSheet1", "HeaderFooter_Book1XlsxSheet1"),
        Choice(HeaderFooterPresetId.ConfidentialPage, "PageSetup_ConfidentialPage1", "HeaderFooter_ConfidentialPage1"),
        Choice(HeaderFooterPresetId.DatePage, "PageSetup_DatePage1", "HeaderFooter_DatePage1"),
        Choice(HeaderFooterPresetId.SheetName, "PageSetup_SheetName", "HeaderFooter_SheetName"),
        Choice(HeaderFooterPresetId.FileName, "PageSetup_FileName", "HeaderFooter_FileName"),
        Choice(HeaderFooterPresetId.FilePath, "PageSetup_FilePath", "HeaderFooter_FilePath"),
    ];

    public static IReadOnlyList<HeaderFooterPresetChoice> FooterChoices { get; } =
    [
        Choice(HeaderFooterPresetId.None, "PageSetup_None", "HeaderFooter_None"),
        Choice(HeaderFooterPresetId.PageNumber, "PageSetup_Page1", "HeaderFooter_Page1"),
        Choice(HeaderFooterPresetId.PageNumberOfPages, "PageSetup_Page1Of", "HeaderFooter_Page1Of"),
        Choice(HeaderFooterPresetId.Sheet, "PageSetup_Sheet1", "HeaderFooter_Sheet1"),
        Choice(HeaderFooterPresetId.Book, "PageSetup_Book1", "HeaderFooter_Book1"),
        Choice(HeaderFooterPresetId.BookXlsx, "PageSetup_Book1Xlsx", "HeaderFooter_Book1Xlsx"),
        Choice(HeaderFooterPresetId.BookXlsxSheet, "PageSetup_Book1XlsxSheet1", "HeaderFooter_Book1XlsxSheet1"),
        Choice(HeaderFooterPresetId.Date, "PageSetup_Date", "HeaderFooter_Date"),
        Choice(HeaderFooterPresetId.Time, "PageSetup_Time", "HeaderFooter_Time"),
        Choice(HeaderFooterPresetId.DatePage, "PageSetup_DatePage1", "HeaderFooter_DatePage1"),
        Choice(HeaderFooterPresetId.FilePath, "PageSetup_FilePath", "HeaderFooter_FilePath"),
        Choice(HeaderFooterPresetId.FileName, "PageSetup_FileName", "HeaderFooter_FileName"),
    ];

    public static IReadOnlyList<HeaderFooterPresetChoice> CompactChoices { get; } =
    [
        CompactChoice(HeaderFooterPresetId.None, "PageSetup_None"),
        CompactChoice(HeaderFooterPresetId.PageNumber, "PageSetup_PresetPage"),
        CompactChoice(HeaderFooterPresetId.PageNumberOfPages, "PageSetup_PresetPageOf"),
        CompactChoice(HeaderFooterPresetId.SheetName, "PageSetup_PresetSheetName"),
        CompactChoice(HeaderFooterPresetId.FileName, "PageSetup_PresetFileName"),
        CompactChoice(HeaderFooterPresetId.BookXlsxSheet, "PageSetup_PresetFileSheet"),
        CompactChoice(HeaderFooterPresetId.Date, "PageSetup_PresetDate"),
        CompactChoice(HeaderFooterPresetId.Time, "PageSetup_PresetTime"),
        CompactChoice(HeaderFooterPresetId.DatePage, "PageSetup_PresetDatePage"),
        CompactChoice(HeaderFooterPresetId.ConfidentialPage, "PageSetup_PresetConfidential"),
        CompactChoice(HeaderFooterPresetId.FilePath, "PageSetup_PresetFilePath"),
    ];

    public static string ValueFor(HeaderFooterPresetId id) => id switch
    {
        HeaderFooterPresetId.None => "",
        HeaderFooterPresetId.PageNumber => "&[Page]",
        HeaderFooterPresetId.PageNumberOfPages => "Page &[Page] of &[Pages]",
        HeaderFooterPresetId.Sheet or HeaderFooterPresetId.SheetName => "&[Tab]",
        HeaderFooterPresetId.Book or HeaderFooterPresetId.BookXlsx or HeaderFooterPresetId.FileName => "&[File]",
        HeaderFooterPresetId.BookXlsxSheet => "&[File], &[Tab]",
        HeaderFooterPresetId.ConfidentialPage => "Confidential, Page &[Page]",
        HeaderFooterPresetId.DatePage => "&[Date], Page &[Page]",
        HeaderFooterPresetId.FilePath => "&[Path]&[File]",
        HeaderFooterPresetId.Date => "&[Date]",
        HeaderFooterPresetId.Time => "&[Time]",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
    };

    private static HeaderFooterPresetChoice Choice(
        HeaderFooterPresetId id,
        string labelResourceKey,
        string editorLabelResourceKey) =>
        new(id, labelResourceKey, editorLabelResourceKey);

    private static HeaderFooterPresetChoice CompactChoice(HeaderFooterPresetId id, string labelResourceKey) =>
        new(id, labelResourceKey, labelResourceKey);
}
