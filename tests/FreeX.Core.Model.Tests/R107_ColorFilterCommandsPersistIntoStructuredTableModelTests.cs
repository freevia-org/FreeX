using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R107-commands-autofilter-table-color-sync-1: CellFillColorFilterCommand, CellNoFillColorFilterCommand
/// and CellFontColorFilterCommand used to mutate ONLY session-only state (sheet.FilterHiddenRows /
/// sheet.ColumnFilterOwnedRows) and, for a plain worksheet-level AutoFilter range,
/// sheet.AutoFilter.FilterColumns via WorksheetAutoFilterColumnSync (see R87) -- but
/// WorksheetAutoFilterColumnSync is a no-op whenever <c>_range</c> is a structured table's own Range,
/// since a table carries its own &lt;autoFilter&gt; inside the table part rather than a worksheet-level
/// one. Applying any of the three colour-filter criteria from a Table's own header dropdown hid/showed
/// rows correctly in the live session, but the table's StructuredTableFilterColumnModel list was never
/// updated, so the criterion was silently dropped from the table's &lt;autoFilter&gt; XML the moment the
/// workbook was saved and reopened -- mirrors R106's identical fix for TopBottomFilterCommand/
/// FilterConditionCommand (which itself mirrored the value-list case, finding H18).
/// </summary>
public sealed class R107_ColorFilterCommandsPersistIntoStructuredTableModelTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx, GridRange Range) SetUpTableWithColoredCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var red = new CellColor(255, 0, 0);
        var redCellStyle = CellStyle.Default.Clone();
        redCellStyle.FillColor = red;
        var redStyle = wb.RegisterStyle(redCellStyle);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Ready"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Blocked"));
        sheet.GetCell(2, 1)!.StyleId = redStyle;

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        var table = new StructuredTableModel
        {
            Id = 7,
            Name = "T1",
            DisplayName = "T1",
            Range = range,
            HasAutoFilter = true,
            Columns = { new StructuredTableColumnModel(1, "Status") }
        };
        sheet.StructuredTables.Add(table);

        return (wb, sheet, ctx, range);
    }

    [Fact]
    public void CellFillColorFilterCommand_StructuredTableRange_PersistsIntoTableModelAndReverts()
    {
        var (wb, sheet, ctx, range) = SetUpTableWithColoredCell();
        var red = new CellColor(255, 0, 0);

        var command = new CellFillColorFilterCommand(sheet.Id, range, filterColOffset: 0, red);
        command.Apply(ctx).Success.Should().BeTrue();

        // Bug: previously sheet.StructuredTables[0].FilterColumns stayed empty even though the
        // colour criterion was visibly applied (rows hidden) -- nothing ever wrote it into the table
        // model, and no worksheet-level sheet.AutoFilter should be spuriously created either (mirrors
        // H18/R106).
        sheet.AutoFilter.Should().BeNull();
        sheet.StructuredTables[0].FilterColumns.Should().ContainSingle();
        var column = sheet.StructuredTables[0].FilterColumns[0];
        column.ColumnId.Should().Be(0);
        column.ColorFilter.Should().NotBeNull();
        column.ColorFilter!.CellColor.Should().BeTrue();
        column.ColorFilter.Color.Should().Be(red);

        command.Revert(ctx);
        sheet.StructuredTables[0].FilterColumns.Should().BeEmpty();
    }

    [Fact]
    public void CellFontColorFilterCommand_StructuredTableRange_PersistsIntoTableModelAndReverts()
    {
        var (wb, sheet, ctx, range) = SetUpTableWithColoredCell();
        var blue = new CellColor(0, 0, 255);
        var blueCellStyle = CellStyle.Default.Clone();
        blueCellStyle.FontColor = blue;
        var blueStyle = wb.RegisterStyle(blueCellStyle);
        sheet.GetCell(2, 1)!.StyleId = blueStyle;

        var command = new CellFontColorFilterCommand(sheet.Id, range, filterColOffset: 0, blue);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.AutoFilter.Should().BeNull();
        sheet.StructuredTables[0].FilterColumns.Should().ContainSingle();
        var column = sheet.StructuredTables[0].FilterColumns[0];
        column.ColorFilter.Should().NotBeNull();
        column.ColorFilter!.CellColor.Should().BeFalse();
        column.ColorFilter.Color.Should().Be(blue);

        command.Revert(ctx);
        sheet.StructuredTables[0].FilterColumns.Should().BeEmpty();
    }

    [Fact]
    public void CellNoFillColorFilterCommand_StructuredTableRange_PersistsIntoTableModelAndReverts()
    {
        var (wb, sheet, ctx, range) = SetUpTableWithColoredCell();

        var command = new CellNoFillColorFilterCommand(sheet.Id, range, filterColOffset: 0);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.AutoFilter.Should().BeNull();
        sheet.StructuredTables[0].FilterColumns.Should().ContainSingle();
        var column = sheet.StructuredTables[0].FilterColumns[0];
        column.ColorFilter.Should().NotBeNull();
        column.ColorFilter!.CellColor.Should().BeTrue();
        column.ColorFilter.Color.Should().BeNull("'No Fill' has no colour to record");

        command.Revert(ctx);
        sheet.StructuredTables[0].FilterColumns.Should().BeEmpty();
    }

    /// <summary>
    /// No-regression sibling: the SAME three commands applied against a plain worksheet-level
    /// AutoFilter range (no structured table at all) must keep behaving exactly as R87 already
    /// covers -- sheet.StructuredTables stays empty and nothing throws when there is no table to
    /// match against.
    /// </summary>
    [Fact]
    public void ColorFilterCommands_WorksheetAutoFilterRange_NoStructuredTableTouched()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var red = new CellColor(255, 0, 0);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Ready"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

        var fill = new CellFillColorFilterCommand(sheet.Id, range, filterColOffset: 0, red);
        fill.Apply(ctx).Success.Should().BeTrue();
        sheet.StructuredTables.Should().BeEmpty();
        sheet.AutoFilter!.FilterColumns.Should().ContainSingle();
        fill.Revert(ctx);

        var noFill = new CellNoFillColorFilterCommand(sheet.Id, range, filterColOffset: 0);
        noFill.Apply(ctx).Success.Should().BeTrue();
        sheet.StructuredTables.Should().BeEmpty();
        noFill.Revert(ctx);

        var font = new CellFontColorFilterCommand(sheet.Id, range, filterColOffset: 0, red);
        font.Apply(ctx).Success.Should().BeTrue();
        sheet.StructuredTables.Should().BeEmpty();
        font.Revert(ctx);
    }
}
