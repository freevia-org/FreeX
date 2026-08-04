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
        table.FloatingTableAllowsOverlap = false;
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
        cell.WrapText = false;
        cell.FitText = true;

        var state = TablePropertiesDialogPlanner.BuildInitialState(
            new ModelTableContext(table, row, cell),
            CultureInfo.InvariantCulture);

        state.PreferredWidthText.Should().Be("300");
        state.PreferredWidthOn.Should().BeTrue();
        state.AlignmentIndex.Should().Be(2);
        state.WrappingIndex.Should().Be(1);
        state.FloatingTableAllowsOverlap.Should().BeFalse();
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
        state.CellWrapText.Should().BeFalse();
        state.CellFitText.Should().BeTrue();
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
            CellWrapText = false,
            CellFitText = true,
            FloatingTableAllowsOverlap = false,
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
        result.FloatingTableAllowsOverlap.Should().BeFalse();
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
        result.CellWrapText.Should().BeFalse();
        result.CellFitText.Should().BeTrue();
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

    [Fact]
    public void ApplyValues_AppliesTableRowColumnAndCellFields()
    {
        var table = Table.Create(2, 2);
        var row = table.Rows[0];
        var cell = row.Cells[1];
        var values = new TablePropertiesValues(
            PreferredWidthPt: 300,
            Alignment: TableAlignment.Right,
            TextWrapping: true,
            IndentFromLeftPt: 12,
            DefaultCellMargins: new TableCellMargins(0, 6, 0, 6),
            CellSpacingPt: 2,
            RowHeightPt: 36,
            RowHeightRule: TableRowHeightRule.Exact,
            AllowRowBreak: false,
            RepeatHeaderRow: true,
            ColumnWidthPt: 120,
            CellPreferredWidthPt: 140,
            CellVerticalAlignment: TableCellVerticalAlignment.Center,
            CellMargins: new TableCellMargins(2, 8, 2, 8),
            CellWrapText: false,
            CellFitText: true,
            FloatingTableAllowsOverlap: false);

        TablePropertiesDialogPlanner.ApplyValues(new ModelTableContext(table, row, cell), values);

        table.PreferredWidthPt.Should().Be(300);
        table.Alignment.Should().Be(TableAlignment.Right);
        table.TextWrapping.Should().BeTrue();
        table.FloatingTableAllowsOverlap.Should().BeFalse();
        table.IndentFromLeftPt.Should().Be(12);
        table.DefaultCellMargins.Should().Be(new TableCellMargins(0, 6, 0, 6));
        table.CellSpacingPt.Should().Be(2);
        table.Formatting.RepeatHeaderRow.Should().BeTrue();
        row.HeightPt.Should().Be(36);
        row.HeightRule.Should().Be(TableRowHeightRule.Exact);
        row.AllowBreakAcrossPages.Should().BeFalse();
        table.Rows[1].Cells[1].WidthPt.Should().Be(120);
        cell.WidthPt.Should().Be(140);
        cell.VerticalAlignment.Should().Be(TableCellVerticalAlignment.Center);
        cell.Margins.Should().Be(new TableCellMargins(2, 8, 2, 8));
        cell.WrapText.Should().BeFalse();
        cell.FitText.Should().BeTrue();
    }

    [Fact]
    public void ApplyTablePropertiesCommand_UndoRedoRestoresCompleteMutationFootprint()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = Table.Create(2, 2);
        table.ColumnWidthsPt.AddRange([90, 110]);
        var floatingPosition = new TableFloatingPosition(
            HorizontalAnchor: TableHorizontalAnchor.Page,
            VerticalAnchor: TableVerticalAnchor.Margin,
            HorizontalOffsetPt: -12);
        table.FloatingPosition = floatingPosition;
        table.FloatingTableAllowsOverlap = false;
        document.Blocks.Add(table);
        var row = table.Rows[0];
        var cell = row.Cells[1];
        var values = new TablePropertiesValues(
            PreferredWidthPt: 300,
            Alignment: TableAlignment.Right,
            TextWrapping: false,
            IndentFromLeftPt: 12,
            DefaultCellMargins: new TableCellMargins(0, 6, 0, 6),
            CellSpacingPt: 2,
            RowHeightPt: 36,
            RowHeightRule: TableRowHeightRule.Exact,
            AllowRowBreak: false,
            RepeatHeaderRow: true,
            ColumnWidthPt: 120,
            CellPreferredWidthPt: 140,
            CellVerticalAlignment: TableCellVerticalAlignment.Center,
            CellMargins: new TableCellMargins(2, 8, 2, 8),
            CellWrapText: false,
            CellFitText: true);
        var bus = new DocumentCommandBus(new CommandContext(document));

        bus.Execute(new ApplyTablePropertiesCommand(0, 0, 1, values));
        table.FloatingPosition.Should().BeNull();
        table.FloatingTableAllowsOverlap.Should().BeNull();
        cell.WidthPt.Should().Be(140);
        cell.WrapText.Should().BeFalse();
        cell.FitText.Should().BeTrue();
        table.Rows[1].Cells[1].WidthPt.Should().Be(120);

        bus.Undo().Should().BeTrue();
        table.PreferredWidthPt.Should().BeNull();
        table.Alignment.Should().Be(TableAlignment.Left);
        table.FloatingPosition.Should().Be(floatingPosition);
        table.FloatingTableAllowsOverlap.Should().BeFalse();
        table.ColumnWidthsPt.Should().Equal(90, 110);
        table.Rows.SelectMany(candidate => candidate.Cells).Should().OnlyContain(candidate => candidate.WidthPt == null);
        row.HeightPt.Should().BeNull();
        row.AllowBreakAcrossPages.Should().BeTrue();
        cell.VerticalAlignment.Should().Be(TableCellVerticalAlignment.Top);
        cell.Margins.Should().BeNull();
        cell.WrapText.Should().BeTrue();
        cell.FitText.Should().BeFalse();

        bus.Redo().Should().BeTrue();
        table.PreferredWidthPt.Should().Be(300);
        table.FloatingPosition.Should().BeNull();
        table.FloatingTableAllowsOverlap.Should().BeNull();
        cell.WidthPt.Should().Be(140);
        cell.WrapText.Should().BeFalse();
        cell.FitText.Should().BeTrue();
        table.Rows[1].Cells[1].WidthPt.Should().Be(120);
    }

    private sealed class CommandContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
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
        CellMarginRightText: "8",
        CellWrapText: true,
        CellFitText: false);
}
