using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class StructuredTableCommandTests
{
    [Fact]
    public void CreateStyledStructuredTableCommand_AppliesTableMetadataAndBandedStylesAsOneUndoableOperation()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        var ctx = new TestCommandContext(wb);
        var preexistingBodyStyleId = wb.RegisterStyle(new CellStyle
        {
            FontColor = new CellColor(192, 0, 0),
            Bold = true
        });
        sheet.GetCell(new CellAddress(sheet.Id, 3, 1))!.StyleId = preexistingBodyStyleId;
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));
        var command = new CreateStyledStructuredTableCommand(
            sheet.Id,
            range,
            "TableStyleMedium2",
            firstRowHasHeaders: true,
            new StructuredTableStyleBanding(
                HeaderFill: new CellColor(31, 78, 121),
                OddRowFill: new CellColor(222, 235, 247),
                EvenRowFill: new CellColor(255, 255, 255),
                HeaderFontColor: CellColor.White));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.StructuredTables.Should().ContainSingle()
            .Which.StyleName.Should().Be("TableStyleMedium2");
        var headerStyle = wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.StyleId);
        headerStyle.FillColor.Should().Be(new CellColor(31, 78, 121));
        headerStyle.FontColor.Should().Be(CellColor.White);
        headerStyle.Bold.Should().BeTrue();
        wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 2, 1))!.StyleId)
            .FillColor.Should().Be(new CellColor(255, 255, 255));
        var bodyStyle = wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 3, 1))!.StyleId);
        bodyStyle.FillColor.Should().Be(new CellColor(222, 235, 247));
        bodyStyle.FontColor.Should().Be(CellColor.Black);
        bodyStyle.Bold.Should().BeFalse();

        command.Revert(ctx);

        sheet.StructuredTables.Should().BeEmpty();
        wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.StyleId)
            .Should().Be(wb.GetStyle(StyleId.Default));
        wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 3, 1))!.StyleId)
            .Should().Be(wb.GetStyle(preexistingBodyStyleId));
    }

    [Theory]
    [InlineData(2u)]
    [InlineData(3u)]
    public void CreateStyledStructuredTableCommand_UsesTableRelativeDataRowBanding(uint headerRow)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, headerRow, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, headerRow, 2), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, headerRow + 1, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, headerRow + 1, 2), new TextValue("Open"));
        sheet.SetCell(new CellAddress(sheet.Id, headerRow + 2, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, headerRow + 2, 2), new TextValue("Closed"));
        var ctx = new TestCommandContext(wb);
        var firstDataRowFill = new CellColor(255, 255, 255);
        var secondDataRowFill = new CellColor(222, 235, 247);
        var range = new GridRange(
            new CellAddress(sheet.Id, headerRow, 1),
            new CellAddress(sheet.Id, headerRow + 2, 2));
        var command = new CreateStyledStructuredTableCommand(
            sheet.Id,
            range,
            "TableStyleMedium2",
            firstRowHasHeaders: true,
            new StructuredTableStyleBanding(
                HeaderFill: new CellColor(31, 78, 121),
                OddRowFill: secondDataRowFill,
                EvenRowFill: firstDataRowFill,
                HeaderFontColor: CellColor.White));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, headerRow + 1, 1))!.StyleId)
            .FillColor.Should().Be(firstDataRowFill);
        wb.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, headerRow + 2, 1))!.StyleId)
            .FillColor.Should().Be(secondDataRowFill);
    }
}
