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

/// <summary>
/// <see cref="Min"/>/<see cref="Max"/> mean an EXPLICIT endpoint ("Lowest Value"/"Highest Value" in
/// Excel's Edit Formatting Rule dialog): the axis endpoint is exactly the range's actual minimum/
/// maximum, with no further adjustment. <see cref="AutoMin"/>/<see cref="AutoMax"/> are the data-bar-
/// only "Automatic" endpoint (Excel's default): the base value is the same actual minimum/maximum,
/// but data bars additionally keep a zero baseline for Automatic (the resolved minimum is clamped to
/// <c>min(0, actual minimum)</c> and the maximum to <c>max(0, actual maximum)</c>) -- see the zero-
/// clamp logic in ViewportConditionalFormatEvaluator.Thresholds.cs / ConditionalFormatEvaluator.cs.
/// Excel's OOXML cfvo @type attribute distinguishes them only in the x14 extended data-bar block
/// ("autoMin"/"autoMax" vs "min"/"max"); the pre-2010-compatible classic cfvo block cannot express
/// Automatic distinctly and always falls back to "min"/"max" for both cases. Color-scale and icon-set
/// thresholds have no Automatic concept and only ever use <see cref="Min"/>/<see cref="Max"/>.
/// </summary>
public enum CfThresholdType
{
    Min,
    Max,
    Number,
    Percent,
    Percentile,
    Formula,

    // Appended at the end (not inserted above) so existing serialized ordinal values -- the native
    // JSON (.fxl) format persists this enum by ordinal -- are never shifted.
    AutoMin,
    AutoMax
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
    /// <see cref="AdditionalRanges"/>. Use <see cref="Contains"/> for hot-path membership tests.
    /// </summary>
    public IEnumerable<GridRange> AllRanges =>
        AdditionalRanges is null
            ? [AppliesTo]
            : [AppliesTo, .. AdditionalRanges];

    /// <summary>The number of ranges covered by this rule.</summary>
    public int RangeCount => 1 + (AdditionalRanges?.Count ?? 0);

