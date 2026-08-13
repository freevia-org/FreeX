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
    LineSpacing,
    Special,
    ContextualSpacing,
    KeepWithNext,
    KeepLinesTogether,
    WidowControl,
    PageBreakBefore,
    SuppressAutoHyphens,
    SuppressLineNumbers,
    IndentsAndSpacingTab,
    LineAndPageBreaksTab,
    PaginationSection,
    FormattingExceptionsSection,
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

public readonly record struct ParagraphDialogThickness(
    double Left,
    double Top,
    double Right,
    double Bottom);

/// <summary>
/// WPF-authority layout metrics for the paired Paragraph dialogs. Avalonia-prefixed values are
/// native-template compensation required to reproduce that authority; renderers only translate
/// these neutral values into their toolkit geometry types.
/// </summary>
public sealed record ParagraphDialogVisualMetrics
{
    public double WindowWidth { get; init; } = 380;
    public double NumericFieldMinWidth { get; init; } = 120;
    public double ActionButtonWidth { get; init; } = 72;
    public ParagraphDialogThickness WpfRootMargin { get; init; } = new(12, 12, 12, 12);
    public ParagraphDialogThickness WpfTabContentMargin { get; init; } = new(10, 10, 10, 10);
    public ParagraphDialogThickness FieldLabelMargin { get; init; } = new(0, 4, 8, 4);
    public ParagraphDialogThickness FieldControlMargin { get; init; } = new(0, 4, 0, 4);
    public ParagraphDialogThickness WpfContextualSpacingMargin { get; init; } = new(0, 4, 0, 0);
    public ParagraphDialogThickness CheckBoxMargin { get; init; } = new(0, 0, 0, 6);
    public ParagraphDialogThickness SectionHeadingMargin { get; init; } = new(0, 0, 0, 8);
    public ParagraphDialogThickness SectionSeparatorMargin { get; init; } = new(0, 4, 0, 8);
    public ParagraphDialogThickness WpfActionRowMargin { get; init; } = new(0, 10, 0, 0);
    public ParagraphDialogThickness AvaloniaTabsMargin { get; init; } = new(12, 12, 11, 0);
    public ParagraphDialogThickness AvaloniaIndentsTabContentMargin { get; init; } = new(9, 12, 12, 10);
    public ParagraphDialogThickness AvaloniaContextualSpacingMargin { get; init; } = new(3, 4, 0, 0);
    public ParagraphDialogThickness AvaloniaTabPaneMargin { get; init; } = new(0, -1, 0, 0);
    public ParagraphDialogThickness AvaloniaValidationMargin { get; init; } = new(12, 8, 11, 0);
    public ParagraphDialogThickness AvaloniaActionRowMargin { get; init; } = new(12, 10, 11, 11);
    public double AvaloniaLabelColumnWidth { get; init; } = 104;
    public double AvaloniaIndentsTabHeight { get; init; } = 253;
    public double AvaloniaBreaksTabHeight { get; init; } = 235;
    public double AvaloniaIndentsTabHeaderWidth { get; init; } = 123;
    public double AvaloniaBreaksTabHeaderWidth { get; init; } = 122;
}

public static class ParagraphBreaksDialogPlanner
{
    public const string LeftIndentAutomationId = "paragraph-left-indent";
    public const string ValidationMessage =
        "Enter valid non-negative values in points; line spacing must be positive.";

    public static ParagraphDialogVisualMetrics VisualMetrics { get; } = new();

