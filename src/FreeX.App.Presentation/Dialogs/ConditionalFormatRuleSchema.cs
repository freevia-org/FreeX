using System.Globalization;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Dialogs;

/// <summary>
/// The discrete input controls a conditional-format rule editor can surface. A rule type maps to the
/// subset it needs (see <see cref="ConditionalFormatRuleSchema"/>).
/// </summary>
public enum CfInputField
{
    /// <summary>A free-form formula expression (Formula rule).</summary>
    Formula,

    /// <summary>The comparison operator for a Cell Value rule.</summary>
    Operator,

    /// <summary>The first operand / value (Cell Value, text rules).</summary>
    Value1,

    /// <summary>The second operand, only for Between / NotBetween operators.</summary>
    Value2,

    /// <summary>The literal text for a contains/begins/ends rule.</summary>
    Text,

    /// <summary>The rank or percent for a Top 10 rule.</summary>
    Rank,

    /// <summary>Whether a Top 10 rule selects the top (true) or bottom (false) of the range.</summary>
    TopBottom,

    /// <summary>Whether a Top 10 rule's threshold is a percent (true) rather than an item count.</summary>
    Percent,

    /// <summary>The icon-set style selection.</summary>
    IconSetStyle,

    /// <summary>The data-bar minimum/maximum threshold types.</summary>
    DataBarMinMaxType,

    /// <summary>The data-bar fill/border colors.</summary>
    DataBarColors,

    /// <summary>The optional minimum data-bar length percent.</summary>
    DataBarMinLength,

    /// <summary>The optional maximum data-bar length percent.</summary>
    DataBarMaxLength,

    /// <summary>The color-scale min/mid/max threshold types.</summary>
    ColorScaleThresholdTypes,

    /// <summary>The color-scale min/mid/max colors.</summary>
    ColorScaleColors,

    /// <summary>The color-scale minimum color.</summary>
    ColorScaleMinColor,

    /// <summary>The color-scale midpoint color.</summary>
    ColorScaleMidColor,

    /// <summary>The color-scale maximum color.</summary>
    ColorScaleMaxColor,

    /// <summary>The data-bar minimum threshold value (Number/Percent/Percentile/Formula types).</summary>
    DataBarMinValue,

    /// <summary>The data-bar maximum threshold value (Number/Percent/Percentile/Formula types).</summary>
    DataBarMaxValue,

    /// <summary>The color-scale minimum threshold value (Number/Percent/Percentile/Formula types).</summary>
    ColorScaleMinValue,

    /// <summary>The color-scale midpoint threshold value (Number/Percent/Percentile/Formula types).</summary>
    ColorScaleMidValue,

    /// <summary>The color-scale maximum threshold value (Number/Percent/Percentile/Formula types).</summary>
    ColorScaleMaxValue,

    /// <summary>Whether the color scale uses three colors (with a midpoint) rather than two.</summary>
    UseThreeColorScale,

    /// <summary>The relative date period for a Date Occurring rule.</summary>
    DatePeriod,

    /// <summary>Whether a duplicate-values rule targets duplicates (true) or uniques (false).</summary>
    DuplicateOrUnique
}

/// <summary>A single validation failure produced by <see cref="ConditionalFormatRuleSchema.Validate"/>.</summary>
public sealed record CfValidationError(CfInputField Field, string Message);

/// <summary>The outcome of validating a candidate rule input against its schema.</summary>
public sealed record CfValidationResult(IReadOnlyList<CfValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static CfValidationResult Valid { get; } = new([]);
}

/// <summary>
/// A candidate conditional-format rule input, as collected by the editor before it is committed. Each
/// field is optional; the schema decides which ones are required for the chosen <see cref="RuleType"/>.
/// </summary>
public sealed record CfRuleInput
{
    public CfRuleType RuleType { get; init; }

    /// <summary>Operator for a Cell Value rule.</summary>
    public CfOperator Operator { get; init; } = CfOperator.GreaterThan;

    public string? Formula { get; init; }
    public string? Value1 { get; init; }
    public string? Value2 { get; init; }
    public string? Text { get; init; }

    /// <summary>The relative date period token for a Date Occurring rule.</summary>
    public string? DatePeriod { get; init; }

    /// <summary>Top 10 rank (item count) or percent, as typed.</summary>
    public string? Rank { get; init; }

