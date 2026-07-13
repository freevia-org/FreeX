using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R39-commands-sort-custom-2-3: sort-by-color swatch choices must be scoped to the single
/// column a given sort level targets (and exclude the header row when present), not scanned
/// across the whole multi-column selection.
/// </summary>
public sealed class R39_SortDialogPlannerCustom2Tests
{
    [Fact]
    public void BuildColorChoices_ColumnScoped_OnlyOffersColorsPresentInTargetColumn()
    {
        // A1:B4 selected: column A has red/green fills, column B has only blue. A sort level
        // targeting column B (offset 1) must only ever offer blue — never red/green, which exist
        // solely in column A.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var red = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 0, 0) });
        var green = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(0, 255, 0) });
        var blue = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(0, 0, 255) });

        var aRed = Cell.FromValue(new TextValue("a1"));
        aRed.StyleId = red;
        var aGreen = Cell.FromValue(new TextValue("a2"));
        aGreen.StyleId = green;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), aRed);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), aGreen);

        var bBlue1 = Cell.FromValue(new TextValue("b1"));
        bBlue1.StyleId = blue;
        var bBlue2 = Cell.FromValue(new TextValue("b2"));
        bBlue2.StyleId = blue;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), bBlue1);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), bBlue2);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));

        SortDialogPlanner.BuildColorChoices(workbook, sheet, range, SortOn.CellColor, columnOffset: 1, hasHeaders: false)
            .Should()
            .Equal(new SortColorChoice(""), new SortColorChoice("#0000FF"));

        SortDialogPlanner.BuildColorChoices(workbook, sheet, range, SortOn.CellColor, columnOffset: 0, hasHeaders: false)
            .Should()
            .Equal(new SortColorChoice(""), new SortColorChoice("#00FF00"), new SortColorChoice("#FF0000"));
    }

    [Fact]
    public void BuildColorChoices_ColumnScoped_ExcludesHeaderRowWhenRequested()
    {
        // Row 1 is a colored header; the actual data (row 2) is a different color. With
        // hasHeaders=true, only the data-row color must appear.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var headerStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(128, 128, 128) });
        var dataStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 255, 0) });

        var header = Cell.FromValue(new TextValue("Header"));
        header.StyleId = headerStyle;
        var data = Cell.FromValue(new TextValue("Data"));
        data.StyleId = dataStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), header);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), data);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));

        SortDialogPlanner.BuildColorChoices(workbook, sheet, range, SortOn.CellColor, columnOffset: 0, hasHeaders: true)
            .Should()
            .Equal(new SortColorChoice(""), new SortColorChoice("#FFFF00"));
    }

    [Fact]
    public void BuildColorChoices_WholeRangeOverload_StillScansAllColumns_NoRegression()
    {
        // Sibling no-regression case: the pre-existing whole-range overload (used before a
        // level's column is known) is untouched and still scans every column.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var red = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 0, 0) });
        var blue = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(0, 0, 255) });

        var aRed = Cell.FromValue(new TextValue("a"));
        aRed.StyleId = red;
        var bBlue = Cell.FromValue(new TextValue("b"));
        bBlue.StyleId = blue;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), aRed);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), bBlue);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2));

        SortDialogPlanner.BuildColorChoices(workbook, sheet, range, SortOn.CellColor)
            .Should()
            .Equal(new SortColorChoice(""), new SortColorChoice("#0000FF"), new SortColorChoice("#FF0000"));
    }
}
