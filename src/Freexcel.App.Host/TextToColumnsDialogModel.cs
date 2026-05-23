using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum TextToColumnsDelimiterKind
{
    Comma,
    Semicolon,
    Tab,
    Space,
    Custom
}

public enum TextToColumnsSplitMode
{
    Delimited,
    FixedWidth
}

public enum TextToColumnsTextQualifier
{
    DoubleQuote,
    SingleQuote,
    None
}

public enum TextToColumnsColumnFormat
{
    General = 0,
    Text = 1,
    DateMDY = 2,
    DateDMY = 3,
    DateYMD = 4,
    Skip = 5,
    DateMYD = 6,
    DateDYM = 7,
    DateYDM = 8
}

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
    public char? TextQualifierChar => TextQualifier switch
    {
        TextToColumnsTextQualifier.DoubleQuote => '"',
        TextToColumnsTextQualifier.SingleQuote => '\'',
        _ => null
    };
}
