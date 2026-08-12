using System.Globalization;

namespace FreeW.App.Presentation.Dialogs;

public enum ParagraphIndentSpecialKind
{
    None,
    FirstLine,
    Hanging
}

public enum ParagraphIndentDialogField
{
    Left,
    Right,
    SpecialAmount,
    Special,
}

public sealed record ParagraphIndentSpecialChoice(string Label, ParagraphIndentSpecialKind Value);

public sealed record ParagraphIndentInitialState(
    string LeftText,
    string RightText,
    int SpecialIndex,
    string SpecialAmountText,
    bool SpecialAmountEnabled);

public sealed record ParagraphIndentDialogInput(
    string? LeftText,
    string? RightText,
    int SpecialIndex,
    string? SpecialAmountText);

public sealed record ParagraphIndentValidation(
    ParagraphIndentDialogField Field,
    string Message);

public sealed record ParagraphIndentDialogResult(
    double LeftPt,
    double RightPt,
    double FirstLinePt);

public static class ParagraphIndentDialogPlanner
{
    public const string ValidationMessage = "Enter non-negative indent values in points.";

    public static DialogSurfaceSpec<ParagraphIndentDialogField> CompactSurface { get; } = new(
        Title: "Paragraph",
        AutomationId: "ParagraphIndentDialog",
        AutomationName: "Paragraph",
        Fields:
        [
            new(ParagraphIndentDialogField.Left, "Left (pt):", "ParagraphIndentLeftTextBox", "Left indent"),
            new(ParagraphIndentDialogField.Right, "Right (pt):", "ParagraphIndentRightTextBox", "Right indent"),
            new(ParagraphIndentDialogField.Special, "Special:", "ParagraphIndentSpecialComboBox", "Special indent"),
            new(ParagraphIndentDialogField.SpecialAmount, "By (pt):", "ParagraphIndentByTextBox", "Special indent amount"),
        ],
        ValidationAutomationId: "ParagraphIndentValidationMessage");

    public static readonly IReadOnlyList<ParagraphIndentSpecialChoice> SpecialItems =
    [
        new("(none)", ParagraphIndentSpecialKind.None),
        new("First line", ParagraphIndentSpecialKind.FirstLine),
        new("Hanging", ParagraphIndentSpecialKind.Hanging),
    ];

    public static ParagraphIndentInitialState BuildInitialState(
        double leftPt,
        double rightPt,
        double firstLinePt,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var special = SpecialKindFromSignedFirstLine(firstLinePt);
        return new ParagraphIndentInitialState(
            LeftText: DialogNumericTextPolicy.FormatPoints(leftPt, culture),
            RightText: DialogNumericTextPolicy.FormatPoints(rightPt, culture),
            SpecialIndex: (int)special,
            SpecialAmountText: DialogNumericTextPolicy.FormatPoints(Math.Abs(firstLinePt), culture),
            SpecialAmountEnabled: special != ParagraphIndentSpecialKind.None);
    }

    public static bool IsSpecialAmountEnabled(int specialIndex) =>
        ChoiceAt(SpecialItems, specialIndex).Value != ParagraphIndentSpecialKind.None;

    public static bool TryBuildResult(
        ParagraphIndentDialogInput input,
        CultureInfo culture,
        out ParagraphIndentDialogResult? result,
        out ParagraphIndentValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        validation = null;

        if (!DialogNumericTextPolicy.TryParseNonNegativeDouble(input.LeftText, culture, out var left))
        {
            validation = new ParagraphIndentValidation(ParagraphIndentDialogField.Left, ValidationMessage);
            return false;
        }

        if (!DialogNumericTextPolicy.TryParseNonNegativeDouble(input.RightText, culture, out var right))
        {
            validation = new ParagraphIndentValidation(ParagraphIndentDialogField.Right, ValidationMessage);
            return false;
        }

        if (!DialogNumericTextPolicy.TryParseNonNegativeDouble(input.SpecialAmountText, culture, out var amount))
        {
            validation = new ParagraphIndentValidation(ParagraphIndentDialogField.SpecialAmount, ValidationMessage);
            return false;
        }

        result = new ParagraphIndentDialogResult(
            LeftPt: left,
            RightPt: right,
            FirstLinePt: SignedFirstLineFromSpecial(input.SpecialIndex, amount));
        return true;
    }

    private static ParagraphIndentSpecialKind SpecialKindFromSignedFirstLine(double firstLinePt) =>
        firstLinePt > 0
            ? ParagraphIndentSpecialKind.FirstLine
            : firstLinePt < 0
                ? ParagraphIndentSpecialKind.Hanging
                : ParagraphIndentSpecialKind.None;

    internal static double SignedFirstLineFromSpecial(int specialIndex, double amount) =>
        ChoiceAt(SpecialItems, specialIndex).Value switch
        {
            ParagraphIndentSpecialKind.FirstLine => amount,
            ParagraphIndentSpecialKind.Hanging => -amount,
            _ => 0.0
        };

    private static ParagraphIndentSpecialChoice ChoiceAt(
        IReadOnlyList<ParagraphIndentSpecialChoice> choices,
        int index) =>
        choices[Math.Clamp(index, 0, choices.Count - 1)];
}
