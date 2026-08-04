using System.Globalization;
using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

/// <summary>
/// The values the Table Properties dialog produces, applied onto the caret's table / row / column / cell.
/// </summary>
public sealed record TablePropertiesValues(
    double? PreferredWidthPt,
    TableAlignment Alignment,
    bool TextWrapping,
    double? IndentFromLeftPt,
    TableCellMargins? DefaultCellMargins,
    double? CellSpacingPt,
    double? RowHeightPt,
    TableRowHeightRule RowHeightRule,
    bool AllowRowBreak,
    bool RepeatHeaderRow,
    double? ColumnWidthPt,
    double? CellPreferredWidthPt,
    TableCellVerticalAlignment CellVerticalAlignment,
    TableCellMargins? CellMargins,
    bool CellWrapText,
    bool CellFitText);

/// <summary>
/// The model objects under the caret that seed the Table Properties dialog.
/// </summary>
public sealed record ModelTableContext(Table Table, TableRow? Row, TableCell? Cell);

public sealed record TablePropertiesDialogInitialState(
    string PreferredWidthText,
    bool PreferredWidthOn,
    int AlignmentIndex,
    int WrappingIndex,
    string IndentText,
    string DefaultCellMarginTopText,
    string DefaultCellMarginLeftText,
    string DefaultCellMarginBottomText,
    string DefaultCellMarginRightText,
    string CellSpacingText,
    bool CellSpacingOn,
    string RowHeightText,
    bool RowHeightOn,
    int RowRuleIndex,
    bool AllowRowBreak,
    bool RepeatHeaderRow,
    string ColumnWidthText,
    bool ColumnWidthOn,
    string CellWidthText,
    bool CellWidthOn,
    int CellVerticalAlignmentIndex,
    bool CellMarginsSameAsTable,
    string CellMarginTopText,
    string CellMarginLeftText,
    string CellMarginBottomText,
    string CellMarginRightText,
    bool CellWrapText,
    bool CellFitText);

public sealed record TablePropertiesDialogInput(
    bool PreferredWidthOn,
    string? PreferredWidthText,
    int AlignmentIndex,
    int WrappingIndex,
    string? IndentText,
    string? DefaultCellMarginTopText,
    string? DefaultCellMarginLeftText,
    string? DefaultCellMarginBottomText,
    string? DefaultCellMarginRightText,
    bool CellSpacingOn,
    string? CellSpacingText,
    bool RowHeightOn,
    string? RowHeightText,
    int RowRuleIndex,
    bool AllowRowBreak,
    bool RepeatHeaderRow,
    bool ColumnWidthOn,
    string? ColumnWidthText,
    bool CellWidthOn,
    string? CellWidthText,
    int CellVerticalAlignmentIndex,
    bool CellMarginsSameAsTable,
    string? CellMarginTopText,
    string? CellMarginLeftText,
    string? CellMarginBottomText,
    string? CellMarginRightText,
    bool CellWrapText,
    bool CellFitText);

public static class TablePropertiesDialogPlanner
{
    public const string ValidationMessage =
        "Enter non-negative measurements (in points) for every checked field.";

    public static readonly IReadOnlyList<string> AlignmentNames = ["Left", "Center", "Right"];
    public static readonly IReadOnlyList<TableAlignment> AlignmentValues =
        [TableAlignment.Left, TableAlignment.Center, TableAlignment.Right];

    public static readonly IReadOnlyList<string> WrappingNames = ["None", "Around"];

    public static readonly IReadOnlyList<string> RowRuleNames = ["At least", "Exactly"];
    public static readonly IReadOnlyList<TableRowHeightRule> RowRuleValues =
        [TableRowHeightRule.AtLeast, TableRowHeightRule.Exact];

    public static readonly IReadOnlyList<string> CellVerticalAlignmentNames = ["Top", "Center", "Bottom"];
    public static readonly IReadOnlyList<TableCellVerticalAlignment> CellVerticalAlignmentValues =
        [TableCellVerticalAlignment.Top, TableCellVerticalAlignment.Center, TableCellVerticalAlignment.Bottom];

