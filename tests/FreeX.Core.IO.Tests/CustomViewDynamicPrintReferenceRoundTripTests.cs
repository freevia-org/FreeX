using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class CustomViewDynamicPrintReferenceRoundTripTests
{
    [Fact]
    public void XlsxDynamicAreaRemainsFormulaAndReevaluatesAfterSaveReload()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        const string expression = "OFFSET(Data!$A$1,Data!$B$1,0,2,1)";
        var state = new WorksheetCustomViewState(sheet.Name, WorksheetViewMode.Normal, 0, 0, null, null)
        { OpaquePrintAreaExpression = expression };
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state]));
        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;
        var loaded = adapter.Load(source);
        var live = loaded.GetSheet("Data")!;
        var command = new ApplyCustomViewCommand("View");
        command.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        live.PrintAreas.Should().Equal(new GridRange(new CellAddress(live.Id, 3, 1), new CellAddress(live.Id, 4, 1)));
        loaded.CustomViews[0].Sheets[0].OpaquePrintAreaExpression.Should().Be(expression);
        live.SetCell(new CellAddress(live.Id, 1, 2), new NumberValue(4));
        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var sheetAfterReload = reloaded.GetSheet("Data")!;
        var previous = sheetAfterReload.PrintAreas.ToArray();
        var ctx = new TestCommandContext(reloaded);
        var show = new ApplyCustomViewCommand("View");
        show.Apply(ctx).Success.Should().BeTrue();
        sheetAfterReload.PrintAreas.Should().Equal(new GridRange(new CellAddress(sheetAfterReload.Id, 5, 1), new CellAddress(sheetAfterReload.Id, 6, 1)));
        reloaded.CustomViews[0].Sheets[0].OpaquePrintAreaExpression.Should().Be(expression);
        show.Revert(ctx);
        sheetAfterReload.PrintAreas.Should().Equal(previous);
    }
}
