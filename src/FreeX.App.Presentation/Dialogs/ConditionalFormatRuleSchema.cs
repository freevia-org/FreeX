using System.Globalization;
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

    /// <summary>The color-scale min/mid/max threshold types.</summary>
    ColorScaleThresholdTypes,

    /// <summary>The color-scale min/mid/max colors.</summary>
    ColorScaleColors,

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

    /// <summary>Top 10 rank (item count) or percent, as typed.</summary>
    public string? Rank { get; init; }

    /// <summary>True when a Top 10 rule's threshold is a percent rather than an item count.</summary>
    public bool IsPercent { get; init; }

    public string? IconSetStyle { get; init; }

    public CfThresholdType DataBarMinType { get; init; } = CfThresholdType.Min;
    public CfThresholdType DataBarMaxType { get; init; } = CfThresholdType.Max;

    public bool UseThreeColorScale { get; init; }

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
                new[] { CfInputField.DataBarMinMaxType, CfInputField.DataBarColors },

            CfRuleType.ColorScale =>
                new[]
                {
                    CfInputField.UseThreeColorScale,
                    CfInputField.ColorScaleThresholdTypes,
                    CfInputField.ColorScaleColors
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
            errors.Add(new CfValidationError(CfInputField.ColorScaleColors, "A valid minimum color is required."));

        if (input.UseThreeColorScale && !IsParseableColor(input.MidColor))
            errors.Add(new CfValidationError(CfInputField.ColorScaleColors, "A valid midpoint color is required."));

        if (!IsParseableColor(input.MaxColor))
            errors.Add(new CfValidationError(CfInputField.ColorScaleColors, "A valid maximum color is required."));
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
