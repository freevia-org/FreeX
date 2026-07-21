namespace FreeX.Core.Model;

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

/// <summary>
/// Carries the original OOXML <c>theme</c> index and optional <c>tint</c> for a colorScale stop color
/// that was expressed as a theme reference in the source file. Stored alongside the resolved
/// <see cref="RgbColor"/> so the writer can round-trip the raw theme attributes without flattening
/// to sRGB.
/// </summary>
/// <param name="ThemeIndex">OOXML theme index (0–11), matching the numeric @theme attribute.</param>
/// <param name="Tint">Tint value in [−1, 1]. 0 means no tint.</param>
public readonly record struct CfColorStopSource(int ThemeIndex, double Tint = 0);

/// <summary>Rule type for a conditional format.</summary>
public enum CfRuleType
{
    CellValue,
    ColorScale,
    DataBar,
    AboveAverage,
    Top10,
    Formula,
    IconSet,
    UniqueValues,
    DuplicateValues,
    ContainsText,
    NotContainsText,
    BeginsWith,
    EndsWith,
    DateOccurring,
    Blanks,
    NoBlanks,
    Errors,
    NoErrors
}

public enum CfThresholdType
{
    Min,
    Max,
    Number,
    Percent,
    Percentile,
    Formula
}

public sealed record CfThresholdModel(CfThresholdType Type, string? Value = null, bool? GreaterThanOrEqual = null);

