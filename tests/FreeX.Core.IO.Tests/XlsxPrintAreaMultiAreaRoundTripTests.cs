using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip tests verifying that multi-area print areas survive load→save→reload.
/// Excel supports a comma-separated <c>_xlnm.Print_Area</c> defined name (multiple ranges);
/// FreeX must preserve all areas, not just the first.
/// </summary>
public sealed class XlsxPrintAreaMultiAreaRoundTripTests
{
    [Fact]
    public void SaveReopen_SinglePrintArea_RoundTripsCorrectly()
    {
        var workbook = new Workbook("Single Print Area");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new TextValue("C5"));

        var area = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 3));
        sheet.SetPrintAreas([area]);

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var reopened = adapter.Load(ms);

        var reopenedSheet = reopened.GetSheet("Sheet1")!;
        reopenedSheet.PrintAreas.Should().HaveCount(1);
        reopenedSheet.PrintArea.Should().NotBeNull();
        reopenedSheet.PrintArea!.Value.Start.Row.Should().Be(1u);
        reopenedSheet.PrintArea.Value.Start.Col.Should().Be(1u);
        reopenedSheet.PrintArea.Value.End.Row.Should().Be(5u);
        reopenedSheet.PrintArea.Value.End.Col.Should().Be(3u);
    }

    [Fact]
    public void SaveReopen_TwoPrintAreas_RoundTripsBothAreas()
    {
        var workbook = new Workbook("Multi-Area Print");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new TextValue("C5"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("E1"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 7), new TextValue("G5"));

        var area1 = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 3));
        var area2 = new GridRange(
            new CellAddress(sheet.Id, 1, 5),
            new CellAddress(sheet.Id, 5, 7));
        sheet.SetPrintAreas([area1, area2]);

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var reopened = adapter.Load(ms);

        var reopenedSheet = reopened.GetSheet("Sheet1")!;
        reopenedSheet.PrintAreas.Should().HaveCount(2,
            "both print areas should survive the XLSX round-trip");

        var r0 = reopenedSheet.PrintAreas[0];
        r0.Start.Row.Should().Be(1u);
        r0.Start.Col.Should().Be(1u);
        r0.End.Row.Should().Be(5u);
        r0.End.Col.Should().Be(3u);

        var r1 = reopenedSheet.PrintAreas[1];
        r1.Start.Row.Should().Be(1u);
        r1.Start.Col.Should().Be(5u);
        r1.End.Row.Should().Be(5u);
        r1.End.Col.Should().Be(7u);
    }

    [Fact]
    public void NoPrintArea_PrintAreasIsEmpty()
    {
        var workbook = new Workbook("No Print Area");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("data"));

        sheet.PrintAreas.Should().BeEmpty();
        sheet.PrintArea.Should().BeNull();
    }

    [Fact]
    public void SetPrintAreasEmpty_ClearsPrintArea()
    {
        var workbook = new Workbook("Clear Print Area");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));

        sheet.SetPrintAreas([]);

        sheet.PrintAreas.Should().BeEmpty();
        sheet.PrintArea.Should().BeNull();
    }

    [Fact]
    public void PrintAreaConvenienceSetter_UpdatesPrintAreas()
    {
        var workbook = new Workbook("Convenience Setter");
        var sheet = workbook.AddSheet("Sheet1");

        var area = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 8, 8));
        sheet.PrintArea = area;

        sheet.PrintAreas.Should().HaveCount(1);
        sheet.PrintArea.Should().Be(area);
    }

    [Fact]
    public void BlackAndWhite_FlagRoundTripsThroughXlsx()
    {
        var workbook = new Workbook("B&W Flag");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("text"));
        sheet.PrintBlackAndWhite = true;

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var reopened = adapter.Load(ms);

        reopened.GetSheet("Sheet1")!.PrintBlackAndWhite.Should().BeTrue(
            "the BlackAndWhite page setup flag should survive XLSX save→reload");
    }

    [Fact]
    public void BlackAndWhite_FlagFalseByDefaultAndRoundTrips()
    {
        var workbook = new Workbook("B&W Default");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("text"));
        // Not setting PrintBlackAndWhite — default is false.

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var reopened = adapter.Load(ms);

        reopened.GetSheet("Sheet1")!.PrintBlackAndWhite.Should().BeFalse();
    }

    [Fact]
    public void MultiAreaPrintArea_ClonePreservesAllAreas()
    {
        var workbook = new Workbook("Clone Multi-Area");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("data"));

        var area1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));
        var area2 = new GridRange(new CellAddress(sheet.Id, 5, 5), new CellAddress(sheet.Id, 8, 8));
        sheet.SetPrintAreas([area1, area2]);

        var newId = new SheetId(Guid.NewGuid());
        var clone = sheet.Clone(newId, "Clone");

        clone.PrintAreas.Should().HaveCount(2);
        clone.PrintAreas[0].Start.Sheet.Should().Be(newId, "cloned areas should be remapped to the new sheet id");
        clone.PrintAreas[1].Start.Sheet.Should().Be(newId);
        clone.PrintAreas[0].Start.Row.Should().Be(1u);
        clone.PrintAreas[1].Start.Row.Should().Be(5u);
    }
}
