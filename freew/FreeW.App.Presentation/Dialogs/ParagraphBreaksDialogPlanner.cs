using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public enum ParagraphBreaksDialogField
{
    Left,
    Right,
    SpecialAmount,
    SpaceBefore,
    SpaceAfter,
    LineSpacing
}

public sealed record ParagraphBreaksInitialState(
    string LeftText,
    string RightText,
    string SpaceBeforeText,
    string SpaceAfterText,
    string LineSpacingText,
    int SpecialIndex,
    string SpecialAmountText,
    bool SpecialAmountEnabled,
    bool KeepWithNext,
    bool KeepLinesTogether,
    bool WidowControl,
    bool PageBreakBefore,
    bool SuppressAutoHyphens,
    bool SuppressLineNumbers,
    bool ContextualSpacing);

public sealed record ParagraphBreaksDialogInput(
    string? LeftText,
    string? RightText,
    int SpecialIndex,
    string? SpecialAmountText,
    string? SpaceBeforeText,
    string? SpaceAfterText,
    string? LineSpacingText,
    bool KeepWithNext,
    bool KeepLinesTogether,
    bool WidowControl,
    bool PageBreakBefore,
    bool SuppressAutoHyphens,
    bool SuppressLineNumbers,
    bool ContextualSpacing);

public sealed record ParagraphBreaksValidation(
    ParagraphBreaksDialogField Field,
    string Message);

public sealed record ParagraphBreaksDialogResult(
    double LeftPt,
    double RightPt,
    double FirstLinePt,
    double SpaceBeforePt,
    double SpaceAfterPt,
    double LineSpacing,
    bool KeepWithNext,
    bool KeepLinesTogether,
    bool WidowControl,
    bool PageBreakBefore,
    bool SuppressAutoHyphens,
    bool SuppressLineNumbers,
    bool ContextualSpacing);

public static class ParagraphBreaksDialogPlanner
{
    public const string LeftIndentAutomationId = "paragraph-left-indent";
    public const string ValidationMessage =
        "Enter valid non-negative values in points; line spacing must be positive.";

    public static ParagraphBreaksInitialState BuildInitialState(
        ParagraphFormatting current,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(culture);

        var indentState = ParagraphIndentDialogPlanner.BuildInitialState(
            current.IndentLeftPt,
            current.IndentRightPt,
            current.FirstLineIndentPt,
            culture);

        return new ParagraphBreaksInitialState(
            LeftText: indentState.LeftText,
            RightText: indentState.RightText,
            SpaceBeforeText: ParagraphIndentDialogPlanner.FormatPoints(current.SpaceBeforePt, culture),
            SpaceAfterText: ParagraphIndentDialogPlanner.FormatPoints(current.SpaceAfterPt, culture),
            LineSpacingText: ParagraphIndentDialogPlanner.FormatPoints(current.LineSpacing, culture),
            SpecialIndex: indentState.SpecialIndex,
            SpecialAmountText: indentState.SpecialAmountText,
            SpecialAmountEnabled: indentState.SpecialAmountEnabled,
            KeepWithNext: current.KeepWithNext,
            KeepLinesTogether: current.KeepLinesTogether,
            WidowControl: current.WidowControl,
            PageBreakBefore: current.PageBreakBefore,
            SuppressAutoHyphens: current.SuppressAutoHyphens,
            SuppressLineNumbers: current.SuppressLineNumbers,
            ContextualSpacing: current.ContextualSpacing is true);
    }

    public static bool IsSpecialAmountEnabled(int specialIndex) =>
        ParagraphIndentDialogPlanner.IsSpecialAmountEnabled(specialIndex);

    public static bool TryBuildResult(
        ParagraphBreaksDialogInput input,
        CultureInfo culture,
        out ParagraphBreaksDialogResult? result,
        out ParagraphBreaksValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        validation = null;

        if (!TryParseNonNegative(input.LeftText, culture, out var left))
        {
            validation = new ParagraphBreaksValidation(ParagraphBreaksDialogField.Left, ValidationMessage);
            return false;
        }

        if (!TryParseNonNegative(input.RightText, culture, out var right))
        {
            validation = new ParagraphBreaksValidation(ParagraphBreaksDialogField.Right, ValidationMessage);
            return false;
        }

        if (!TryParseNonNegative(input.SpecialAmountText, culture, out var specialAmount))
        {
            validation = new ParagraphBreaksValidation(ParagraphBreaksDialogField.SpecialAmount, ValidationMessage);
            return false;
        }

        if (!TryParseNonNegative(input.SpaceBeforeText, culture, out var spaceBefore))
        {
            validation = new ParagraphBreaksValidation(ParagraphBreaksDialogField.SpaceBefore, ValidationMessage);
            return false;
        }

        if (!TryParseNonNegative(input.SpaceAfterText, culture, out var spaceAfter))
        {
            validation = new ParagraphBreaksValidation(ParagraphBreaksDialogField.SpaceAfter, ValidationMessage);
            return false;
        }

        if (!TryParsePositive(input.LineSpacingText, culture, out var lineSpacing))
        {
            validation = new ParagraphBreaksValidation(ParagraphBreaksDialogField.LineSpacing, ValidationMessage);
            return false;
        }

        result = new ParagraphBreaksDialogResult(
            LeftPt: left,
            RightPt: right,
            FirstLinePt: SignedFirstLine(input.SpecialIndex, specialAmount),
            SpaceBeforePt: spaceBefore,
            SpaceAfterPt: spaceAfter,
            LineSpacing: lineSpacing,
            KeepWithNext: input.KeepWithNext,
            KeepLinesTogether: input.KeepLinesTogether,
            WidowControl: input.WidowControl,
            PageBreakBefore: input.PageBreakBefore,
            SuppressAutoHyphens: input.SuppressAutoHyphens,
            SuppressLineNumbers: input.SuppressLineNumbers,
            ContextualSpacing: input.ContextualSpacing);
        return true;
    }

    private static double SignedFirstLine(int specialIndex, double amount) =>
        ParagraphIndentDialogPlanner.SpecialItems[Math.Clamp(
            specialIndex,
            0,
            ParagraphIndentDialogPlanner.SpecialItems.Count - 1)].Value switch
        {
            ParagraphIndentSpecialKind.FirstLine => amount,
            ParagraphIndentSpecialKind.Hanging => -amount,
            _ => 0.0
        };

    private static bool TryParseNonNegative(string? text, CultureInfo culture, out double value)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return double.TryParse(trimmed, NumberStyles.Float, culture, out value) && value >= 0;
    }

    private static bool TryParsePositive(string? text, CultureInfo culture, out double value)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return double.TryParse(trimmed, NumberStyles.Float, culture, out value) && value > 0;
    }
}
