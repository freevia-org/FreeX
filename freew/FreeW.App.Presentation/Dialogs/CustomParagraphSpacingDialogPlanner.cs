using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public enum CustomParagraphSpacingDialogField
{
    SpaceBefore,
    SpaceAfter,
    LineSpacing
}

public sealed record CustomParagraphSpacingInitialState(
    string SpaceBeforeText,
    string SpaceAfterText,
    string LineSpacingText);

public sealed record CustomParagraphSpacingDialogInput(
    string? SpaceBeforeText,
    string? SpaceAfterText,
    string? LineSpacingText);

public sealed record CustomParagraphSpacingValidation(
    CustomParagraphSpacingDialogField Field,
    string Message);

public static class CustomParagraphSpacingDialogPlanner
{
    public const string Title = "Custom Paragraph Spacing";
    public const string Hint = "All values in points (pt). Line spacing is a multiple (for example, 1.15 = 115%).";
    public const string SpaceBeforeLabel = "Space before (pt):";
    public const string SpaceAfterLabel = "Space after (pt):";
    public const string LineSpacingLabel = "Line spacing (x):";
    public const string SpaceBeforeValidationMessage = "Space before must be between 0 and 200 pt.";
    public const string SpaceAfterValidationMessage = "Space after must be between 0 and 200 pt.";
    public const string LineSpacingValidationMessage = "Line spacing must be between 0.01 and 10.";
    public const string AutomationId = "CustomParagraphSpacingDialog";
    public const string SpaceBeforeAutomationId = "CustomParagraphSpacingBefore";
    public const string SpaceAfterAutomationId = "CustomParagraphSpacingAfter";
    public const string LineSpacingAutomationId = "CustomParagraphSpacingLine";

    public static CustomParagraphSpacingInitialState BuildInitialState(
        DocumentParagraphSpacingSet? current,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var seed = current ?? DocumentParagraphSpacingSet.Default;
        return new CustomParagraphSpacingInitialState(
            SpaceBeforeText: FormatNumber(seed.SpaceBeforePt, culture),
            SpaceAfterText: FormatNumber(seed.SpaceAfterPt, culture),
            LineSpacingText: FormatNumber(seed.LineSpacing, culture));
    }

    public static bool TryBuildResult(
        CustomParagraphSpacingDialogInput input,
        CultureInfo culture,
        out DocumentParagraphSpacingSet? result,
        out CustomParagraphSpacingValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        validation = null;

        if (!TryParseDouble(input.SpaceBeforeText, culture, out var before) || before < 0 || before > 200)
        {
            validation = new CustomParagraphSpacingValidation(
                CustomParagraphSpacingDialogField.SpaceBefore,
                SpaceBeforeValidationMessage);
            return false;
        }

        if (!TryParseDouble(input.SpaceAfterText, culture, out var after) || after < 0 || after > 200)
        {
            validation = new CustomParagraphSpacingValidation(
                CustomParagraphSpacingDialogField.SpaceAfter,
                SpaceAfterValidationMessage);
            return false;
        }

        if (!TryParseDouble(input.LineSpacingText, culture, out var lineSpacing) || lineSpacing <= 0 || lineSpacing > 10)
        {
            validation = new CustomParagraphSpacingValidation(
                CustomParagraphSpacingDialogField.LineSpacing,
                LineSpacingValidationMessage);
            return false;
        }

        result = new DocumentParagraphSpacingSet("Custom", before, after, lineSpacing);
        return true;
    }

    public static string FormatNumber(double value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value.ToString("0.##", culture);
    }

    private static bool TryParseDouble(string? text, CultureInfo culture, out double value)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return double.TryParse(trimmed, NumberStyles.Float, culture, out value);
    }
}
