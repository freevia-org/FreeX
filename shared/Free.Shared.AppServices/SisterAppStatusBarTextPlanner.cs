namespace Free.Shared.AppServices;

public static class SisterAppStatusBarChromeDefaults
{
    public const double Height = 26;
    public const double TextFontSize = 12;
    public const double SeparatorWidth = 1;
    public const byte SeparatorAlpha = 0x66;
    public const byte SeparatorRgb = 0xFF;
    public const double SeparatorHorizontalMargin = 8;
    public const double SeparatorVerticalMargin = 3;
    public const double LeftMarginLeft = 10;
    public const double LeftMarginTop = 0;
    public const double LeftMarginRight = 4;
    public const double LeftMarginBottom = 0;
}

public static class SisterAppStatusBarTextPlanner
{
    public const string SegmentSeparator = "   ";

    public static string FormatDocumentPageStatus(int currentPage, int totalPages) =>
        $"Page {AtLeastOne(currentPage)} of {AtLeastOne(totalPages)}";

    public static string FormatDocumentSectionStatus(int currentSection, int totalSections) =>
        $"Section {AtLeastOne(currentSection)} of {AtLeastOne(totalSections)}";

    public static string FormatDocumentSelectionStatus(int words, int charactersWithSpaces) =>
        $"Selection: {AtLeastZero(words)} words, {AtLeastZero(charactersWithSpaces)} characters";

    public static string FormatDocumentCountsStatus(
        int words,
        int charactersWithSpaces,
        int paragraphs) =>
        $"Words: {AtLeastZero(words)}{SegmentSeparator}Characters: {AtLeastZero(charactersWithSpaces)}{SegmentSeparator}Paragraphs: {AtLeastZero(paragraphs)}";

    public static string FormatDocumentSummaryStatus(
        int words,
        int characters,
        int paragraphs,
        string pageStatus = "",
        bool isEdited = false)
    {
        var text =
            $"{AtLeastZero(words)} words{SegmentSeparator}{AtLeastZero(characters)} characters{SegmentSeparator}{AtLeastZero(paragraphs)} paragraphs";

        if (!string.IsNullOrWhiteSpace(pageStatus))
            text = $"{pageStatus}{SegmentSeparator}{text}";

        if (isEdited)
            text = $"{text}{SegmentSeparator}\u2022 edited";

        return text;
    }

    public static string FormatDataFolderStatus(string dataFolderLabel) =>
        $"Data folder: {dataFolderLabel}";

    public static string FormatPresentationSlideStatus(
        int currentSlideIndex,
        int slideCount,
        string trailingStatus = "")
    {
        var text = slideCount <= 0
            ? "No slides"
            : $"Slide {ClampSlideIndex(currentSlideIndex, slideCount) + 1} / {slideCount}";

        return AppendTrailingStatus(text, trailingStatus);
    }

    private static string AppendTrailingStatus(string text, string trailingStatus) =>
        string.IsNullOrWhiteSpace(trailingStatus)
            ? text
            : $"{text}{SegmentSeparator}{trailingStatus}";

    private static int ClampSlideIndex(int currentSlideIndex, int slideCount) =>
        Math.Clamp(currentSlideIndex, 0, Math.Max(0, slideCount - 1));

    private static int AtLeastOne(int value) => Math.Max(1, value);

    private static int AtLeastZero(int value) => Math.Max(0, value);
}