    public static TablePropertiesDialogInitialState BuildInitialState(
        ModelTableContext context,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(culture);

        var table = context.Table;
        var row = context.Row;
        var cell = context.Cell;
        var defaults = table.DefaultCellMargins ?? TableCellMargins.Default;
        var rowRule = row?.HeightRule == TableRowHeightRule.Exact
            ? TableRowHeightRule.Exact
            : TableRowHeightRule.AtLeast;
        var cellMargins = cell?.Margins ?? defaults;

        return new TablePropertiesDialogInitialState(
            PreferredWidthText: FormatPoints(table.PreferredWidthPt ?? 0, culture),
            PreferredWidthOn: table.PreferredWidthPt is not null,
            AlignmentIndex: Math.Max(0, DialogOptionPolicy.IndexOf(AlignmentValues, table.Alignment)),
            WrappingIndex: table.TextWrapping ? 1 : 0,
            IndentText: FormatPoints(table.IndentFromLeftPt ?? 0, culture),
            DefaultCellMarginTopText: FormatPoints(defaults.TopPt, culture),
            DefaultCellMarginLeftText: FormatPoints(defaults.LeftPt, culture),
            DefaultCellMarginBottomText: FormatPoints(defaults.BottomPt, culture),
            DefaultCellMarginRightText: FormatPoints(defaults.RightPt, culture),
            CellSpacingText: FormatPoints(table.CellSpacingPt ?? 0, culture),
            CellSpacingOn: table.CellSpacingPt is not null,
            RowHeightText: FormatPoints(row?.HeightPt ?? 0, culture),
            RowHeightOn: row?.HeightPt is not null,
            RowRuleIndex: Math.Max(0, DialogOptionPolicy.IndexOf(RowRuleValues, rowRule)),
            AllowRowBreak: row?.AllowBreakAcrossPages ?? true,
            RepeatHeaderRow: table.Formatting.RepeatHeaderRow,
            ColumnWidthText: FormatPoints(cell?.WidthPt ?? 0, culture),
            ColumnWidthOn: cell?.WidthPt is not null,
            CellWidthText: FormatPoints(cell?.WidthPt ?? 0, culture),
            CellWidthOn: cell?.WidthPt is not null,
            CellVerticalAlignmentIndex: Math.Max(0, DialogOptionPolicy.IndexOf(
                CellVerticalAlignmentValues,
                cell?.VerticalAlignment ?? TableCellVerticalAlignment.Top)),
            CellMarginsSameAsTable: cell?.Margins is null,
            CellMarginTopText: FormatPoints(cellMargins.TopPt, culture),
            CellMarginLeftText: FormatPoints(cellMargins.LeftPt, culture),
            CellMarginBottomText: FormatPoints(cellMargins.BottomPt, culture),
            CellMarginRightText: FormatPoints(cellMargins.RightPt, culture),
            CellWrapText: cell?.WrapText ?? true,
            CellFitText: cell?.FitText ?? false);
    }

