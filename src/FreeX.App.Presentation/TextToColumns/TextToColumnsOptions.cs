namespace FreeX.App.Presentation.TextToColumns;

/// <summary>How the source text is divided into fields.</summary>
public enum TextToColumnsSplitMode
{
    /// <summary>Fields are separated by one or more delimiter characters.</summary>
    Delimited,

    /// <summary>Fields are sliced at fixed character positions.</summary>
    FixedWidth
}

/// <summary>
/// The well-known delimiter characters offered by the split wizard. <see cref="Custom"/> defers to a
/// caller-supplied character.
/// </summary>
public enum TextToColumnsDelimiterKind
{
    Tab,
    Semicolon,
    Comma,
    Space,
    Custom
}

/// <summary>
/// The text qualifier that brackets a field so embedded delimiters are kept literal. <see cref="None"/>
/// disables qualifier handling.
/// </summary>
public enum TextToColumnsTextQualifier
{
    DoubleQuote,
    SingleQuote,
    None
}

/// <summary>
/// The data-format hint carried for each output column. The planner never converts values; it only
/// propagates the hint so a desktop host or other renderer can apply the conversion in its own step.
/// </summary>
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

/// <summary>
/// Options describing how a single source column of cell texts should be split into multiple columns.
/// Construct via <see cref="Delimited"/> or <see cref="FixedWidth"/>.
/// </summary>
public sealed record TextToColumnsOptions
{
    private TextToColumnsOptions(TextToColumnsSplitMode splitMode)
    {
        SplitMode = splitMode;
    }

    /// <summary>How the source text is divided.</summary>
    public TextToColumnsSplitMode SplitMode { get; }

    /// <summary>
    /// The set of delimiter characters, used in <see cref="TextToColumnsSplitMode.Delimited"/> mode. Any
    /// single character in this string acts as a separator. Empty in fixed-width mode.
    /// </summary>
    public string Delimiters { get; private init; } = string.Empty;

    /// <summary>
    /// When true, runs of adjacent delimiters collapse into a single separator (so no empty fields are
    /// produced between them). Delimited mode only.
    /// </summary>
    public bool TreatConsecutiveDelimitersAsOne { get; private init; }

    /// <summary>
    /// The qualifier character that brackets a field, or null when qualifier handling is disabled. A
    /// doubled qualifier inside a qualified field is an escaped literal. Delimited mode only.
    /// </summary>
    public char? TextQualifier { get; private init; }

    /// <summary>
    /// The 1-based character offsets at which fields break, used in
    /// <see cref="TextToColumnsSplitMode.FixedWidth"/> mode. Empty in delimited mode.
    /// </summary>
    public IReadOnlyList<int> FixedWidthBreakPositions { get; private init; } = [];

    /// <summary>
    /// Per-column data-format hints, indexed by output column. Columns without an explicit hint default
    /// to <see cref="TextToColumnsColumnFormat.General"/>; a <see cref="TextToColumnsColumnFormat.Skip"/>
    /// column is excluded from the split result. Never null.
    /// </summary>
    public IReadOnlyList<TextToColumnsColumnFormat> ColumnFormats { get; private init; } = [];

    /// <summary>Builds delimited-mode options from an explicit set of delimiter characters.</summary>
    public static TextToColumnsOptions Delimited(
        string delimiters,
        bool treatConsecutiveDelimitersAsOne = false,
        char? textQualifier = '"',
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null) =>
        new(TextToColumnsSplitMode.Delimited)
        {
            Delimiters = delimiters ?? string.Empty,
            TreatConsecutiveDelimitersAsOne = treatConsecutiveDelimitersAsOne,
            TextQualifier = textQualifier,
            ColumnFormats = columnFormats ?? []
        };

    /// <summary>
    /// Builds delimited-mode options from a set of well-known delimiter kinds. The selected kinds are
    /// expanded to their characters (e.g. <see cref="TextToColumnsDelimiterKind.Tab"/> to a tab) and the
    /// qualifier kind is mapped to its character.
    /// </summary>
    public static TextToColumnsOptions Delimited(
        IEnumerable<TextToColumnsDelimiterKind> delimiterKinds,
        string? customDelimiter = null,
        bool treatConsecutiveDelimitersAsOne = false,
        TextToColumnsTextQualifier textQualifier = TextToColumnsTextQualifier.DoubleQuote,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null) =>
        Delimited(
            TextToColumnsDelimiters.Resolve(delimiterKinds, customDelimiter),
            treatConsecutiveDelimitersAsOne,
            QualifierChar(textQualifier),
            columnFormats);

    /// <summary>Builds fixed-width-mode options from a set of 1-based break positions.</summary>
    public static TextToColumnsOptions FixedWidth(
        IReadOnlyList<int> breakPositions,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null) =>
        new(TextToColumnsSplitMode.FixedWidth)
        {
            FixedWidthBreakPositions = breakPositions ?? [],
            ColumnFormats = columnFormats ?? []
        };

    /// <summary>Returns these options with the given per-column format hints attached.</summary>
    public TextToColumnsOptions WithColumnFormats(IReadOnlyList<TextToColumnsColumnFormat> columnFormats) =>
        this with { ColumnFormats = columnFormats ?? [] };

    /// <summary>Maps a qualifier kind to the character the splitter recognises, or null for none.</summary>
    public static char? QualifierChar(TextToColumnsTextQualifier qualifier) => qualifier switch
    {
        TextToColumnsTextQualifier.DoubleQuote => '"',
        TextToColumnsTextQualifier.SingleQuote => '\'',
        _ => null
    };
}
