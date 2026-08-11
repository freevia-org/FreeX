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
    bool CellFitText,
    bool? FloatingTableAllowsOverlap = null,
    TableFloatingPosition? FloatingPosition = null);

/// <summary>
/// The model objects under the caret that seed the Table Properties dialog.
/// </summary>
public sealed record ModelTableContext(Table Table, TableRow? Row, TableCell? Cell);

public enum TablePropertiesDialogTabKind
{
    Table,
    Row,
    Column,
    Cell,
}

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
    bool CellFitText,
    int FloatingHorizontalAnchorIndex,
    int FloatingHorizontalModeIndex,
    string FloatingHorizontalOffsetText,
    int FloatingVerticalAnchorIndex,
    int FloatingVerticalModeIndex,
    string FloatingVerticalOffsetText,
    string FloatingDistanceTopText,
    string FloatingDistanceLeftText,
    string FloatingDistanceBottomText,
    string FloatingDistanceRightText,
    bool? FloatingTableAllowsOverlap);

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
    bool CellFitText,
    int FloatingHorizontalAnchorIndex = -1,
    int FloatingHorizontalModeIndex = -1,
    string? FloatingHorizontalOffsetText = null,
    int FloatingVerticalAnchorIndex = -1,
    int FloatingVerticalModeIndex = -1,
    string? FloatingVerticalOffsetText = null,
    string? FloatingDistanceTopText = null,
    string? FloatingDistanceLeftText = null,
    string? FloatingDistanceBottomText = null,
    string? FloatingDistanceRightText = null,
    bool? FloatingTableAllowsOverlap = null);

public sealed record TablePropertiesDialogEnabledState(
    bool FloatingControlsEnabled,
    bool HorizontalOffsetEnabled,
    bool VerticalOffsetEnabled);

public sealed record TablePropertiesDialogFocusPlan(
    TablePropertiesDialogTabKind Tab,
    string TargetAutomationId,
    bool SelectAllOnFocus);

public sealed record TablePropertiesDialogAcceptance(
    TablePropertiesValues? Result,
    string? ValidationMessage)
{
    public bool IsAccepted => Result is not null;
}

public sealed class TablePropertiesDialogSession
{
    private readonly CultureInfo _culture;

