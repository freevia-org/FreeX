using System.Globalization;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class TablePropertiesDialogPlannerTests
{
    [Fact]
    public void BuildInitialState_SeedsAllTabsFromCaretTableContext()
    {
        var table = Table.Create(1, 1);
        table.PreferredWidthPt = 300;
        table.Alignment = TableAlignment.Right;
        table.TextWrapping = true;
        table.IndentFromLeftPt = 12;
        table.CellSpacingPt = 2;
        table.Formatting = table.Formatting with { RepeatHeaderRow = true };
        var row = table.Rows[0];
        row.HeightPt = 36;
        row.HeightRule = TableRowHeightRule.Exact;
        row.AllowBreakAcrossPages = false;
        var cell = row.Cells[0];
        cell.WidthPt = 150;
        cell.VerticalAlignment = TableCellVerticalAlignment.Bottom;
        cell.Margins = new TableCellMargins(1, 7, 1, 7);

        var state = TablePropertiesDialogPlanner.BuildInitialState(
            new ModelTableContext(table, row, cell),
            CultureInfo.InvariantCulture);

        state.PreferredWidthText.Should().Be("300");
        state.PreferredWidthOn.Should().BeTrue();
        state.AlignmentIndex.Should().Be(2);
        state.WrappingIndex.Should().Be(1);
        state.IndentText.Should().Be("12");
        state.CellSpacingOn.Should().BeTrue();
        state.RowHeightText.Should().Be("36");
        state.RowRuleIndex.Should().Be(1);
        state.AllowRowBreak.Should().BeFalse();
        state.RepeatHeaderRow.Should().BeTrue();
        state.ColumnWidthText.Should().Be("150");
        state.CellVerticalAlignmentIndex.Should().Be(2);
        state.CellMarginsSameAsTable.Should().BeFalse();
        state.CellMarginLeftText.Should().Be("7");
    }

    [Fact]
    public void TryBuildResult_ConstructsTableRowColumnAndCellValues()
    {
        var input = ValidInput() with
        {
            AlignmentIndex = 1,
            WrappingIndex = 1,
            RowRuleIndex = 1,
            CellVerticalAlignmentIndex = 1,
            CellMarginsSameAsTable = false,
        };

        TablePropertiesDialogPlanner.TryBuildResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out var error)
            .Should().BeTrue();

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.PreferredWidthPt.Should().Be(300);
        result.Alignment.Should().Be(TableAlignment.Center);
        result.TextWrapping.Should().BeTrue();
        result.IndentFromLeftPt.Should().Be(12);
        result.DefaultCellMargins!.LeftPt.Should().Be(6);
        result.CellSpacingPt.Should().Be(2);
        result.RowHeightPt.Should().Be(36);
        result.RowHeightRule.Should().Be(TableRowHeightRule.Exact);
        result.AllowRowBreak.Should().BeFalse();
        result.RepeatHeaderRow.Should().BeTrue();
        result.ColumnWidthPt.Should().Be(120);
        result.CellPreferredWidthPt.Should().Be(140);
        result.CellVerticalAlignment.Should().Be(TableCellVerticalAlignment.Center);
        result.CellMargins!.LeftPt.Should().Be(8);
    }

    [Fact]
    public void TryBuildResult_UncheckedOptionalFieldsIgnoreInvalidTextAndUseAutoRowRule()
    {
        var input = ValidInput() with
        {
            PreferredWidthOn = false,
            PreferredWidthText = "wide",
            CellSpacingOn = false,
            CellSpacingText = "spaced",
            RowHeightOn = false,
            RowHeightText = "tall",
            ColumnWidthOn = false,
            ColumnWidthText = "column",
            CellWidthOn = false,
            CellWidthText = "cell",
            CellMarginsSameAsTable = true,
        };

        TablePropertiesDialogPlanner.TryBuildResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out _)
            .Should().BeTrue();

        result!.PreferredWidthPt.Should().BeNull();
        result.CellSpacingPt.Should().BeNull();
        result.RowHeightPt.Should().BeNull();
        result.RowHeightRule.Should().Be(TableRowHeightRule.Auto);
        result.ColumnWidthPt.Should().BeNull();
        result.CellPreferredWidthPt.Should().BeNull();
        result.CellMargins.Should().BeNull();
    }

    [Fact]
    public void TryBuildResult_RejectsNegativeRequiredMeasurementsWithPreservedMessage()
    {
        var input = ValidInput() with { DefaultCellMarginLeftText = "-1" };

        TablePropertiesDialogPlanner.TryBuildResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out var error)
            .Should().BeFalse();

        result.Should().BeNull();
        error.Should().Be(TablePropertiesDialogPlanner.ValidationMessage);
    }

    private static TablePropertiesDialogInput ValidInput() => new(
        PreferredWidthOn: true,
        PreferredWidthText: "300",
        AlignmentIndex: 0,
        WrappingIndex: 0,
        IndentText: "12",
        DefaultCellMarginTopText: "0",
        DefaultCellMarginLeftText: "6",
        DefaultCellMarginBottomText: "0",
        DefaultCellMarginRightText: "6",
        CellSpacingOn: true,
        CellSpacingText: "2",
        RowHeightOn: true,
        RowHeightText: "36",
        RowRuleIndex: 0,
        AllowRowBreak: false,
        RepeatHeaderRow: true,
        ColumnWidthOn: true,
        ColumnWidthText: "120",
        CellWidthOn: true,
        CellWidthText: "140",
        CellVerticalAlignmentIndex: 0,
        CellMarginsSameAsTable: true,
        CellMarginTopText: "2",
        CellMarginLeftText: "8",
        CellMarginBottomText: "2",
        CellMarginRightText: "8");
}