    /// <summary>True when a Top 10 rule's threshold is a percent rather than an item count.</summary>
    public bool IsPercent { get; init; }

    /// <summary>
    /// True when a Top 10 rule selects the TOP of the range; false selects the BOTTOM. Mirrors the
    /// model's <see cref="ConditionalFormat.AboveAverage"/> convention for Top 10 rules.
    /// </summary>
    public bool IsTop { get; init; } = true;

    public string? IconSetStyle { get; init; }
    public bool IconSetShowValue { get; init; } = true;
    public bool IconSetReverse { get; init; }
    public IReadOnlyList<CfThresholdModel>? IconSetThresholds { get; init; }
    public IReadOnlyList<CfIconOverride?>? IconOverrides { get; init; }

    /// <summary>
    /// Defaults to <see cref="CfThresholdType.AutoMin"/> (Excel's "Automatic" default for a brand-new
    /// data bar), not the explicit <see cref="CfThresholdType.Min"/> ("Lowest Value") — mirrors
    /// <see cref="ConditionalFormat.DataBarMinThresholdType"/>'s model default so a data bar authored
    /// via an editor that never touches this field still matches Excel.
    /// </summary>
    public CfThresholdType DataBarMinType { get; init; } = CfThresholdType.AutoMin;
    public string? DataBarMinValue { get; init; }
    /// <summary>
    /// Defaults to <see cref="CfThresholdType.AutoMax"/> (Excel's "Automatic" default for a brand-new
    /// data bar), not the explicit <see cref="CfThresholdType.Max"/> ("Highest Value") — mirrors
    /// <see cref="ConditionalFormat.DataBarMaxThresholdType"/>'s model default so a data bar authored
    /// via an editor that never touches this field still matches Excel.
    /// </summary>
    public CfThresholdType DataBarMaxType { get; init; } = CfThresholdType.AutoMax;
    public string? DataBarMaxValue { get; init; }
    public RgbColor? DataBarColor { get; init; }
    public bool DataBarShowValue { get; init; } = true;
    public bool DataBarGradient { get; init; } = true;
    public string? DataBarMinLength { get; init; }
    public string? DataBarMaxLength { get; init; }
    public bool DataBarBorder { get; init; }
    public string? DataBarAxisPosition { get; init; }
    public RgbColor? DataBarAxisColor { get; init; }
    public RgbColor? DataBarNegativeFillColor { get; init; }
    public RgbColor? DataBarNegativeBorderColor { get; init; }

    public bool UseThreeColorScale { get; init; }
    public CfThresholdType ColorScaleMinType { get; init; } = CfThresholdType.Min;
    public string? ColorScaleMinValue { get; init; }
    public CfThresholdType ColorScaleMidType { get; init; } = CfThresholdType.Percentile;
    public string? ColorScaleMidValue { get; init; }
    public CfThresholdType ColorScaleMaxType { get; init; } = CfThresholdType.Max;
    public string? ColorScaleMaxValue { get; init; }

    /// <summary>Min/mid/max colors as typed; a null or unparseable entry where required is an error.</summary>
    public string? MinColor { get; init; }
    public string? MidColor { get; init; }
    public string? MaxColor { get; init; }
}