    public TablePropertiesDialogSession(
        ModelTableContext context,
        CultureInfo culture,
        TablePropertiesDialogTabKind initialTab = TablePropertiesDialogTabKind.Table)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(culture);
        _culture = culture;
        InitialState = TablePropertiesDialogPlanner.BuildInitialState(context, culture);
        InitialFocusPlan = PlanFocus(initialTab);
    }

    public TablePropertiesDialogInitialState InitialState { get; }
    public TablePropertiesDialogFocusPlan InitialFocusPlan { get; }

    public IReadOnlyList<string> AlignmentNames => TablePropertiesDialogPlanner.AlignmentNames;
    public IReadOnlyList<string> WrappingNames => TablePropertiesDialogPlanner.WrappingNames;
    public IReadOnlyList<string> FloatingHorizontalAnchorNames => TablePropertiesDialogPlanner.FloatingHorizontalAnchorNames;
    public IReadOnlyList<string> FloatingHorizontalModeNames => TablePropertiesDialogPlanner.FloatingHorizontalModeNames;
    public IReadOnlyList<string> FloatingVerticalAnchorNames => TablePropertiesDialogPlanner.FloatingVerticalAnchorNames;
    public IReadOnlyList<string> FloatingVerticalModeNames => TablePropertiesDialogPlanner.FloatingVerticalModeNames;
    public IReadOnlyList<string> RowRuleNames => TablePropertiesDialogPlanner.RowRuleNames;
    public IReadOnlyList<string> CellVerticalAlignmentNames => TablePropertiesDialogPlanner.CellVerticalAlignmentNames;

    public TablePropertiesDialogFocusPlan PlanFocus(TablePropertiesDialogTabKind tab) =>
        tab switch
        {
            TablePropertiesDialogTabKind.Row => new(
                TablePropertiesDialogTabKind.Row,
                TablePropertiesDialogPlanner.RowHeightAutomationId,
                SelectAllOnFocus: true),
            TablePropertiesDialogTabKind.Column => new(
                TablePropertiesDialogTabKind.Column,
                TablePropertiesDialogPlanner.ColumnWidthAutomationId,
                SelectAllOnFocus: true),
            TablePropertiesDialogTabKind.Cell => new(
                TablePropertiesDialogTabKind.Cell,
                TablePropertiesDialogPlanner.CellWidthAutomationId,
                SelectAllOnFocus: true),
            _ => new(
                TablePropertiesDialogTabKind.Table,
                TablePropertiesDialogPlanner.PreferredWidthAutomationId,
                SelectAllOnFocus: true),
        };

    public TablePropertiesDialogEnabledState PlanEnabledState(
        int wrappingIndex,
        int horizontalModeIndex,
        int verticalModeIndex)
    {
        var floating = wrappingIndex == 1;
        return new TablePropertiesDialogEnabledState(
            FloatingControlsEnabled: floating,
            HorizontalOffsetEnabled: floating && horizontalModeIndex == 0,
            VerticalOffsetEnabled: floating && verticalModeIndex == 0);
    }

    public TablePropertiesDialogAcceptance PlanAcceptance(TablePropertiesDialogInput input) =>
        TablePropertiesDialogPlanner.TryBuildResult(input, _culture, out var result, out var error)
            ? new TablePropertiesDialogAcceptance(result, ValidationMessage: null)
            : new TablePropertiesDialogAcceptance(null, error ?? TablePropertiesDialogPlanner.ValidationMessage);
}

public static class TablePropertiesDialogPlanner
{
    public const string Title = "Table Properties";
    public const string TableTabLabel = "Table";
    public const string RowTabLabel = "Row";
    public const string ColumnTabLabel = "Column";
    public const string CellTabLabel = "Cell";
    public const string PreferredWidthLabel = "Preferred width (pt):";
    public const string AlignmentLabel = "Alignment:";
    public const string TextWrappingLabel = "Text wrapping:";
    public const string IndentFromLeftLabel = "Indent from left (pt):";
    public const string DefaultCellMarginsLabel = "Default cell margins (pt):";
    public const string TopLabel = "Top:";
    public const string LeftLabel = "Left:";
    public const string BottomLabel = "Bottom:";
    public const string RightLabel = "Right:";
    public const string CellSpacingLabel = "Allow spacing between cells (pt):";
    public const string SpecifyRowHeightLabel = "Specify height (pt):";
    public const string RowHeightRuleLabel = "Row height is:";
    public const string AllowRowBreakLabel = "Allow row to break across pages";
    public const string RepeatHeaderLabel = "Repeat as header row at the top of each page";
    public const string VerticalAlignmentLabel = "Vertical alignment:";
    public const string SameMarginsLabel = "Same as the whole table";
    public const string WrapTextLabel = "Wrap text";
    public const string FitTextLabel = "Fit text";
    public const string CellMarginsLabel = "Cell margins (pt):";
    public const string PositioningLabel = "Positioning";
    public const string AllowOverlapLabel = "Allow overlap";
    public const string HorizontalRelativeToLabel = "Horizontal relative to:";
    public const string HorizontalAlignmentLabel = "Horizontal alignment:";
    public const string HorizontalPositionLabel = "Horizontal position (pt):";
    public const string VerticalRelativeToLabel = "Vertical relative to:";
    public const string VerticalPositionLabel = "Vertical position (pt):";
    public const string DistanceFromTextLabel = "Distance from surrounding text (pt):";
    public const string AcceptButtonLabel = "OK";
    public const string CancelButtonLabel = "Cancel";
    public const string ValidationMessage =
        "Enter valid point measurements; sizes and text distances cannot be negative.";

