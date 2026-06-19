using System.Globalization;

using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Dialogs;

/// <summary>
/// Non-UI glue backing the Avalonia conditional-format rule editor. Builds a Core
/// <see cref="ConditionalFormat"/> from the portable <see cref="CfRuleInput"/> the editor collects
/// (validated by <see cref="ConditionalFormatRuleSchema"/>), and maps it to the Core.Commands
/// add/replace command. Mirrors the fidelity of the Windows WPF dialog's commit path
/// (<c>ConditionalFormatDialog.Result</c>) without depending on any running UI, so it is unit
/// testable. Rendering of the applied rule is already handled by the grid.
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
        Guid? id = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var cf = new ConditionalFormat
        {
            Id = id ?? Guid.NewGuid(),
            AppliesTo = range,
            RuleType = input.RuleType,
        };

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
                cf.IconSetShowValue = true;
                cf.IconSetReverse = false;
                cf.IconSetThresholds.Clear();
                cf.IconSetThresholds.AddRange(ConditionalFormatIconSetCatalog.CreateThresholds(cf.IconSetStyle));
                break;

            case CfRuleType.DataBar:
                cf.DataBarMinThresholdType = input.DataBarMinType;
                cf.DataBarMaxThresholdType = input.DataBarMaxType;
                break;

            case CfRuleType.ColorScale:
                cf.UseThreeColorScale = input.UseThreeColorScale;
                if (ConditionalFormatInputParser.TryParseRgbColor(input.MinColor, out var minColor))
                    cf.MinColor = minColor;
                if (input.UseThreeColorScale && ConditionalFormatInputParser.TryParseRgbColor(input.MidColor, out var midColor))
                    cf.MidColor = midColor;
                if (ConditionalFormatInputParser.TryParseRgbColor(input.MaxColor, out var maxColor))
                    cf.MaxColor = maxColor;
                break;

            case CfRuleType.ContainsText:
            case CfRuleType.NotContainsText:
            case CfRuleType.BeginsWith:
            case CfRuleType.EndsWith:
                cf.TextRuleText = ConditionalFormatInputParser.BlankToNull(input.Text);
                break;

            case CfRuleType.DateOccurring:
                cf.DateOccurringPeriod = ConditionalFormatInputParser.BlankToNull(input.Text);
                break;

            case CfRuleType.DuplicateValues:
            case CfRuleType.UniqueValues:
                // The schema's DuplicateOrUnique choice is already encoded in the rule type itself.
                break;
        }

        // The visual families render their own appearance; the highlight families take a fill/font style.
        if (input.RuleType is CfRuleType.IconSet or CfRuleType.DataBar or CfRuleType.ColorScale)
            cf.FormatIfTrue = null;
        else
            cf.FormatIfTrue = (highlight ?? ConditionalFormatHighlightPreset.Default).ToCellStyle();

        return cf;
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
        Guid? id = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var schema = ConditionalFormatRuleSchema.ForRuleType(input.RuleType);
        var validation = schema.Validate(input);
        if (!validation.IsValid)
            return CfRuleCommandResult.Invalid(validation);

        var rule = Build(input, range, highlight, id);
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
/// presets the Windows dialog's format-preset combo offers (and an arbitrary custom fill/font), kept
/// portable so the Avalonia editor and its tests share one definition.
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
