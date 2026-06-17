namespace FreeX.App.Presentation.Consolidate;

/// <summary>
/// Discriminates the kinds of cell a source range can contribute to a consolidation: an empty cell, a
/// numeric value, or a non-numeric label (any text, or a value the host chose to surface as a label
/// string). This mirrors the desktop hosts' blank/number/non-numeric handling without depending on any
/// host or renderer value model.
/// </summary>
public enum ConsolidateCellKind
{
    /// <summary>An empty cell. Contributes nothing to the numeric values and is not counted as non-empty.</summary>
    Blank,

    /// <summary>A numeric cell. Contributes its value to the aggregation and counts as non-empty.</summary>
    Number,

    /// <summary>A non-numeric cell carrying a label string. Counts as non-empty but is not aggregated numerically.</summary>
    Label
}

/// <summary>
/// A single source cell handed to the <see cref="ConsolidatePlanner"/>: either blank, a number, or a
/// label string. Pure data — the host maps its own cell model into these and reads the planned result
/// back out. Number cells may also carry the text the host would display, which the planner uses when a
/// number sits in a label row/column for label-based consolidation (mirroring the desktop hosts, where a
/// numeric label is matched by its displayed text).
/// </summary>
public readonly struct ConsolidateCellValue : IEquatable<ConsolidateCellValue>
{
    private ConsolidateCellValue(ConsolidateCellKind kind, double number, string? label)
    {
        Kind = kind;
        Number = number;
        Label = label;
    }

    /// <summary>The kind of this cell.</summary>
    public ConsolidateCellKind Kind { get; }

    /// <summary>The numeric value when <see cref="Kind"/> is <see cref="ConsolidateCellKind.Number"/>; otherwise zero.</summary>
    public double Number { get; }

    /// <summary>
    /// The label text. For a <see cref="ConsolidateCellKind.Label"/> cell this is its text; for a
    /// <see cref="ConsolidateCellKind.Number"/> cell this is the optional display text the host supplied
    /// (used only when the number is read as a label). Null/empty otherwise.
    /// </summary>
    public string? Label { get; }

    /// <summary>True when this cell is blank.</summary>
    public bool IsBlank => Kind == ConsolidateCellKind.Blank;

    /// <summary>True when this cell carries a numeric value.</summary>
    public bool IsNumber => Kind == ConsolidateCellKind.Number;

    /// <summary>True when this cell is anything other than blank — i.e. it counts toward COUNT (non-empty).</summary>
    public bool IsNonEmpty => Kind != ConsolidateCellKind.Blank;

    /// <summary>A blank cell.</summary>
    public static ConsolidateCellValue Blank { get; } = new(ConsolidateCellKind.Blank, 0, null);

    /// <summary>Creates a numeric cell.</summary>
    public static ConsolidateCellValue FromNumber(double value) =>
        new(ConsolidateCellKind.Number, value, null);

    /// <summary>
    /// Creates a numeric cell that also carries the display text the host would show for it, used when the
    /// number is read as a label in label-based consolidation.
    /// </summary>
    public static ConsolidateCellValue FromNumber(double value, string? displayText) =>
        new(ConsolidateCellKind.Number, value, displayText);

    /// <summary>
    /// Creates a cell from label text. A null or whitespace-only string yields a <see cref="Blank"/> cell so
    /// that empty source cells the host represents as empty strings do not count as non-empty.
    /// </summary>
    public static ConsolidateCellValue FromLabel(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Blank
            : new(ConsolidateCellKind.Label, 0, text);

    /// <summary>
    /// The text used to identify this cell as a row/column label, trimmed. Numbers fall back to an
    /// invariant round-trippable rendering when no display text was supplied.
    /// </summary>
    public string LabelText() =>
        Kind switch
        {
            ConsolidateCellKind.Label => (Label ?? string.Empty).Trim(),
            ConsolidateCellKind.Number => string.IsNullOrWhiteSpace(Label)
                ? Number.ToString("G15", System.Globalization.CultureInfo.CurrentCulture)
                : Label.Trim(),
            _ => string.Empty
        };

    /// <inheritdoc />
    public bool Equals(ConsolidateCellValue other) =>
        Kind == other.Kind &&
        Number.Equals(other.Number) &&
        string.Equals(Label, other.Label, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ConsolidateCellValue other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Number, Label);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(ConsolidateCellValue left, ConsolidateCellValue right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(ConsolidateCellValue left, ConsolidateCellValue right) => !left.Equals(right);
}