/// <summary>
/// Portable schema describing, per conditional-format rule type, which input controls apply and how a
/// candidate input is validated. This mirrors the field layout and threshold validation the desktop
/// conditional-format dialog enforces, with the rendering left to a renderer.
/// </summary>
public sealed record ConditionalFormatRuleSchema(
    CfRuleType RuleType,
    IReadOnlyList<CfInputField> Fields)
{
    /// <summary>Inclusive rank range for a Top 10 rule that selects an item count.</summary>
    public const int MinRank = 1;
    public const int MaxRank = 1000;

    /// <summary>Inclusive percent range for a Top 10 rule that selects a percent of the range.</summary>
    public const int MinPercent = 1;
    public const int MaxPercent = 100;

    /// <summary>True when the schema includes the given field.</summary>
    public bool HasField(CfInputField field) => Fields.Contains(field);

    /// <summary>Resolves the schema for a rule type, describing the fields its editor surfaces.</summary>
    public static ConditionalFormatRuleSchema ForRuleType(CfRuleType ruleType)
    {
        var fields = ruleType switch
        {
            CfRuleType.Formula =>
                new[] { CfInputField.Formula },

            CfRuleType.CellValue =>
                new[] { CfInputField.Operator, CfInputField.Value1, CfInputField.Value2 },

            CfRuleType.Top10 =>
                new[] { CfInputField.Rank, CfInputField.TopBottom, CfInputField.Percent },

            CfRuleType.IconSet =>
                new[] { CfInputField.IconSetStyle },

            CfRuleType.DataBar =>
                new[]
                {
                    CfInputField.DataBarMinMaxType,
                    CfInputField.DataBarColors,
                    CfInputField.DataBarMinLength,
                    CfInputField.DataBarMaxLength
                },

            CfRuleType.ColorScale =>
                new[]
                {
                    CfInputField.UseThreeColorScale,
                    CfInputField.ColorScaleThresholdTypes,
                    CfInputField.ColorScaleColors,
                    CfInputField.ColorScaleMinColor,
                    CfInputField.ColorScaleMidColor,
                    CfInputField.ColorScaleMaxColor
                },

            CfRuleType.DateOccurring =>
                new[] { CfInputField.DatePeriod },

            CfRuleType.DuplicateValues or CfRuleType.UniqueValues =>
                new[] { CfInputField.DuplicateOrUnique },

            CfRuleType.ContainsText
                or CfRuleType.NotContainsText
                or CfRuleType.BeginsWith
                or CfRuleType.EndsWith =>
                new[] { CfInputField.Text },

            // Blanks / NoBlanks / Errors / NoErrors / AboveAverage have no value inputs.
            _ => Array.Empty<CfInputField>()
        };

        return new ConditionalFormatRuleSchema(ruleType, fields);
    }

    /// <summary>
    /// Validates a candidate input against this schema, returning every failure found (or
    /// <see cref="CfValidationResult.Valid"/> when the input is complete and well-formed).
    /// </summary>
    public CfValidationResult Validate(CfRuleInput input)
    {
        var errors = new List<CfValidationError>();

        switch (RuleType)
        {
            case CfRuleType.Formula:
                ValidateFormula(input, errors);
                break;

            case CfRuleType.CellValue:
                ValidateCellValue(input, errors);
                break;

            case CfRuleType.Top10:
                ValidateTop10(input, errors);
                break;

            case CfRuleType.IconSet:
                if (string.IsNullOrWhiteSpace(input.IconSetStyle))
                    errors.Add(new CfValidationError(CfInputField.IconSetStyle, "An icon-set style is required."));
                break;

            case CfRuleType.DataBar:
                ValidateDataBar(input, errors);
                break;

            case CfRuleType.ColorScale:
                ValidateColorScale(input, errors);
                break;

            case CfRuleType.ContainsText:
            case CfRuleType.NotContainsText:
            case CfRuleType.BeginsWith:
            case CfRuleType.EndsWith:
                if (string.IsNullOrWhiteSpace(input.Text))
                    errors.Add(new CfValidationError(CfInputField.Text, "Text is required."));
                break;

            // DataBar / DateOccurring / DuplicateValues / UniqueValues / Blanks / Errors and the
            // average rules carry only choices with valid defaults, so there is nothing to reject.
        }

        return errors.Count == 0 ? CfValidationResult.Valid : new CfValidationResult(errors);
    }

    private static void ValidateFormula(CfRuleInput input, List<CfValidationError> errors)
    {
        var raw = input.Formula?.Trim() ?? string.Empty;
        if (raw is "" or "=")
            errors.Add(new CfValidationError(CfInputField.Formula, "A formula is required."));
    }

    private static void ValidateCellValue(CfRuleInput input, List<CfValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(input.Value1))
            errors.Add(new CfValidationError(CfInputField.Value1, "A value is required."));

        if (input.Operator is CfOperator.Between or CfOperator.NotBetween
            && string.IsNullOrWhiteSpace(input.Value2))
            errors.Add(new CfValidationError(CfInputField.Value2, "A maximum value is required."));
    }

    private static void ValidateTop10(CfRuleInput input, List<CfValidationError> errors)
    {
        if (input.IsPercent)
        {
            if (!int.TryParse(input.Rank?.Trim(), out var percent)
                || percent is < MinPercent or > MaxPercent)
                errors.Add(new CfValidationError(
                    CfInputField.Rank,
                    $"Enter a percent between {MinPercent} and {MaxPercent}."));
        }
        else if (!int.TryParse(input.Rank?.Trim(), out var rank) || rank is < MinRank or > MaxRank)
        {
            errors.Add(new CfValidationError(
                CfInputField.Rank,
                $"Enter a rank between {MinRank} and {MaxRank}."));
        }
    }

    private static void ValidateColorScale(CfRuleInput input, List<CfValidationError> errors)
    {
        if (!IsParseableColor(input.MinColor))
            errors.Add(new CfValidationError(CfInputField.ColorScaleMinColor, "A valid minimum color is required."));

        if (input.UseThreeColorScale && !IsParseableColor(input.MidColor))
            errors.Add(new CfValidationError(CfInputField.ColorScaleMidColor, "A valid midpoint color is required."));

        if (!IsParseableColor(input.MaxColor))
            errors.Add(new CfValidationError(CfInputField.ColorScaleMaxColor, "A valid maximum color is required."));

        ValidateThresholdValue(input.ColorScaleMinType, input.ColorScaleMinValue, CfInputField.ColorScaleMinValue, errors);

        if (input.UseThreeColorScale)
            ValidateThresholdValue(input.ColorScaleMidType, input.ColorScaleMidValue, CfInputField.ColorScaleMidValue, errors);

        ValidateThresholdValue(input.ColorScaleMaxType, input.ColorScaleMaxValue, CfInputField.ColorScaleMaxValue, errors);
    }

    private static void ValidateDataBar(CfRuleInput input, List<CfValidationError> errors)
    {
        if (!ConditionalFormatInputParser.TryParseOptionalPercent(input.DataBarMinLength, out _))
            errors.Add(new CfValidationError(CfInputField.DataBarMinLength, "Enter a minimum bar length from 0 to 100."));

        if (!ConditionalFormatInputParser.TryParseOptionalPercent(input.DataBarMaxLength, out _))
            errors.Add(new CfValidationError(CfInputField.DataBarMaxLength, "Enter a maximum bar length from 0 to 100."));

        ValidateThresholdValue(input.DataBarMinType, input.DataBarMinValue, CfInputField.DataBarMinValue, errors);
        ValidateThresholdValue(input.DataBarMaxType, input.DataBarMaxValue, CfInputField.DataBarMaxValue, errors);
    }

    /// <summary>
    /// Validates a data-bar/color-scale threshold's typed value against its selected threshold type.
    /// <see cref="CfThresholdType.Min"/>/<see cref="CfThresholdType.Max"/> (the explicit "Lowest Value"/
    /// "Highest Value" endpoint) and the data-bar-only <see cref="CfThresholdType.AutoMin"/>/
    /// <see cref="CfThresholdType.AutoMax"/> ("Automatic" endpoint) all derive their bound from the
    /// actual range data and ignore any typed text. <see cref="CfThresholdType.Number"/>/
    /// <see cref="CfThresholdType.Percent"/>/<see cref="CfThresholdType.Percentile"/> require a value
    /// that parses the same way <see cref="ConditionalFormatStatistics.TryResolveThreshold"/> parses it
    /// at render time — without this check, non-numeric text (or a blank box) silently resolves to no
    /// bar/scale at all instead of being rejected the way real Excel's "Please enter a valid entry"
    /// guard rejects it. <see cref="CfThresholdType.Formula"/> requires non-blank text (the formula
    /// itself).
    /// </summary>
    private static void ValidateThresholdValue(
        CfThresholdType type,
        string? value,
        CfInputField field,
        List<CfValidationError> errors)
    {
        switch (type)
        {
            case CfThresholdType.Min:
            case CfThresholdType.Max:
            case CfThresholdType.AutoMin:
            case CfThresholdType.AutoMax:
                break;

            case CfThresholdType.Formula:
                if (string.IsNullOrWhiteSpace(value))
                    errors.Add(new CfValidationError(field, "A formula is required."));
                break;

            default:
                if (!ConditionalFormatStatistics.TryParseInvariant(value, out _))
                    errors.Add(new CfValidationError(field, "Enter a valid number."));
                break;
        }
    }

    /// <summary>
    /// Mirrors the color-text validation the desktop dialog applies to color-scale entries: an
    /// <c>r,g,b</c> triple of byte components. Kept self-contained so this portable schema does not
    /// depend on host-layer parsing.
    /// </summary>
    private static bool IsParseableColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Trim().Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 3
            && byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            && byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            && byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }
}
