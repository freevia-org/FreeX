using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class CustomCellStyleTests
{
    [Fact]
    public void Define_CapturesActiveCellStyle_AndApplyCopiesOnlyTheChosenParts()
    {
        var (session, sheet) = CreateSession();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        session.SelectCell(a1);
        Assert.True(session.ApplySelectedRangeCompactFormat(new StyleDiff(Bold: true, FontSize: 18, NumberFormat: "0.00"), null).Success);

        var defined = session.DefineCustomCellStyleFromActiveCell("Big Number", CustomCellStyleParts.Font);
        Assert.True(defined.Success);
        Assert.Single(session.Workbook.CustomCellStyles);

        var b1 = new CellAddress(sheet.Id, 1, 2);
        session.SelectCell(b1);
        Assert.True(session.ApplyCustomCellStyle("big number").Success);

        var style = session.Workbook.GetStyle(WorkbookSession.GetCellStyleId(sheet, b1));
        Assert.True(style.Bold);
        Assert.Equal(18, style.FontSize);
        Assert.NotEqual("0.00", style.NumberFormat);
    }

    [Fact]
    public void Define_RejectsBuiltInAndBlankNames_AndUndoRemovesTheStyle()
    {
        var (session, _) = CreateSession();
        Assert.False(session.DefineCustomCellStyleFromActiveCell("Good", CustomCellStyleParts.All).Success);
        Assert.False(session.DefineCustomCellStyleFromActiveCell("  ", CustomCellStyleParts.All).Success);

        Assert.True(session.DefineCustomCellStyleFromActiveCell("Mine", CustomCellStyleParts.All).Success);
        Assert.Single(session.Workbook.CustomCellStyles);
        session.UndoLastEdit();
        Assert.Empty(session.Workbook.CustomCellStyles);
    }

    [Fact]
    public void NativeJson_RoundTripsCustomStyles()
    {
        var workbook = new Workbook("t");
        workbook.AddSheet("S");
        workbook.CustomCellStyles.Add(new CustomCellStyle(
            "Mine",
            new CellStyle { Bold = true, FontSize = 14 },
            CustomCellStyleParts.Font | CustomCellStyleParts.Fill));

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var loaded = adapter.Load(stream);

        var style = Assert.Single(loaded.CustomCellStyles);
        Assert.Equal("Mine", style.Name);
        Assert.Equal(CustomCellStyleParts.Font | CustomCellStyleParts.Fill, style.Parts);
        Assert.True(style.Style.Bold);
        Assert.Equal(14, style.Style.FontSize);
    }

    private static (WorkbookSession Session, Sheet Sheet) CreateSession()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var sheet = workbook.Sheets.Single();
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
        session.SelectCell(new CellAddress(sheet.Id, 1, 1));
        return (session, sheet);
    }
}