    public static DialogSurfaceSpec<ParagraphBreaksDialogField> Surface { get; } = new(
        Title: "Paragraph",
        AutomationId: "ParagraphDialog",
        AutomationName: "Paragraph",
        Fields:
        [
            new(ParagraphBreaksDialogField.Left, "Left indent (pt):", LeftIndentAutomationId, "Left indent"),
            new(ParagraphBreaksDialogField.Right, "Right indent (pt):", "paragraph-right-indent", "Right indent"),
            new(ParagraphBreaksDialogField.Special, "Special:", "paragraph-special-indent", "Special indent"),
            new(ParagraphBreaksDialogField.SpecialAmount, "By (pt):", "paragraph-special-indent-amount", "Special indent amount"),
            new(ParagraphBreaksDialogField.SpaceBefore, "Space before (pt):", "paragraph-space-before", "Space before"),
            new(ParagraphBreaksDialogField.SpaceAfter, "Space after (pt):", "paragraph-space-after", "Space after"),
            new(ParagraphBreaksDialogField.LineSpacing, "Line spacing (\u00d7):", "paragraph-line-spacing", "Line spacing"),
            new(ParagraphBreaksDialogField.ContextualSpacing, "Don't add space between paragraphs of the same style", "paragraph-contextual-spacing", "Contextual paragraph spacing"),
            new(ParagraphBreaksDialogField.KeepWithNext, "Keep with next", "paragraph-keep-with-next", "Keep with next"),
            new(ParagraphBreaksDialogField.KeepLinesTogether, "Keep lines together", "paragraph-keep-lines-together", "Keep lines together"),
            new(ParagraphBreaksDialogField.WidowControl, "Widow/orphan control", "paragraph-widow-control", "Widow or orphan control"),
            new(ParagraphBreaksDialogField.PageBreakBefore, "Page break before", "paragraph-page-break-before", "Page break before"),
            new(ParagraphBreaksDialogField.SuppressAutoHyphens, "Suppress auto-hyphenation", "paragraph-suppress-auto-hyphenation", "Suppress automatic hyphenation"),
            new(ParagraphBreaksDialogField.SuppressLineNumbers, "Suppress line numbers", "paragraph-suppress-line-numbers", "Suppress line numbers"),
            new(ParagraphBreaksDialogField.IndentsAndSpacingTab, "Indents and Spacing", "paragraph-indents-spacing-tab", "Indents and Spacing tab"),
            new(ParagraphBreaksDialogField.LineAndPageBreaksTab, "Line and Page Breaks", "paragraph-line-page-breaks-tab", "Line and Page Breaks tab"),
            new(ParagraphBreaksDialogField.PaginationSection, "Pagination", "paragraph-pagination-section", "Pagination"),
            new(ParagraphBreaksDialogField.FormattingExceptionsSection, "Formatting exceptions", "paragraph-formatting-exceptions-section", "Formatting exceptions"),
        ],
        ValidationAutomationId: "paragraph-validation-message");

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
            SpaceBeforeText: DialogNumericTextPolicy.FormatPoints(current.SpaceBeforePt, culture),
            SpaceAfterText: DialogNumericTextPolicy.FormatPoints(current.SpaceAfterPt, culture),
            LineSpacingText: DialogNumericTextPolicy.FormatPoints(current.LineSpacing, culture),
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

        if (!DialogNumericTextPolicy.TryParseNonNegativeDouble(input.LeftText, culture, out var left))
        {
            validation = new ParagraphBreaksValidation(ParagraphBreaksDialogField.Left, ValidationMessage);
            return false;
        }

        if (!DialogNumericTextPolicy.TryParseNonNegativeDouble(input.RightText, culture, out var right))
        {
            validation = new ParagraphBreaksValidation(ParagraphBreaksDialogField.Right, ValidationMessage);
            return false;
        }

        if (!DialogNumericTextPolicy.TryParseNonNegativeDouble(input.SpecialAmountText, culture, out var specialAmount))
        {
            validation = new ParagraphBreaksValidation(ParagraphBreaksDialogField.SpecialAmount, ValidationMessage);
            return false;
        }

        if (!DialogNumericTextPolicy.TryParseNonNegativeDouble(input.SpaceBeforeText, culture, out var spaceBefore))
        {
            validation = new ParagraphBreaksValidation(ParagraphBreaksDialogField.SpaceBefore, ValidationMessage);
            return false;
        }

        if (!DialogNumericTextPolicy.TryParseNonNegativeDouble(input.SpaceAfterText, culture, out var spaceAfter))
        {
            validation = new ParagraphBreaksValidation(ParagraphBreaksDialogField.SpaceAfter, ValidationMessage);
            return false;
        }

        if (!DialogNumericTextPolicy.TryParsePositiveDouble(input.LineSpacingText, culture, out var lineSpacing))
        {
            validation = new ParagraphBreaksValidation(ParagraphBreaksDialogField.LineSpacing, ValidationMessage);
            return false;
        }

        result = new ParagraphBreaksDialogResult(
            LeftPt: left,
            RightPt: right,
            FirstLinePt: ParagraphIndentDialogPlanner.SignedFirstLineFromSpecial(input.SpecialIndex, specialAmount),
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

}
