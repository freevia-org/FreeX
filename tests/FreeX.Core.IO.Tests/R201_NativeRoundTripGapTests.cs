using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r201: the seven members the .fxl serializer silently dropped, found by diffing every DTO against
/// its model type rather than by looking for interesting ones. The contract test beside this file
/// stops the class recurring; these pin the behaviour the users of each field actually depend on.
/// <para>
/// Autosave and crash recovery go through this adapter exclusively, so every one of these was lost
/// on a recovered document, not only on an explicit Save As .fxl.
/// </para>
/// </summary>
public sealed class R201_NativeRoundTripGapTests
{
    [Fact]
    public void ASheetsTabThemeColorSurvives()
    {
        // R123 added Sheet.TabThemeColor precisely so a theme-linked tab colour is not baked to RGB
        // on save. The DTO carried only the resolved TabColor, undoing that on every round trip.
        var workbook = NewWorkbook(out var sheet);
        sheet.TabThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, 0.4);

        var reopened = RoundTrip(workbook).Sheets[0];

        reopened.TabThemeColor.Should().NotBeNull();
        reopened.TabThemeColor!.Value.Slot.Should().Be(WorkbookThemeColorSlot.Accent2);
        reopened.TabThemeColor.Value.Tint.Should().BeApproximately(0.4, 1e-9);
    }

    [Fact]
    public void ASheetsDefaultRowAndColumnSizesSurvive()
    {
        var workbook = NewWorkbook(out var sheet);
        sheet.DefaultColumnWidth = 14.5;
        sheet.DefaultRowHeight = 26.0;

        var reopened = RoundTrip(workbook).Sheets[0];

        reopened.DefaultColumnWidth.Should().Be(14.5);
        reopened.DefaultRowHeight.Should().Be(26.0);
    }

    [Fact]
    public void ASheetsCodeNameSurvives()
    {
        var workbook = NewWorkbook(out var sheet);
        sheet.CodeName = "Sheet1_Code";

        RoundTrip(workbook).Sheets[0].CodeName.Should().Be("Sheet1_Code");
    }

    [Fact]
    public void ACellsQuotePrefixSurvives()
    {
        // The leading apostrophe that makes a numeric-looking cell text and drives Excel's
        // "Number Stored as Text" indicator.
        var workbook = NewWorkbook(out var sheet);
        var address = new CellAddress(sheet.Id, 1, 1);
        var cell = Cell.FromValue(new TextValue("00123"));
        cell.QuotePrefix = true;
        sheet.SetCell(address, cell);

        var reopened = RoundTrip(workbook).Sheets[0];

        reopened.GetCell(new CellAddress(reopened.Id, 1, 1))!.QuotePrefix.Should().BeTrue();
    }

    [Fact]
    public void ALegacyArrayFormulasDeclaredExtentSurvives()
    {
        var workbook = NewWorkbook(out var sheet);
        var address = new CellAddress(sheet.Id, 1, 1);
        var cell = Cell.FromFormula("SUM(A1:A3)");
        cell.LegacyArrayRows = 3;
        cell.LegacyArrayCols = 2;
        sheet.SetCell(address, cell);

        var reopened = RoundTrip(workbook).Sheets[0];
        var reloaded = reopened.GetCell(new CellAddress(reopened.Id, 1, 1))!;

        reloaded.LegacyArrayRows.Should().Be(3u);
        reloaded.LegacyArrayCols.Should().Be(2u);
    }

    [Fact]
    public void AnOrdinaryCellIsUnchanged()
    {
        // The control: the new fields default to off and must not appear on a plain cell.
        var workbook = NewWorkbook(out var sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(42)));

        var reopened = RoundTrip(workbook).Sheets[0];
        var reloaded = reopened.GetCell(new CellAddress(reopened.Id, 1, 1))!;

        reloaded.QuotePrefix.Should().BeFalse();
        reloaded.LegacyArrayRows.Should().Be(0u);
        reloaded.LegacyArrayCols.Should().Be(0u);
        reloaded.Value.Should().BeOfType<NumberValue>();
    }

    private static Workbook NewWorkbook(out Sheet sheet)
    {
        var workbook = new Workbook("test");
        sheet = workbook.AddSheet("Sheet1");
        return workbook;
    }

    private static Workbook RoundTrip(Workbook workbook)
    {
        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }
}
