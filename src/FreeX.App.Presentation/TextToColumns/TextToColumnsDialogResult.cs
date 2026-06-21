using FreeX.Core.Model;

namespace FreeX.App.Presentation.TextToColumns;

public sealed record TextToColumnsAdvancedOptions(
    string DecimalSeparator = ".",
    string ThousandsSeparator = ",",
    bool TrailingMinusNumbers = false);

public sealed record TextToColumnsDialogResult(
    TextToColumnsDelimiterKind DelimiterKind,
    string Delimiter,
    TextToColumnsSplitMode SplitMode = TextToColumnsSplitMode.Delimited,
    IReadOnlyList<int>? FixedWidthBreakPositions = null,
    TextToColumnsTextQualifier TextQualifier = TextToColumnsTextQualifier.DoubleQuote,
    bool TreatConsecutiveDelimitersAsOne = false,
    CellAddress? Destination = null,
    IReadOnlyList<TextToColumnsColumnFormat>? ColumnFormats = null,
    TextToColumnsAdvancedOptions? AdvancedOptions = null)
{
    public string Delimiters => Delimiter;

    public char? TextQualifierChar => TextToColumnsOptions.QualifierChar(TextQualifier);
}
