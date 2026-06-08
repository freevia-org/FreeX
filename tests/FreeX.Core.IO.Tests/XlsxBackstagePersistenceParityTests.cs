using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxBackstagePersistenceParityTests
{
    [Fact]
    public void SaveReopen_PersistsUiCreatedSheetFormattingAndFilterState()
    {
        var workbook = new Workbook("Backstage Persistence Sample");
        var ledger = workbook.AddSheet("Sheet1");
        PopulateLedger(ledger);

        ledger.Name = "Ledger";
        var notes = workbook.AddSheet("Notes");
        notes.SetCell(new CellAddress(notes.Id, 1, 1), new TextValue("Added from the sheet-tab UI"));
        workbook.MoveSheet(fromIndex: 1, toIndex: 0);

        var amountStyleId = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = new CellColor(221, 235, 247),
            NumberFormat = "0.00",
            HorizontalAlignment = HorizontalAlignment.Right,
            BorderBottom = new CellBorder(BorderStyle.Thin, CellColor.Black)
        });
        ledger.GetCell(2, 2)!.StyleId = amountStyleId;
        ledger.GetCell(4, 2)!.StyleId = amountStyleId;
        ledger.AutoFilter = new WorksheetAutoFilterModel("A1:B4", null);
        ledger.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            ColumnId: 0,
            Values: ["Open"]));
        ledger.FilterHiddenRows.Add(3);

        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var reopened = adapter.Load(saved);

        reopened.Sheets.Select(sheet => sheet.Name).Should().Equal("Notes", "Ledger");
        reopened.GetSheet("Notes")!.GetValue(1, 1).Should().Be(new TextValue("Added from the sheet-tab UI"));

        var reopenedLedger = reopened.GetSheet("Ledger");
        reopenedLedger.Should().NotBeNull();
        reopenedLedger!.GetValue(2, 1).Should().Be(new TextValue("Open"));
        reopenedLedger.GetValue(2, 2).Should().Be(new NumberValue(1200.5));
        reopenedLedger.FilterHiddenRows.Should().Contain(3u);
        reopenedLedger.AutoFilter.Should().NotBeNull();
        reopenedLedger.AutoFilter!.Reference.Should().Be("A1:B4");
        reopenedLedger.AutoFilter.FilterColumns.Should().ContainSingle().Which.Values.Should().Equal("Open");

        var reopenedAmountStyle = reopened.GetStyle(reopenedLedger.GetCell(2, 2)!.StyleId);
        reopenedAmountStyle.Bold.Should().BeTrue();
        reopenedAmountStyle.FillColor.Should().Be(new CellColor(221, 235, 247));
        reopenedAmountStyle.NumberFormat.Should().Be("0.00");
        reopenedAmountStyle.HorizontalAlignment.Should().Be(HorizontalAlignment.Right);
        reopenedAmountStyle.BorderBottom.Should().Be(new CellBorder(BorderStyle.Thin, CellColor.Black));
    }

    private static void PopulateLedger(Sheet ledger)
    {
        ledger.SetCell(new CellAddress(ledger.Id, 1, 1), new TextValue("Status"));
        ledger.SetCell(new CellAddress(ledger.Id, 1, 2), new TextValue("Amount"));
        ledger.SetCell(new CellAddress(ledger.Id, 2, 1), new TextValue("Open"));
        ledger.SetCell(new CellAddress(ledger.Id, 2, 2), new NumberValue(1200.5));
        ledger.SetCell(new CellAddress(ledger.Id, 3, 1), new TextValue("Closed"));
        ledger.SetCell(new CellAddress(ledger.Id, 3, 2), new NumberValue(850));
        ledger.SetCell(new CellAddress(ledger.Id, 4, 1), new TextValue("Open"));
        ledger.SetCell(new CellAddress(ledger.Id, 4, 2), new NumberValue(430));
    }
}
