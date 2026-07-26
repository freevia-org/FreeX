namespace FreeX.App.Presentation.Import;

/// <summary>
/// The well-known field delimiter a Get Data / From Text import can split on. <see cref="Custom"/> defers
/// to a caller-supplied character; <see cref="Detect"/> asks the planner to sniff the delimiter from the
/// sampled text.
/// </summary>
public enum ImportDelimiterKind
{
    Detect,
    Comma,
    Tab,
    Semicolon,
    Space,
    Pipe,
    Custom
}

/// <summary>
/// The text encoding a Get Data / From Text import decodes the source bytes with. <see cref="Detect"/>
/// honours a byte-order mark and otherwise tries UTF-8 then a Windows-1252 fallback (matching the existing
/// delimited-text reader); the explicit members force a single encoding.
/// </summary>
public enum ImportEncodingKind
{
    Detect,
    Utf8,
    Utf16Le,
    Utf16Be,
    Windows1252,
    Latin1
}

/// <summary>Where an import writes its parsed rows.</summary>
public enum ImportDestinationKind
{
    /// <summary>Into the active sheet, anchored at the active cell.</summary>
    CurrentSheet,

    /// <summary>Into a freshly added sheet, anchored at A1.</summary>
    NewSheet
}

/// <summary>
/// The portable, UI-free choices a Get Data / From Text dialog gathers: how to decode the source bytes,
/// what character splits fields, the quote/qualifier handling, whether runs of delimiters collapse, and
/// where the parsed rows land. The planner resolves these to a concrete delimiter character and
/// <see cref="System.Text.Encoding"/> and projects a preview; the host runs the parse and applies the
/// edits through the existing import command path.
/// </summary>
public sealed record ImportDataOptions
{
    /// <summary>How the source bytes are decoded to text.</summary>
    public ImportEncodingKind Encoding { get; init; } = ImportEncodingKind.Detect;

    /// <summary>Which character separates fields.</summary>
    public ImportDelimiterKind Delimiter { get; init; } = ImportDelimiterKind.Detect;

    /// <summary>The custom delimiter character, used only when <see cref="Delimiter"/> is
    /// <see cref="ImportDelimiterKind.Custom"/>. Null falls back to a comma.</summary>
    public char? CustomDelimiter { get; init; }

    /// <summary>
    /// When true, runs of adjacent delimiters collapse into a single separator (no empty fields between
    /// them). Mirrors the Text-to-Columns "treat consecutive delimiters as one" option.
    /// </summary>
    public bool TreatConsecutiveDelimitersAsOne { get; init; }

    /// <summary>
    /// The qualifier character that brackets a field so embedded delimiters stay literal, or null to
    /// disable qualifier handling. Defaults to the double quote.
    /// </summary>
    public char? TextQualifier { get; init; } = '"';

    /// <summary>Where the parsed rows are written.</summary>
    public ImportDestinationKind Destination { get; init; } = ImportDestinationKind.CurrentSheet;

    /// <summary>
    /// R88-io-text-import-wizard-5-4: overrides the decimal-point marker used when a value is coerced to
    /// a number, independent of the current locale -- mirroring the Text Import Wizard's Advanced dialog.
    /// Null (the default) leaves numeric coercion on its normal current-culture-then-invariant-culture
    /// resolution. Must differ from <see cref="ThousandsSeparator"/> when both are set (an identical pair
    /// is an invalid configuration, same as Text-to-Columns' <c>TextToColumnsAdvancedOptions</c>).
    /// </summary>
    public string? DecimalSeparator { get; init; }

    /// <summary>
    /// R88-io-text-import-wizard-5-4: overrides the digit-grouping marker stripped before numeric
    /// coercion, independent of the current locale -- mirroring the Text Import Wizard's Advanced dialog.
    /// Null (the default) leaves numeric coercion on its normal current-culture-then-invariant-culture
    /// resolution. Must differ from <see cref="DecimalSeparator"/> when both are set.
    /// </summary>
    public string? ThousandsSeparator { get; init; }
}
