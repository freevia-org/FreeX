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
    General,
    Text,
    Skip
}

public sealed record TextToColumnsDialogResult(
    TextToColumnsDelimiterKind DelimiterKind,
    string Delimiter,
    TextToColumnsSplitMode SplitMode = TextToColumnsSplitMode.Delimited,
    IReadOnlyList<int>? FixedWidthBreakPositions = null,
    TextToColumnsTextQualifier TextQualifier = TextToColumnsTextQualifier.DoubleQuote,
    bool TreatConsecutiveDelimitersAsOne = false,
    CellAddress? Destination = null,
    IReadOnlyList<TextToColumnsColumnFormat>? ColumnFormats = null)
{
    public string Delimiters => Delimiter;
    public char? TextQualifierChar => TextQualifier switch
    {
        TextToColumnsTextQualifier.DoubleQuote => '"',
        TextToColumnsTextQualifier.SingleQuote => '\'',
        _ => null
    };
}
