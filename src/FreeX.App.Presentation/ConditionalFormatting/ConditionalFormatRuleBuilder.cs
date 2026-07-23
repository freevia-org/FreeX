using System.Globalization;

using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

/// <summary>
/// Non-UI glue backing conditional-format rule editors. Builds a Core
/// <see cref="ConditionalFormat"/> from the portable <see cref="CfRuleInput"/> the editor collects
/// (validated by <see cref="ConditionalFormatRuleSchema"/>), and maps it to the Core.Commands
/// add/replace command without depending on any running UI, so it is unit testable. Rendering of the
/// applied rule is already handled by the grid.
/// </summary>
public static class ConditionalFormatRuleBuilder
{
    /// <summary>
    /// Builds a Core conditional-format rule from a validated editor input over the given range.
    /// The caller is expected to have run <see cref="ConditionalFormatRuleSchema.Validate"/> first;
    /// well-formed-but-empty optional fields are tolerated here and fall back to model defaults.
    /// <paramref name="highlight"/> supplies the "format if true" style for the highlight rule
    /// families (Cell Value, Formula, Top 10, text, duplicate/unique, above-average); it is ignored
    /// for the visual families (Icon Set, Data Bar, Color Scale) that carry their own appearance.
    /// </summary>
    public static ConditionalFormat Build(
        CfRuleInput input,
        GridRange range,
        ConditionalFormatHighlightPreset? highlight = null,
        Guid? id = null,
        CellStyle? customFormat = null,
        ConditionalFormat? existingRule = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var cf = existingRule is not null
            ? ConditionalFormatDialogPlanner.CloneRule(existingRule)
            : new ConditionalFormat
            {
                Id = id ?? Guid.NewGuid(),
                AppliesTo = range,
            };

        var previousRuleType = cf.RuleType;
        cf.AppliesTo = range;
        cf.RuleType = input.RuleType;

        if (existingRule is not null && input.RuleType != previousRuleType)
            ConditionalFormatDialogPlanner.ClearNativeConditionalFormatMetadata(cf);

        switch (input.RuleType)
        {
            case CfRuleType.Formula:
                var raw = (input.Formula ?? string.Empty).Trim();
                cf.FormulaText = raw.StartsWith('=') ? raw[1..] : raw;
                break;

            case CfRuleType.CellValue:
                cf.Operator = input.Operator;
                cf.Value1 = ConditionalFormatInputParser.BlankToNull(input.Value1);
                cf.Value2 = ConditionalFormatInputParser.BlankToNull(input.Value2);
                break;

            case CfRuleType.Top10:
                cf.TopBottomPercent = input.IsPercent;
                // For Top 10 rules the model reuses AboveAverage to record top (true) vs bottom (false).
                cf.AboveAverage = input.IsTop;
                if (int.TryParse((input.Rank ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rank))
                    cf.TopBottomRank = rank;
                break;

            case CfRuleType.IconSet:
                cf.IconSetStyle = ConditionalFormatInputParser.BlankToNull(input.IconSetStyle) ?? ConditionalFormatIconSetCatalog.DefaultStyle;
                cf.IconSetShowValue = input.IconSetShowValue;
                cf.IconSetReverse = input.IconSetReverse;
                cf.IconSetThresholds.Clear();
                cf.IconSetThresholds.AddRange(input.IconSetThresholds ?? ConditionalFormatIconSetCatalog.CreateThresholds(cf.IconSetStyle));
                ApplyIconOverrides(cf, input.IconOverrides);
                break;

            case CfRuleType.DataBar:
                if (input.DataBarColor is { } dataBarColor)
                    cf.DataBarColor = dataBarColor;
                cf.DataBarMinThresholdType = input.DataBarMinType;
                cf.DataBarMinThresholdValue = ConditionalFormatInputParser.BlankToNull(input.DataBarMinValue);
                cf.DataBarMaxThresholdType = input.DataBarMaxType;
                cf.DataBarMaxThresholdValue = ConditionalFormatInputParser.BlankToNull(input.DataBarMaxValue);
                cf.DataBarShowValue = input.DataBarShowValue;
                cf.DataBarGradient = input.DataBarGradient;
                if (ConditionalFormatInputParser.TryParseOptionalPercent(input.DataBarMinLength, out var minLength))
                    cf.DataBarMinLength = minLength;
                if (ConditionalFormatInputParser.TryParseOptionalPercent(input.DataBarMaxLength, out var maxLength))
                    cf.DataBarMaxLength = maxLength;
                cf.DataBarBorder = input.DataBarBorder;
                cf.DataBarAxisPosition = ConditionalFormatInputParser.BlankToNull(input.DataBarAxisPosition);
                cf.DataBarAxisColor = input.DataBarAxisColor;
                cf.DataBarNegativeFillColor = input.DataBarNegativeFillColor;
                cf.DataBarNegativeBorderColor = input.DataBarNegativeBorderColor;
                break;

            case CfRuleType.ColorScale:
                cf.UseThreeColorScale = input.UseThreeColorScale;
                cf.MinThresholdType = input.ColorScaleMinType;
                cf.MinThresholdValue = ConditionalFormatInputParser.BlankToNull(input.ColorScaleMinValue);
                if (ConditionalFormatInputParser.TryParseRgbColor(input.MinColor, out var minColor))
                    cf.MinColor = minColor;
                cf.MidThresholdType = input.ColorScaleMidType;
                cf.MidThresholdValue = ConditionalFormatInputParser.BlankToNull(input.ColorScaleMidValue);
                if (input.UseThreeColorScale && ConditionalFormatInputParser.TryParseRgbColor(input.MidColor, out var midColor))
                    cf.MidColor = midColor;
                cf.MaxThresholdType = input.ColorScaleMaxType;
                cf.MaxThresholdValue = ConditionalFormatInputParser.BlankToNull(input.ColorScaleMaxValue);
                if (ConditionalFormatInputParser.TryParseRgbColor(input.MaxColor, out var maxColor))
                    cf.MaxColor = maxColor;
                break;

            case CfRuleType.ContainsText:
            case CfRuleType.NotContainsText:
            case CfRuleType.BeginsWith:
            case CfRuleType.EndsWith:
                var previousTextRuleText = cf.TextRuleText;
                var newTextRuleText = ConditionalFormatInputParser.BlankToNull(input.Text);
                cf.TextRuleText = newTextRuleText;
                // The rule's FormulaText (if any) was cloned from the loaded rule and was generated
                // for the OLD text -- once the user actually changes the text, that stale formula
                // must be dropped so the writer's synthesis fallback (XlsxAdvancedConditionalFormatWriter)
                // regenerates it from the new text on save instead of silently keeping the old condition.
                if (!string.Equals(previousTextRuleText, newTextRuleText, StringComparison.Ordinal))
                    cf.FormulaText = null;
                break;

            case CfRuleType.DateOccurring:
                var previousDateOccurringPeriod = cf.DateOccurringPeriod;
                var newDateOccurringPeriod = ConditionalFormatInputParser.BlankToNull(input.DatePeriod ?? input.Text);
                cf.DateOccurringPeriod = newDateOccurringPeriod;
                // Same staleness hazard as the text-rule case above: a formula cloned from the loaded
                // rule was generated for the OLD period and must be cleared once the period actually
                // changes, so the writer's synthesis fallback regenerates it for the new period.
                if (!string.Equals(previousDateOccurringPeriod, newDateOccurringPeriod, StringComparison.Ordinal))
                    cf.FormulaText = null;
                break;

            case CfRuleType.DuplicateValues:
            case CfRuleType.UniqueValues:
                // The schema's DuplicateOrUnique choice is already encoded in the rule type itself.
                break;
        }

        if (input.RuleType != CfRuleType.Formula)
        {
            cf.AboveAverage = input.IsTop;
            cf.TopBottomPercent = input.IsPercent;
        }

        // The visual families render their own appearance; the highlight families take a fill/font
        // style — either an explicit custom format (from the "Format…" picker) or a named preset.
        if (input.RuleType is CfRuleType.IconSet or CfRuleType.DataBar or CfRuleType.ColorScale)
            cf.FormatIfTrue = null;
        else
            cf.FormatIfTrue = customFormat ?? (highlight ?? ConditionalFormatHighlightPreset.Default).ToCellStyle();

        return cf;
    }

    private static void ApplyIconOverrides(ConditionalFormat cf, IReadOnlyList<CfIconOverride?>? overrides)
    {
        cf.IconOverrides.Clear();
        if (overrides is null || overrides.Count == 0)
            return;

        var hasAnyOverride = false;
        foreach (var iconOverride in overrides)
        {
            if (iconOverride is not null)
            {
                hasAnyOverride = true;
                break;
            }
        }

        if (!hasAnyOverride)
            return;

        for (var i = 0; i < overrides.Count; i++)
        {
            cf.IconOverrides.Add(overrides[i] ?? new CfIconOverride(
                string.IsNullOrWhiteSpace(cf.IconSetStyle) ? ConditionalFormatIconSetCatalog.DefaultStyle : cf.IconSetStyle,
                i));
        }
    }

    /// <summary>
    /// Maps a built rule onto the Core add/replace command. Reusing the rule's existing
    /// <see cref="ConditionalFormat.Id"/> replaces a rule in place (edit); a fresh id adds a new one.
    /// </summary>
    public static ApplyConditionalFormatCommand ToApplyCommand(SheetId sheetId, ConditionalFormat rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new ApplyConditionalFormatCommand(sheetId, rule);
    }

    /// <summary>Convenience: validate, build, and map to the add command in one step.</summary>
    public static CfRuleCommandResult TryBuildApplyCommand(
        CfRuleInput input,
        SheetId sheetId,
        GridRange range,
        ConditionalFormatHighlightPreset? highlight = null,
        Guid? id = null,
        CellStyle? customFormat = null,
        ConditionalFormat? existingRule = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var schema = ConditionalFormatRuleSchema.ForRuleType(input.RuleType);
        var validation = schema.Validate(input);
        if (!validation.IsValid)
            return CfRuleCommandResult.Invalid(validation);

        // Forward the rule being edited (not just its Id) so Build clones it — preserving fields the
        // editor doesn't surface (e.g. StopIfTrue) instead of silently resetting them to defaults.
        var rule = Build(input, range, highlight, id, customFormat, existingRule);
        return CfRuleCommandResult.Ok(rule, ToApplyCommand(sheetId, rule));
    }
}

/// <summary>The outcome of <see cref="ConditionalFormatRuleBuilder.TryBuildApplyCommand"/>.</summary>
public sealed record CfRuleCommandResult
{
    private CfRuleCommandResult(
        bool isValid,
        ConditionalFormat? rule,
        ApplyConditionalFormatCommand? command,
        CfValidationResult validation)
    {
        IsValid = isValid;
        Rule = rule;
        Command = command;
        Validation = validation;
    }

