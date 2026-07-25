namespace FreeX.Core.Commands;

/// <summary>
/// Excel's Accessibility Checker groups inspection results by severity. Errors are content that
/// people with disabilities cannot access at all; Warnings are content that is difficult for most
/// (but not all) people with disabilities to access; Tips are content that people with disabilities
/// can access but that could be better organized.
/// </summary>
public enum AccessibilitySeverity
{
    Error,
    Warning,
    Tip,
}

/// <summary>
/// Describes how an <see cref="AccessibilityIssueKind"/> maps onto Excel's Accessibility Checker
/// task pane: its severity bucket (Errors / Warnings / Tips), the issue-type heading that groups
/// affected objects (e.g. "Missing alternative text"), and the "Why Fix" / "How To Fix" guidance
/// shown in the Additional Information area when an issue is selected.
/// </summary>
/// <remarks>
/// All English strings here are presentation defaults. Shells route the heading and guidance through
/// their localization catalog using the <see cref="LabelKey"/>, <see cref="WhyFixKey"/>, and
/// <see cref="HowToFixKey"/> resource keys, falling back to these defaults when a key is absent.
/// </remarks>
public sealed record AccessibilityIssueDescriptor(
    AccessibilitySeverity Severity,
    string LabelKey,
    string Label,
    string WhyFixKey,
    string WhyFix,
    string HowToFixKey,
    string HowToFix);

public static class AccessibilityIssueClassification
{
    /// <summary>Order in which the severity groups appear in the task pane.</summary>
    public static readonly IReadOnlyList<AccessibilitySeverity> SeverityOrder = new[]
    {
        AccessibilitySeverity.Error,
        AccessibilitySeverity.Warning,
        AccessibilitySeverity.Tip,
    };

    public static AccessibilitySeverity GetSeverity(AccessibilityIssueKind kind) =>
        Describe(kind).Severity;

