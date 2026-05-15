namespace Freexcel.Core.Model;

/// <summary>
/// A lightweight RGB color value used in conditional formatting rules.
/// Maps 1-to-1 with <see cref="CellColor"/> but kept separate to avoid
/// confusion between "cell base-style color" and "CF rule color".
/// </summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>Convert to the equivalent <see cref="CellColor"/>.</summary>
    public CellColor ToCellColor() => new(R, G, B);

    /// <summary>Create from a <see cref="CellColor"/>.</summary>
    public static RgbColor FromCellColor(CellColor c) => new(c.R, c.G, c.B);
}

/// <summary>Rule type for a conditional format.</summary>
public enum CfRuleType { CellValue, ColorScale, DataBar, AboveAverage, Top10, Formula }

/// <summary>Comparison operator used in CellValue rules.</summary>
public enum CfOperator
{
    Equal,
    NotEqual,
    GreaterThan,
    LessThanOrEqual,
    LessThan,
    GreaterThanOrEqual,
    Between,
    NotBetween
}

/// <summary>
/// A single conditional formatting rule applied to a rectangular range.
/// </summary>
public sealed class ConditionalFormat
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The range on the sheet this rule covers.</summary>
    public GridRange AppliesTo { get; set; }

    /// <summary>Lower priority number = higher precedence (Excel convention).</summary>
    public int Priority { get; set; } = 1;

    public CfRuleType RuleType { get; set; }

    // ── CellValue rule ──────────────────────────────────────────────────────

    public CfOperator Operator { get; set; }

    /// <summary>Literal value or formula text for the comparison threshold.</summary>
    public string? Value1 { get; set; }

    /// <summary>Upper bound for Between / NotBetween operators.</summary>
    public string? Value2 { get; set; }

    /// <summary>Style to apply when the rule condition is true.</summary>
    public CellStyle? FormatIfTrue { get; set; }

    // ── ColorScale rule ─────────────────────────────────────────────────────

    public RgbColor MinColor { get; set; } = new(99, 190, 123);   // green
    public RgbColor MidColor { get; set; } = new(255, 235, 132);  // yellow
    public RgbColor MaxColor { get; set; } = new(248, 105, 107);  // red

    /// <summary>When true, interpolate through MidColor at the 50 % point.</summary>
    public bool UseThreeColorScale { get; set; } = false;

    // ── DataBar rule ────────────────────────────────────────────────────────

    public RgbColor DataBarColor { get; set; } = new(99, 142, 198);

    // ── AboveAverage rule ───────────────────────────────────────────────────

    /// <summary>True = highlight cells above the range average; false = below.</summary>
    public bool AboveAverage { get; set; } = true;

    // ── Formula rule ────────────────────────────────────────────────────────

    /// <summary>Formula text (without leading =) evaluated per cell; truthy result triggers the format.</summary>
    public string? FormulaText { get; set; }

    // ── Rule control ────────────────────────────────────────────────────────

    /// <summary>When true, no lower-priority rules are evaluated for a cell that matches this rule.</summary>
    public bool StopIfTrue { get; set; }
}