    public const string AutomationId = "TablePropertiesDialog";
    public const string TabsAutomationId = "TablePropertiesTabs";
    public const string TableTabAutomationId = "TablePropertiesTableTab";
    public const string RowTabAutomationId = "TablePropertiesRowTab";
    public const string ColumnTabAutomationId = "TablePropertiesColumnTab";
    public const string CellTabAutomationId = "TablePropertiesCellTab";
    public const string PreferredWidthAutomationId = "TablePropertiesPreferredWidthBox";
    public const string PreferredWidthToggleAutomationId = "TablePropertiesPreferredWidthCheckBox";
    public const string AlignmentAutomationId = "TablePropertiesAlignmentBox";
    public const string WrappingAutomationId = "TablePropertiesWrappingBox";
    public const string AllowOverlapAutomationId = "TablePropertiesAllowOverlapCheckBox";
    public const string HorizontalAnchorAutomationId = "TablePropertiesHorizontalAnchorBox";
    public const string HorizontalModeAutomationId = "TablePropertiesHorizontalModeBox";
    public const string HorizontalOffsetAutomationId = "TablePropertiesHorizontalOffsetBox";
    public const string VerticalAnchorAutomationId = "TablePropertiesVerticalAnchorBox";
    public const string VerticalModeAutomationId = "TablePropertiesVerticalModeBox";
    public const string VerticalOffsetAutomationId = "TablePropertiesVerticalOffsetBox";
    public const string DistanceTopAutomationId = "TablePropertiesDistanceTopBox";
    public const string DistanceLeftAutomationId = "TablePropertiesDistanceLeftBox";
    public const string DistanceBottomAutomationId = "TablePropertiesDistanceBottomBox";
    public const string DistanceRightAutomationId = "TablePropertiesDistanceRightBox";
    public const string IndentAutomationId = "TablePropertiesIndentBox";
    public const string DefaultMarginTopAutomationId = "TablePropertiesDefaultMarginTopBox";
    public const string DefaultMarginLeftAutomationId = "TablePropertiesDefaultMarginLeftBox";
    public const string DefaultMarginBottomAutomationId = "TablePropertiesDefaultMarginBottomBox";
    public const string DefaultMarginRightAutomationId = "TablePropertiesDefaultMarginRightBox";
    public const string CellSpacingAutomationId = "TablePropertiesCellSpacingBox";
    public const string CellSpacingToggleAutomationId = "TablePropertiesCellSpacingCheckBox";
    public const string RowHeightAutomationId = "TablePropertiesRowHeightBox";
    public const string RowHeightToggleAutomationId = "TablePropertiesRowHeightCheckBox";
    public const string RowRuleAutomationId = "TablePropertiesRowRuleBox";
    public const string AllowRowBreakAutomationId = "TablePropertiesAllowRowBreakCheckBox";
    public const string RepeatHeaderAutomationId = "TablePropertiesRepeatHeaderCheckBox";
    public const string ColumnWidthAutomationId = "TablePropertiesColumnWidthBox";
    public const string ColumnWidthToggleAutomationId = "TablePropertiesColumnWidthCheckBox";
    public const string CellWidthAutomationId = "TablePropertiesCellWidthBox";
    public const string CellWidthToggleAutomationId = "TablePropertiesCellWidthCheckBox";
    public const string CellVerticalAlignmentAutomationId = "TablePropertiesCellVerticalAlignmentBox";
    public const string CellMarginTopAutomationId = "TablePropertiesCellMarginTopBox";
    public const string CellMarginLeftAutomationId = "TablePropertiesCellMarginLeftBox";
    public const string CellMarginBottomAutomationId = "TablePropertiesCellMarginBottomBox";
    public const string CellMarginRightAutomationId = "TablePropertiesCellMarginRightBox";
    public const string SameMarginsAutomationId = "TablePropertiesSameMarginsCheckBox";
    public const string CellWrapTextAutomationId = "TablePropertiesCellWrapTextCheckBox";
    public const string CellFitTextAutomationId = "TablePropertiesCellFitTextCheckBox";
    public const string ValidationAutomationId = "TablePropertiesValidationText";
    public const string AcceptButtonAutomationId = "TablePropertiesOkButton";
    public const string CancelButtonAutomationId = "TablePropertiesCancelButton";