    public static AccessibilityIssueDescriptor Describe(AccessibilityIssueKind kind) => kind switch
    {
        // ---- Errors: content that is inaccessible to people with disabilities --------------------
        AccessibilityIssueKind.MissingAltText => new(
            AccessibilitySeverity.Error,
            "AccessibilityChecker_GroupMissingAltText", "Missing alternative text",
            "AccessibilityChecker_WhyMissingAltText",
            "People who are blind or have low vision rely on alternative text to understand pictures, shapes, and other objects.",
            "AccessibilityChecker_HowMissingAltText",
            "Add alternative text that describes the object's content and purpose."),

        AccessibilityIssueKind.ChartMissingTitle => new(
            AccessibilitySeverity.Error,
            "AccessibilityChecker_GroupMissingAltText", "Missing alternative text",
            "AccessibilityChecker_WhyMissingAltText",
            "People who are blind or have low vision rely on a chart title to understand what the chart shows.",
            "AccessibilityChecker_HowMissingChartTitle",
            "Add a descriptive title to the chart."),

        AccessibilityIssueKind.ChartMissingAxisTitle => new(
            AccessibilitySeverity.Error,
            "AccessibilityChecker_GroupMissingAltText", "Missing alternative text",
            "AccessibilityChecker_WhyMissingAltText",
            "People who are blind or have low vision rely on axis titles to understand what a chart measures.",
            "AccessibilityChecker_HowMissingAxisTitle",
            "Add a descriptive title to each chart axis."),

        AccessibilityIssueKind.TableMissingHeaderRow => new(
            AccessibilitySeverity.Error,
            "AccessibilityChecker_GroupMissingTableHeader", "Missing table header",
            "AccessibilityChecker_WhyTableHeader",
            "Screen readers use table headers to help people navigate a table and understand its data.",
            "AccessibilityChecker_HowTableHeaderRow",
            "Turn on the header row for the table."),

        AccessibilityIssueKind.TableMissingHeaderText => new(
            AccessibilitySeverity.Error,
            "AccessibilityChecker_GroupMissingTableHeader", "Missing table header",
            "AccessibilityChecker_WhyTableHeader",
            "Screen readers use table headers to help people navigate a table and understand its data.",
            "AccessibilityChecker_HowTableHeaderText",
            "Enter descriptive text in every table header cell."),

        // ---- Warnings: content difficult for most people with disabilities to access -------------
        AccessibilityIssueKind.GenericAltText => new(
            AccessibilitySeverity.Warning,
            "AccessibilityChecker_GroupGenericAltText", "Hard-to-understand alternative text",
            "AccessibilityChecker_WhyGenericAltText",
            "Generic alternative text such as a file name does not describe what an object shows.",
            "AccessibilityChecker_HowGenericAltText",
            "Replace the alternative text with a description of the object's content and purpose."),

        AccessibilityIssueKind.GenericChartTitle => new(
            AccessibilitySeverity.Warning,
            "AccessibilityChecker_GroupGenericAltText", "Hard-to-understand alternative text",
            "AccessibilityChecker_WhyGenericAltText",
            "A generic chart title does not describe what the chart shows.",
            "AccessibilityChecker_HowGenericChartTitle",
            "Replace the chart title with a description of what the chart shows."),

        AccessibilityIssueKind.GenericChartAxisTitle => new(
            AccessibilitySeverity.Warning,
            "AccessibilityChecker_GroupGenericAltText", "Hard-to-understand alternative text",
            "AccessibilityChecker_WhyGenericAltText",
            "A generic axis title does not describe what the axis measures.",
            "AccessibilityChecker_HowGenericAxisTitle",
            "Replace the axis title with a description of what the axis measures."),

        AccessibilityIssueKind.LowContrastCellText => new(
            AccessibilitySeverity.Warning,
            "AccessibilityChecker_GroupLowContrast", "Hard-to-read text contrast",
            "AccessibilityChecker_WhyLowContrast",
            "People with low vision or color blindness may not be able to read text that has low contrast with its background.",
            "AccessibilityChecker_HowLowContrast",
            "Increase the contrast between the text and its background color."),

        AccessibilityIssueKind.LowContrastChartText => new(
            AccessibilitySeverity.Warning,
            "AccessibilityChecker_GroupLowContrast", "Hard-to-read text contrast",
            "AccessibilityChecker_WhyLowContrast",
            "People with low vision or color blindness may not be able to read chart text that has low contrast with its background.",
            "AccessibilityChecker_HowLowContrast",
            "Increase the contrast between the text and its background color."),

        AccessibilityIssueKind.LowContrastObjectText => new(
            AccessibilitySeverity.Warning,
            "AccessibilityChecker_GroupLowContrast", "Hard-to-read text contrast",
            "AccessibilityChecker_WhyLowContrast",
            "People with low vision or color blindness may not be able to read object text that has low contrast with its background.",
            "AccessibilityChecker_HowLowContrast",
            "Increase the contrast between the text and its background color."),

        AccessibilityIssueKind.MergedCells => new(
            AccessibilitySeverity.Warning,
            "AccessibilityChecker_GroupMergedCells", "Merged cells",
            "AccessibilityChecker_WhyMergedCells",
            "Merged cells make it harder for people who use screen readers to navigate a worksheet.",
            "AccessibilityChecker_HowMergedCells",
            "Unmerge the cells so the data reads in a predictable order."),

        AccessibilityIssueKind.HyperlinkDisplayTextIsUrl => new(
            AccessibilitySeverity.Warning,
            "AccessibilityChecker_GroupHyperlinkText", "Unclear hyperlink text",
            "AccessibilityChecker_WhyHyperlinkText",
            "Screen readers read hyperlink display text aloud, so a raw URL does not tell people where the link goes.",
            "AccessibilityChecker_HowHyperlinkText",
            "Change the display text to describe the link destination."),

        AccessibilityIssueKind.TableDefaultHeaderText => new(
            AccessibilitySeverity.Warning,
            "AccessibilityChecker_GroupDefaultTableHeader", "Default table header text",
            "AccessibilityChecker_WhyDefaultTableHeader",
            "Default header text such as Column1 does not describe what a table column contains.",
            "AccessibilityChecker_HowDefaultTableHeader",
            "Replace the default header text with a description of each column."),

        AccessibilityIssueKind.TableDuplicateHeaderText => new(
            AccessibilitySeverity.Warning,
            "AccessibilityChecker_GroupDuplicateTableHeader", "Duplicate table header text",
            "AccessibilityChecker_WhyDuplicateTableHeader",
            "Duplicate header text makes it hard for screen reader users to tell table columns apart.",
            "AccessibilityChecker_HowDuplicateTableHeader",
            "Give each table column a unique header."),

        // R90-app-accessibility-checker-5-1: previously fell through to the generic "Other
        // accessibility issues" default arm below, which mis-bucketed it as a Tip and showed
        // boilerplate guidance instead of a dedicated heading.
        AccessibilityIssueKind.BlankRowOrColumnInTable => new(
            AccessibilitySeverity.Warning,
            "AccessibilityChecker_GroupBlankTableRowOrColumn", "Blank rows or columns in a table",
            "AccessibilityChecker_WhyBlankTableRowOrColumn",
            "A screen reader can interpret a fully blank row or column as the end of the table.",
            "AccessibilityChecker_HowBlankTableRowOrColumn",
            "Remove the blank row or column, or fill it with data."),

        // ---- Tips: accessible content that could be better organized -----------------------------
        AccessibilityIssueKind.DefaultWorksheetName => new(
            AccessibilitySeverity.Tip,
            "AccessibilityChecker_GroupDefaultSheetNames", "Default sheet names",
            "AccessibilityChecker_WhyDefaultSheetNames",
            "Descriptive sheet names help everyone, including people who use screen readers, find their way around a workbook.",
            "AccessibilityChecker_HowDefaultSheetNames",
            "Rename the sheet tab to describe its contents."),

        AccessibilityIssueKind.HiddenSheetWithContent => new(
            AccessibilitySeverity.Tip,
            "AccessibilityChecker_GroupHiddenContent", "Hidden content",
            "AccessibilityChecker_WhyHiddenContent",
            "Hidden sheets, rows, and columns may contain information that people who use assistive technology miss.",
            "AccessibilityChecker_HowHiddenSheet",
            "Unhide the sheet if its content should be available to everyone."),

        AccessibilityIssueKind.HiddenRowWithContent => new(
            AccessibilitySeverity.Tip,
            "AccessibilityChecker_GroupHiddenContent", "Hidden content",
            "AccessibilityChecker_WhyHiddenContent",
            "Hidden sheets, rows, and columns may contain information that people who use assistive technology miss.",
            "AccessibilityChecker_HowHiddenRow",
            "Unhide the rows if their content should be available to everyone."),

        AccessibilityIssueKind.HiddenColumnWithContent => new(
            AccessibilitySeverity.Tip,
            "AccessibilityChecker_GroupHiddenContent", "Hidden content",
            "AccessibilityChecker_WhyHiddenContent",
            "Hidden sheets, rows, and columns may contain information that people who use assistive technology miss.",
            "AccessibilityChecker_HowHiddenColumn",
            "Unhide the columns if their content should be available to everyone."),

        _ => new(
            AccessibilitySeverity.Tip,
            "AccessibilityChecker_GroupOther", "Other accessibility issues",
            "AccessibilityChecker_WhyOther",
            "Review this item to make the workbook easier for people with disabilities to use.",
            "AccessibilityChecker_HowOther",
            "Follow the guidance for this issue to improve accessibility."),
    };
}