/// <summary>
/// Per-threshold icon override. <see cref="IconSet"/> = "NoIcons" suppresses the icon for that bucket.
/// </summary>
public sealed record CfIconOverride(string IconSet, int IconId);

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

    /// <summary>
    /// Additional non-contiguous ranges from the same Excel <c>sqref</c> token list,
    /// beyond the first range stored in <see cref="AppliesTo"/>.
    /// When the original sqref contains a single range this is <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<GridRange>? AdditionalRanges { get; set; }

    /// <summary>
    /// Returns all ranges covered by this rule: <see cref="AppliesTo"/> followed by any
    /// <see cref="AdditionalRanges"/>. Use this when testing whether a cell falls within the rule.
    /// </summary>
    public IEnumerable<GridRange> AllRanges =>
        AdditionalRanges is null
            ? [AppliesTo]
            : [AppliesTo, .. AdditionalRanges];

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

    /// <summary>
    /// When the min stop color originated from a workbook theme reference in the source file,
    /// this carries the raw theme index and tint so the writer can round-trip the original
    /// attributes instead of flattening to sRGB.
    /// </summary>
    public CfColorStopSource? MinColorSource { get; set; }

    /// <summary>Theme source for the mid stop color. <see langword="null"/> when the color was sRGB or indexed.</summary>
    public CfColorStopSource? MidColorSource { get; set; }

    /// <summary>Theme source for the max stop color. <see langword="null"/> when the color was sRGB or indexed.</summary>
    public CfColorStopSource? MaxColorSource { get; set; }

    /// <summary>When true, interpolate through MidColor at the 50 % point.</summary>
    public bool UseThreeColorScale { get; set; } = false;
    public CfThresholdType MinThresholdType { get; set; } = CfThresholdType.Min;
    public string? MinThresholdValue { get; set; }
    public bool? MinThresholdGreaterThanOrEqual { get; set; }
    public CfThresholdType MidThresholdType { get; set; } = CfThresholdType.Percentile;
    public string? MidThresholdValue { get; set; } = "50";
    public bool? MidThresholdGreaterThanOrEqual { get; set; }
    public CfThresholdType MaxThresholdType { get; set; } = CfThresholdType.Max;
    public string? MaxThresholdValue { get; set; }
    public bool? MaxThresholdGreaterThanOrEqual { get; set; }

    // ── DataBar rule ────────────────────────────────────────────────────────

    public RgbColor DataBarColor { get; set; } = new(99, 142, 198);

    /// <summary>
    /// When the dataBar fill color originated from a workbook theme reference in the source file,
    /// this carries the raw theme index and tint so the writer can round-trip the original
    /// attributes instead of flattening to sRGB.
    /// </summary>
    public CfColorStopSource? DataBarColorSource { get; set; }
    public CfThresholdType DataBarMinThresholdType { get; set; } = CfThresholdType.Min;
    public string? DataBarMinThresholdValue { get; set; }
    public CfThresholdType DataBarMaxThresholdType { get; set; } = CfThresholdType.Max;
    public string? DataBarMaxThresholdValue { get; set; }
    public bool DataBarShowValue { get; set; } = true;
    public int? DataBarMinLength { get; set; }
    public int? DataBarMaxLength { get; set; }
    /// <summary>When false the bar uses a solid fill instead of the default gradient.</summary>
    public bool DataBarGradient { get; set; } = true;
    public bool DataBarBorder { get; set; }
    /// <summary>Explicit border color for the positive (or only) bar when <see cref="DataBarBorder"/> is true.</summary>
    public RgbColor? DataBarBorderColor { get; set; }
    public string? DataBarAxisPosition { get; set; }
    public RgbColor? DataBarAxisColor { get; set; }
    public RgbColor? DataBarNegativeFillColor { get; set; }
    public RgbColor? DataBarNegativeBorderColor { get; set; }
    /// <summary>
    /// Maps to the x14 <c>dataBar/@negativeBarColorSameAsPositive</c> attribute: true when the user
    /// explicitly checked "Same as Positive Value" for the negative fill in Excel's Negative Value
    /// and Axis dialog. When true, <see cref="DataBarNegativeFillColor"/> is redundant (Excel omits
    /// the <c>negativeFillColor</c> child in this case) and the negative bar should use
    /// <see cref="DataBarColor"/> instead.
    /// </summary>
    public bool DataBarNegativeFillSameAsPositive { get; set; }
    /// <summary>
    /// Maps to the x14 <c>dataBar/@negativeBarBorderColorSameAsPositive</c> attribute: true when the
    /// user explicitly checked "Same as Positive Value" for the negative border in Excel's Negative
    /// Value and Axis dialog. When true, <see cref="DataBarNegativeBorderColor"/> is redundant and the
    /// negative bar's border should use <see cref="DataBarBorderColor"/> instead.
    /// </summary>
    public bool DataBarNegativeBorderSameAsPositive { get; set; }
    /// <summary>
    /// Raw value of the x14 <c>dataBar/@direction</c> attribute ("context" or "rightToLeft"), preserved
    /// verbatim for XLSX round-trip fidelity. Null means the attribute was absent (Excel's default,
    /// left-to-right growth). Rendering does not currently consult this value.
    /// </summary>
    public string? DataBarDirection { get; set; }

    // ── AboveAverage rule ───────────────────────────────────────────────────

    /// <summary>True = highlight cells above the range average; false = below.</summary>
    public bool AboveAverage { get; set; } = true;

    /// <summary>
    /// True = the comparison is inclusive of the average/stdDev band boundary itself
    /// (Excel's "Above or equal to Average" / "Below or equal to Average" and the
    /// std-dev variants). Maps to the OOXML <c>equalAverage</c> cfRule attribute.
    /// </summary>
    public bool EqualAverage { get; set; }

    /// <summary>
    /// When set, this is an "N standard deviations above/below average" rule instead of a
    /// plain above/below-average rule: the threshold band is <c>mean ± StdDevCount * stdDev</c>
    /// over the applied range. Maps to the OOXML <c>stdDev</c> cfRule attribute. Null means
    /// the rule is a plain above/below-average comparison against the mean.
    /// </summary>
    public int? StdDevCount { get; set; }

    // ── Formula rule ────────────────────────────────────────────────────────

    /// <summary>Formula text (without leading =) evaluated per cell; truthy result triggers the format.</summary>
    public string? FormulaText { get; set; }

    public string? IconSetStyle { get; set; }
    public bool IconSetShowValue { get; set; } = true;
    public bool IconSetReverse { get; set; }
    public List<CfThresholdModel> IconSetThresholds { get; } = [];

    /// <summary>
    /// Per-bucket icon overrides, one entry per icon position (lowest to highest).
    /// When non-empty and the count matches the icon set size, each override specifies
    /// the exact icon set and index to display for that bucket.
    /// </summary>
    public List<CfIconOverride> IconOverrides { get; } = [];

    public int TopBottomRank { get; set; } = 10;
    public bool TopBottomPercent { get; set; }
    public string? TextRuleText { get; set; }
    public string? DateOccurringPeriod { get; set; }

    // ── Rule control ────────────────────────────────────────────────────────

    /// <summary>When true, no lower-priority rules are evaluated for a cell that matches this rule.</summary>
    public bool StopIfTrue { get; set; }

    /// <summary>Native cfRule attributes not modeled by FreeX, retained for XLSX round-trip fidelity.</summary>
    public IReadOnlyDictionary<string, string>? NativeAttributes { get; set; }

    /// <summary>Native cfRule child elements not modeled by FreeX, retained for XLSX round-trip fidelity.</summary>
    public IReadOnlyList<string>? NativeChildXmls { get; set; }

    /// <summary>Native attributes on the modeled cfRule payload element, retained for XLSX round-trip fidelity.</summary>
    public IReadOnlyDictionary<string, string>? NativePayloadAttributes { get; set; }

    /// <summary>Native child elements on the modeled cfRule payload element, retained for XLSX round-trip fidelity.</summary>
    public IReadOnlyList<string>? NativePayloadChildXmls { get; set; }

    /// <summary>Native conditionalFormatting attributes not modeled by FreeX, retained for XLSX round-trip fidelity.</summary>
    public IReadOnlyDictionary<string, string>? NativeContainerAttributes { get; set; }

    /// <summary>Native conditionalFormatting child elements not modeled by FreeX, retained for XLSX round-trip fidelity.</summary>
    public IReadOnlyList<string>? NativeContainerChildXmls { get; set; }

    /// <summary>
    /// Returns a deep copy of this rule. All fields are copied; mutable collections
    /// (<see cref="IconSetThresholds"/>, <see cref="IconOverrides"/>) are given new independent
    /// instances so mutating the clone never affects the original.
    /// <para>
    /// When <paramref name="newId"/> is supplied the clone receives that id (and the
    /// X14 extended-id native metadata is stripped so it is not duplicated). When it is
    /// omitted the clone keeps the same id as the source.
    /// </para>
    /// </summary>
    public ConditionalFormat Clone(Guid? newId = null)
    {
        var clone = new ConditionalFormat
        {
            Id = newId ?? Id,
            AppliesTo = AppliesTo,
            AdditionalRanges = AdditionalRanges,
            Priority = Priority,
            RuleType = RuleType,
            Operator = Operator,
            Value1 = Value1,
            Value2 = Value2,
            FormatIfTrue = FormatIfTrue?.Clone(),
            MinColor = MinColor,
            MidColor = MidColor,
            MaxColor = MaxColor,
            MinColorSource = MinColorSource,
            MidColorSource = MidColorSource,
            MaxColorSource = MaxColorSource,
            UseThreeColorScale = UseThreeColorScale,
            MinThresholdType = MinThresholdType,
            MinThresholdValue = MinThresholdValue,
            MinThresholdGreaterThanOrEqual = MinThresholdGreaterThanOrEqual,
            MidThresholdType = MidThresholdType,
            MidThresholdValue = MidThresholdValue,
            MidThresholdGreaterThanOrEqual = MidThresholdGreaterThanOrEqual,
            MaxThresholdType = MaxThresholdType,
            MaxThresholdValue = MaxThresholdValue,
            MaxThresholdGreaterThanOrEqual = MaxThresholdGreaterThanOrEqual,
            DataBarColor = DataBarColor,
            DataBarColorSource = DataBarColorSource,
            DataBarMinThresholdType = DataBarMinThresholdType,
            DataBarMinThresholdValue = DataBarMinThresholdValue,
            DataBarMaxThresholdType = DataBarMaxThresholdType,
            DataBarMaxThresholdValue = DataBarMaxThresholdValue,
            DataBarShowValue = DataBarShowValue,
            DataBarMinLength = DataBarMinLength,
            DataBarMaxLength = DataBarMaxLength,
            DataBarGradient = DataBarGradient,
            DataBarBorder = DataBarBorder,
            DataBarBorderColor = DataBarBorderColor,
            DataBarAxisPosition = DataBarAxisPosition,
            DataBarAxisColor = DataBarAxisColor,
            DataBarNegativeFillColor = DataBarNegativeFillColor,
            DataBarNegativeBorderColor = DataBarNegativeBorderColor,
            DataBarNegativeFillSameAsPositive = DataBarNegativeFillSameAsPositive,
            DataBarNegativeBorderSameAsPositive = DataBarNegativeBorderSameAsPositive,
            DataBarDirection = DataBarDirection,
            AboveAverage = AboveAverage,
            EqualAverage = EqualAverage,
            StdDevCount = StdDevCount,
            FormulaText = FormulaText,
            IconSetStyle = IconSetStyle,
            IconSetShowValue = IconSetShowValue,
            IconSetReverse = IconSetReverse,
            TopBottomRank = TopBottomRank,
            TopBottomPercent = TopBottomPercent,
            TextRuleText = TextRuleText,
            DateOccurringPeriod = DateOccurringPeriod,
            StopIfTrue = StopIfTrue,
            NativeAttributes = NativeAttributes,
            NativeChildXmls = newId.HasValue && newId.Value != Id
                ? ConditionalFormatNativeMetadata.RemoveX14IdNativeChildXmls(NativeChildXmls)
                : NativeChildXmls,
            NativePayloadAttributes = NativePayloadAttributes,
            NativePayloadChildXmls = NativePayloadChildXmls,
            NativeContainerAttributes = NativeContainerAttributes,
            NativeContainerChildXmls = NativeContainerChildXmls,
        };

        clone.IconSetThresholds.AddRange(IconSetThresholds);
        clone.IconOverrides.AddRange(IconOverrides);
        return clone;
    }
}