    public static readonly IReadOnlyList<string> AlignmentNames = ["Left", "Center", "Right"];
    public static readonly IReadOnlyList<TableAlignment> AlignmentValues =
        [TableAlignment.Left, TableAlignment.Center, TableAlignment.Right];

    public static readonly IReadOnlyList<string> WrappingNames = ["None", "Around"];

    public static readonly IReadOnlyList<string> FloatingHorizontalAnchorNames = ["Text", "Margin", "Page"];
    public static readonly IReadOnlyList<TableHorizontalAnchor> FloatingHorizontalAnchorValues =
        [TableHorizontalAnchor.Text, TableHorizontalAnchor.Margin, TableHorizontalAnchor.Page];
    public static readonly IReadOnlyList<string> FloatingHorizontalModeNames =
        ["Position", "Left", "Center", "Right", "Inside", "Outside"];
    public static readonly IReadOnlyList<TableHorizontalPositionAlignment?> FloatingHorizontalModeValues =
        [null, TableHorizontalPositionAlignment.Left, TableHorizontalPositionAlignment.Center,
            TableHorizontalPositionAlignment.Right, TableHorizontalPositionAlignment.Inside,
            TableHorizontalPositionAlignment.Outside];

    public static readonly IReadOnlyList<string> FloatingVerticalAnchorNames = ["Text", "Margin", "Page"];
    public static readonly IReadOnlyList<TableVerticalAnchor> FloatingVerticalAnchorValues =
        [TableVerticalAnchor.Text, TableVerticalAnchor.Margin, TableVerticalAnchor.Page];
    public static readonly IReadOnlyList<string> FloatingVerticalModeNames =
        ["Position", "Inline", "Top", "Center", "Bottom", "Inside", "Outside"];
    public static readonly IReadOnlyList<TableVerticalPositionAlignment?> FloatingVerticalModeValues =
        [null, TableVerticalPositionAlignment.Inline, TableVerticalPositionAlignment.Top,
            TableVerticalPositionAlignment.Center, TableVerticalPositionAlignment.Bottom,
            TableVerticalPositionAlignment.Inside, TableVerticalPositionAlignment.Outside];

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
        var floating = table.FloatingPosition ?? TableFloatingPosition.WordCompatibleDefault;

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
            CellFitText: cell?.FitText ?? false,
            FloatingHorizontalAnchorIndex: Math.Max(0, DialogOptionPolicy.IndexOf(
                FloatingHorizontalAnchorValues,
                floating.HorizontalAnchor ?? TableHorizontalAnchor.Text)),
            FloatingHorizontalModeIndex: Math.Max(0, DialogOptionPolicy.IndexOf(
                FloatingHorizontalModeValues,
                floating.HorizontalAlignment)),
            FloatingHorizontalOffsetText: FormatPoints(floating.HorizontalOffsetPt ?? 0, culture),
            FloatingVerticalAnchorIndex: Math.Max(0, DialogOptionPolicy.IndexOf(
                FloatingVerticalAnchorValues,
                floating.VerticalAnchor ?? TableVerticalAnchor.Text)),
            FloatingVerticalModeIndex: Math.Max(0, DialogOptionPolicy.IndexOf(
                FloatingVerticalModeValues,
                floating.VerticalAlignment)),
            FloatingVerticalOffsetText: FormatPoints(floating.VerticalOffsetPt ?? 0, culture),
            FloatingDistanceTopText: FormatPoints(floating.TopFromTextPt ?? 0, culture),
            FloatingDistanceLeftText: FormatPoints(floating.LeftFromTextPt ?? 0, culture),
            FloatingDistanceBottomText: FormatPoints(floating.BottomFromTextPt ?? 0, culture),
            FloatingDistanceRightText: FormatPoints(floating.RightFromTextPt ?? 0, culture),
            FloatingTableAllowsOverlap: table.FloatingTableAllowsOverlap);
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

        TableFloatingPosition? floatingPosition = null;
        var hasFloatingPositionInput = input.FloatingHorizontalAnchorIndex >= 0
            && input.FloatingHorizontalModeIndex >= 0
            && input.FloatingVerticalAnchorIndex >= 0
            && input.FloatingVerticalModeIndex >= 0;
        if (input.WrappingIndex == 1 && hasFloatingPositionInput)
        {
            var horizontalUsesOffset = input.FloatingHorizontalModeIndex == 0;
            var verticalUsesOffset = input.FloatingVerticalModeIndex == 0;
            if (!TryParseOptionalSignedPoints(
                    horizontalUsesOffset,
                    input.FloatingHorizontalOffsetText,
                    culture,
                    out var horizontalOffset)
                || !TryParseOptionalSignedPoints(
                    verticalUsesOffset,
                    input.FloatingVerticalOffsetText,
                    culture,
                    out var verticalOffset)
                || !DialogNumericTextPolicy.TryParseNonNegativeDouble(input.FloatingDistanceTopText, culture, out var distanceTop)
                || !DialogNumericTextPolicy.TryParseNonNegativeDouble(input.FloatingDistanceLeftText, culture, out var distanceLeft)
                || !DialogNumericTextPolicy.TryParseNonNegativeDouble(input.FloatingDistanceBottomText, culture, out var distanceBottom)
                || !DialogNumericTextPolicy.TryParseNonNegativeDouble(input.FloatingDistanceRightText, culture, out var distanceRight))
            {
                errorMessage = ValidationMessage;
                return false;
            }

            floatingPosition = new TableFloatingPosition(
                HorizontalAnchor: DialogOptionPolicy.ValueAtOrDefault(
                    FloatingHorizontalAnchorValues,
                    input.FloatingHorizontalAnchorIndex),
                VerticalAnchor: DialogOptionPolicy.ValueAtOrDefault(
                    FloatingVerticalAnchorValues,
                    input.FloatingVerticalAnchorIndex),
                HorizontalOffsetPt: horizontalOffset,
                VerticalOffsetPt: verticalOffset,
                HorizontalAlignment: DialogOptionPolicy.ValueAtOrDefault(
                    FloatingHorizontalModeValues,
                    input.FloatingHorizontalModeIndex),
                VerticalAlignment: DialogOptionPolicy.ValueAtOrDefault(
                    FloatingVerticalModeValues,
                    input.FloatingVerticalModeIndex),
                LeftFromTextPt: distanceLeft,
                RightFromTextPt: distanceRight,
                TopFromTextPt: distanceTop,
                BottomFromTextPt: distanceBottom);
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
            CellFitText: input.CellFitText,
            FloatingTableAllowsOverlap: input.FloatingTableAllowsOverlap,
            FloatingPosition: floatingPosition);
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
        if (values.TextWrapping)
        {
            if (values.FloatingPosition is not null)
                table.FloatingPosition = values.FloatingPosition;
            table.FloatingTableAllowsOverlap = values.FloatingTableAllowsOverlap;
        }
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

    private static bool TryParseOptionalSignedPoints(
        bool enabled,
        string? text,
        CultureInfo culture,
        out double? value)
    {
        value = null;
        if (!enabled)
            return true;

        if (!double.TryParse(text?.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, culture, out var parsed)
            || !double.IsFinite(parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }
}
