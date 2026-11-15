using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public class CustomViewSheetRenameRoundTripTests
{
    [Fact]
    public void RenameAndSaveReload_PreservesViewAndRewritesOpaquePrintExpression()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var state = new WorksheetCustomViewState(
            "Data",
            WorksheetViewMode.Normal,
            0,
            0,
            null,
            null,
            ZoomPercent: 125)
        {
            PrintSettings = new WorksheetCustomViewPrintSettings(),
            OpaquePrintAreaExpression = "Data!$A$1:$B$2,OFFSET(Data!$C$1,0,0,2,1)"
        };
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state], "{11111111-1111-1111-1111-111111111111}"));
        new RenameSheetCommand(sheet.Id, "New Data").Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        using var file = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, file);
        file.Position = 0;
        var loaded = adapter.Load(file);
        var view = loaded.CustomViews.Should().ContainSingle().Subject;
        view.Sheets.Should().ContainSingle().Which.SheetName.Should().Be("New Data");
        view.Sheets[0].OpaquePrintAreaExpression.Should().Be("'New Data'!$A$1:$B$2,OFFSET('New Data'!$C$1,0,0,2,1)");
        var loadedSheet = loaded.GetSheet("New Data")!;
        loadedSheet.ZoomPercent = 90;
        new ApplyCustomViewCommand("View").Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        loadedSheet.ZoomPercent.Should().Be(125);
    }
    [Fact]
    public void MixedLocalExternalAndBracketLiteral_SurvivesRenameAndXlsxRoundTrip()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        const string formula = "=SUM(Data!A1,'[1]First:Data'!B2,\"[literal]\",TABLE1[Column])";
        var state = new WorksheetCustomViewState(
            "Data",
            WorksheetViewMode.Normal,
            0,
            0,
            null,
            null)
        {
            PrintSettings = new WorksheetCustomViewPrintSettings(),
            OpaquePrintAreaExpression = formula
        };
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state], "{11111111-1111-1111-1111-111111111111}"));
        new RenameSheetCommand(sheet.Id, "New Data").Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var loaded = adapter.Load(stream);
        loaded.CustomViews.Should().ContainSingle().Which.Sheets.Should().ContainSingle().Which
            .OpaquePrintAreaExpression.Should().Be("=SUM('New Data'!A1,'[1]First:Data'!B2,\"[literal]\",TABLE1[Column])");
    }

}