    /// <summary>
    /// Tests whether <paramref name="address"/> is covered without materializing
    /// <see cref="AllRanges"/>.
    /// </summary>
    public bool Contains(CellAddress address)
    {
        if (AppliesTo.Contains(address))
            return true;

        if (AdditionalRanges is null)
            return false;

        for (var index = 0; index < AdditionalRanges.Count; index++)
        {
            if (AdditionalRanges[index].Contains(address))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Tests whether <paramref name="range"/> overlaps any covered range without materializing
    /// <see cref="AllRanges"/>.
    /// </summary>
    public bool Overlaps(GridRange range)
    {
        if (AppliesTo.Overlaps(range))
            return true;

        if (AdditionalRanges is null)
            return false;

        for (var index = 0; index < AdditionalRanges.Count; index++)
        {
            if (AdditionalRanges[index].Overlaps(range))
                return true;
        }

        return false;
    }

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
    /// <summary>Defaults to <see cref="CfThresholdType.AutoMin"/> (Excel's "Automatic" default), not
    /// the explicit <see cref="CfThresholdType.Min"/> ("Lowest Value").</summary>
    public CfThresholdType DataBarMinThresholdType { get; set; } = CfThresholdType.AutoMin;
    public string? DataBarMinThresholdValue { get; set; }
    /// <summary>Defaults to <see cref="CfThresholdType.AutoMax"/> (Excel's "Automatic" default), not
    /// the explicit <see cref="CfThresholdType.Max"/> ("Highest Value").</summary>
    public CfThresholdType DataBarMaxThresholdType { get; set; } = CfThresholdType.AutoMax;
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

    /// <summary>
    /// r249: content comparison, field for field with <see cref="Clone"/>.
    /// <para>
    /// ConditionalFormat is a class with reference equality and sixty settable members, so a
    /// no-op decision about a rule needs this, and hand-listing sixty members is not something
    /// anyone should be asked to re-read. Clone is already the maintained enumeration of what
    /// this type consists of -- so R249_ConditionalFormatComparisonCoverageContractTests
    /// compares THIS method against Clone's own assignment list and fails if they diverge.
    /// A new member added to Clone and forgotten here is caught by the type's own definition
    /// of itself rather than by anybody noticing.
    /// </para>
    /// </summary>
    public bool SameAs(ConditionalFormat other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return
            Equals(Id, other.Id)
            && Equals(AppliesTo, other.AppliesTo)
            && SameRanges(AdditionalRanges, other.AdditionalRanges)
            && Equals(Priority, other.Priority)
            && Equals(RuleType, other.RuleType)
            && Equals(Operator, other.Operator)
            && Equals(Value1, other.Value1)
            && Equals(Value2, other.Value2)
            && Equals(FormatIfTrue, other.FormatIfTrue)
            && Equals(MinColor, other.MinColor)
            && Equals(MidColor, other.MidColor)
            && Equals(MaxColor, other.MaxColor)
            && Equals(MinColorSource, other.MinColorSource)
            && Equals(MidColorSource, other.MidColorSource)
            && Equals(MaxColorSource, other.MaxColorSource)
            && Equals(UseThreeColorScale, other.UseThreeColorScale)
            && Equals(MinThresholdType, other.MinThresholdType)
            && Equals(MinThresholdValue, other.MinThresholdValue)
            && Equals(MinThresholdGreaterThanOrEqual, other.MinThresholdGreaterThanOrEqual)
            && Equals(MidThresholdType, other.MidThresholdType)
            && Equals(MidThresholdValue, other.MidThresholdValue)
            && Equals(MidThresholdGreaterThanOrEqual, other.MidThresholdGreaterThanOrEqual)
            && Equals(MaxThresholdType, other.MaxThresholdType)
            && Equals(MaxThresholdValue, other.MaxThresholdValue)
            && Equals(MaxThresholdGreaterThanOrEqual, other.MaxThresholdGreaterThanOrEqual)
            && Equals(DataBarColor, other.DataBarColor)
            && Equals(DataBarColorSource, other.DataBarColorSource)
            && Equals(DataBarMinThresholdType, other.DataBarMinThresholdType)
            && Equals(DataBarMinThresholdValue, other.DataBarMinThresholdValue)
            && Equals(DataBarMaxThresholdType, other.DataBarMaxThresholdType)
            && Equals(DataBarMaxThresholdValue, other.DataBarMaxThresholdValue)
            && Equals(DataBarShowValue, other.DataBarShowValue)
            && Equals(DataBarMinLength, other.DataBarMinLength)
            && Equals(DataBarMaxLength, other.DataBarMaxLength)
            && Equals(DataBarGradient, other.DataBarGradient)
            && Equals(DataBarBorder, other.DataBarBorder)
            && Equals(DataBarBorderColor, other.DataBarBorderColor)
            && Equals(DataBarAxisPosition, other.DataBarAxisPosition)
            && Equals(DataBarAxisColor, other.DataBarAxisColor)
            && Equals(DataBarNegativeFillColor, other.DataBarNegativeFillColor)
            && Equals(DataBarNegativeBorderColor, other.DataBarNegativeBorderColor)
            && Equals(DataBarNegativeFillSameAsPositive, other.DataBarNegativeFillSameAsPositive)
            && Equals(DataBarNegativeBorderSameAsPositive, other.DataBarNegativeBorderSameAsPositive)
            && Equals(DataBarDirection, other.DataBarDirection)
            && Equals(AboveAverage, other.AboveAverage)
            && Equals(EqualAverage, other.EqualAverage)
            && Equals(StdDevCount, other.StdDevCount)
            && Equals(FormulaText, other.FormulaText)
            && Equals(IconSetStyle, other.IconSetStyle)
            && Equals(IconSetShowValue, other.IconSetShowValue)
            && Equals(IconSetReverse, other.IconSetReverse)
            && Equals(TopBottomRank, other.TopBottomRank)
            && Equals(TopBottomPercent, other.TopBottomPercent)
            && Equals(TextRuleText, other.TextRuleText)
            && Equals(DateOccurringPeriod, other.DateOccurringPeriod)
            && Equals(StopIfTrue, other.StopIfTrue)
            && Equals(NativeAttributes, other.NativeAttributes)
            && Equals(NativeChildXmls, other.NativeChildXmls)
            && Equals(NativePayloadAttributes, other.NativePayloadAttributes)
            && Equals(NativePayloadChildXmls, other.NativePayloadChildXmls)
            && Equals(NativeContainerAttributes, other.NativeContainerAttributes)
            && Equals(NativeContainerChildXmls, other.NativeContainerChildXmls);
    }

    private static bool SameRanges(IReadOnlyList<GridRange>? left, IReadOnlyList<GridRange>? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return left.Count == right.Count && left.SequenceEqual(right);
    }
}