    public static bool TryBuildResult(
        TablePropertiesDialogInput input,
        CultureInfo culture,
        out TablePropertiesValues? result,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        errorMessage = null;

        if (!DialogNumericTextPolicy.TryParseOptionalNonNegativeDouble(input.PreferredWidthOn, input.PreferredWidthText, culture, out var preferredWidth)
            || !DialogNumericTextPolicy.TryParseNonNegativeDouble(input.IndentText, culture, out var indent)
            || !DialogNumericTextPolicy.TryParseNonNegativeDouble(input.DefaultCellMarginTopText, culture, out var dmTop)
            || !DialogNumericTextPolicy.TryParseNonNegativeDouble(input.DefaultCellMarginLeftText, culture, out var dmLeft)
            || !DialogNumericTextPolicy.TryParseNonNegativeDouble(input.DefaultCellMarginBottomText, culture, out var dmBottom)
            || !DialogNumericTextPolicy.TryParseNonNegativeDouble(input.DefaultCellMarginRightText, culture, out var dmRight)
            || !DialogNumericTextPolicy.TryParseOptionalNonNegativeDouble(input.CellSpacingOn, input.CellSpacingText, culture, out var cellSpacing)
            || !DialogNumericTextPolicy.TryParseOptionalNonNegativeDouble(input.RowHeightOn, input.RowHeightText, culture, out var rowHeight)
            || !DialogNumericTextPolicy.TryParseOptionalNonNegativeDouble(input.ColumnWidthOn, input.ColumnWidthText, culture, out var columnWidth)
            || !DialogNumericTextPolicy.TryParseOptionalNonNegativeDouble(input.CellWidthOn, input.CellWidthText, culture, out var cellWidth)
            || !DialogNumericTextPolicy.TryParseNonNegativeDouble(input.CellMarginTopText, culture, out var cmTop)
            || !DialogNumericTextPolicy.TryParseNonNegativeDouble(input.CellMarginLeftText, culture, out var cmLeft)
            || !DialogNumericTextPolicy.TryParseNonNegativeDouble(input.CellMarginBottomText, culture, out var cmBottom)
            || !DialogNumericTextPolicy.TryParseNonNegativeDouble(input.CellMarginRightText, culture, out var cmRight))
        {
            errorMessage = ValidationMessage;
            return false;
        }

        result = new TablePropertiesValues(
            PreferredWidthPt: preferredWidth,
            Alignment: DialogOptionPolicy.ValueAtOrDefault(AlignmentValues, input.AlignmentIndex),
            TextWrapping: input.WrappingIndex == 1,
            IndentFromLeftPt: indent > 0 ? indent : null,
            DefaultCellMargins: new TableCellMargins(dmTop, dmLeft, dmBottom, dmRight),
            CellSpacingPt: cellSpacing,
            RowHeightPt: rowHeight,
            RowHeightRule: rowHeight is null
                ? TableRowHeightRule.Auto
                : DialogOptionPolicy.ValueAtOrDefault(RowRuleValues, input.RowRuleIndex),
            AllowRowBreak: input.AllowRowBreak,
            RepeatHeaderRow: input.RepeatHeaderRow,
            ColumnWidthPt: columnWidth,
            CellPreferredWidthPt: cellWidth,
            CellVerticalAlignment: DialogOptionPolicy.ValueAtOrDefault(
                CellVerticalAlignmentValues,
                input.CellVerticalAlignmentIndex),
            CellMargins: input.CellMarginsSameAsTable ? null : new TableCellMargins(cmTop, cmLeft, cmBottom, cmRight),
            CellWrapText: input.CellWrapText,
            CellFitText: input.CellFitText);
        return true;
    }

    public static void ApplyValues(ModelTableContext context, TablePropertiesValues values)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(values);

        var table = context.Table;

        table.PreferredWidthPt = values.PreferredWidthPt;
        table.Alignment = values.Alignment;
        table.IndentFromLeftPt = values.IndentFromLeftPt;
        table.TextWrapping = values.TextWrapping;
        table.DefaultCellMargins = values.DefaultCellMargins;
        table.CellSpacingPt = values.CellSpacingPt;
        TableLayoutOperations.UpdateFormatting(
            table,
            formatting => formatting with { RepeatHeaderRow = values.RepeatHeaderRow });

        if (context.Row is { } row)
        {
            row.HeightPt = values.RowHeightPt;
            row.HeightRule = values.RowHeightRule;
            row.AllowBreakAcrossPages = values.AllowRowBreak;
        }

        var columnIndex = context.Row is not null && context.Cell is not null
            ? context.Row.Cells.IndexOf(context.Cell)
            : -1;
        if (values.ColumnWidthPt is not null)
            TableLayoutOperations.SetColumnWidth(table, columnIndex, values.ColumnWidthPt);

        if (context.Cell is { } cell)
        {
            if (values.CellPreferredWidthPt is { } cellWidthPt)
                cell.WidthPt = cellWidthPt;
            cell.VerticalAlignment = values.CellVerticalAlignment;
            cell.Margins = values.CellMargins;
            cell.WrapText = values.CellWrapText;
            cell.FitText = values.CellFitText;
        }
    }

    public static string FormatPoints(double value, CultureInfo culture)
        => DialogNumericTextPolicy.FormatPoints(value, culture);
}