    public bool IsValid { get; }
    public ConditionalFormat? Rule { get; }
    public ApplyConditionalFormatCommand? Command { get; }
    public CfValidationResult Validation { get; }

    public static CfRuleCommandResult Ok(ConditionalFormat rule, ApplyConditionalFormatCommand command) =>
        new(true, rule, command, CfValidationResult.Valid);

    public static CfRuleCommandResult Invalid(CfValidationResult validation) =>
        new(false, null, null, validation);
}

/// <summary>
/// A fill/font appearance applied when a highlight-style rule's condition is true. Mirrors the named
/// presets the format-preset combo offers (and an arbitrary custom fill/font), kept portable so
/// editor surfaces and their tests share one definition.
/// </summary>
public sealed record ConditionalFormatHighlightPreset(
    string Label,
    CellColor? FillColor,
    CellColor? FontColor,
    bool Bold)
{
    /// <summary>Builds the Core cell style this preset applies as <c>FormatIfTrue</c>.</summary>
    public CellStyle ToCellStyle()
    {
        var style = new CellStyle { Bold = Bold };
        if (FillColor is { } fill)
            style.FillColor = fill;
        if (FontColor is { } font)
            style.FontColor = font;
        return style;
    }

    /// <summary>The named presets, in dialog order. The first entry is the default highlight.</summary>
    public static IReadOnlyList<ConditionalFormatHighlightPreset> Presets { get; } =
    [
        new("Light Red Fill with Dark Red Text", new CellColor(255, 199, 206), new CellColor(156, 0, 6), true),
        new("Yellow Fill with Dark Yellow Text", new CellColor(255, 235, 132), new CellColor(156, 101, 0), true),
        new("Green Fill with Dark Green Text", new CellColor(198, 239, 206), new CellColor(0, 97, 0), true),
        new("Light Red Fill", new CellColor(255, 199, 206), null, false),
        new("Yellow Fill", new CellColor(255, 235, 132), null, false),
        new("Green Fill", new CellColor(198, 239, 206), null, false),
        new("Light Blue Fill", new CellColor(189, 215, 238), null, false),
        new("Bold Red Text", null, new CellColor(255, 0, 0), true),
        new("Bold Green Text", null, new CellColor(0, 176, 80), true),
    ];

    /// <summary>The default highlight (Light Red Fill with Dark Red Text), matching Excel.</summary>
    public static ConditionalFormatHighlightPreset Default => Presets[0];
}
