using System.IO;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowFormulaBarSyncTests
{
    [Fact]
    public void QuotedSameSheetFormulaReferenceGrip_ResizesCommitsAndCalculates()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.RenameFirstSheet("Revenue Data");

            harness.SetCellNumber(2, 2, 1); // B2
            harness.SetCellNumber(3, 3, 2); // C3
            harness.SetCellNumber(4, 4, 3); // D4
            harness.SetCellNumber(5, 5, 4); // E5
            harness.SetCellNumber(6, 6, 5); // F6
            harness.SetCellFormula(8, 7, "SUM('Revenue Data'!B2:C3,'Revenue Data'!D4:E5)");

            harness.SelectActiveCell(8, 7);
            harness.EditActiveCellInFormulaBar();

            harness.RaiseFormulaReferenceGripDrag(1, 6, 6).Should().BeTrue();
            harness.FormulaBarText.Should().Be("=SUM('Revenue Data'!B2:C3,'Revenue Data'!D4:F6)");

            harness.CommitEdit().Should().BeTrue();
            harness.CellFormula(8, 7).Should().Be("SUM('Revenue Data'!B2:C3,'Revenue Data'!D4:F6)");
            harness.CellValue(8, 7).Should().Be(new NumberValue(15));
        });
    }

    [Fact]
    public void QualifiedFormula_SwitchesToReferencedSheet_ResizesAndRoundTrips()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var sourceSheet = harness.FirstSheet;
            sourceSheet.Name = "Source Sheet";
            var targetSheet = harness.AddSheet("Revenue Data");

            targetSheet.SetCell(new CellAddress(targetSheet.Id, 2, 2), new NumberValue(1)); // B2
            targetSheet.SetCell(new CellAddress(targetSheet.Id, 3, 3), new NumberValue(2)); // C3
            targetSheet.SetCell(new CellAddress(targetSheet.Id, 4, 4), new NumberValue(3)); // D4
            targetSheet.SetCell(new CellAddress(targetSheet.Id, 5, 5), new NumberValue(4)); // E5
            targetSheet.SetCell(new CellAddress(targetSheet.Id, 6, 6), new NumberValue(5)); // F6
            harness.SetCellFormula(8, 7, "SUM('Revenue Data'!B2:C3,'Revenue Data'!D4:E5)");

            harness.SelectActiveCell(8, 7);
            harness.EditActiveCellInFormulaBar();
            harness.FormulaBarText.Should().Be("=SUM('Revenue Data'!B2:C3,'Revenue Data'!D4:E5)");
            harness.SelectFormulaSheetTab(targetSheet.Id, ModifierKeys.None);
            harness.CurrentSheetId.Should().Be(targetSheet.Id);
            harness.FormulaBarText.Should().Be("=SUM('Revenue Data'!B2:C3,'Revenue Data'!D4:E5)");
            harness.FormulaEditCell.Should().Be(new CellAddress(sourceSheet.Id, 8, 7));

            harness.RaiseFormulaReferenceGripDrag(1, 6, 6).Should().BeTrue();
            harness.FormulaBarText.Should().Be("=SUM('Revenue Data'!B2:C3,'Revenue Data'!D4:F6)");

            harness.CommitEdit().Should().BeTrue();
            targetSheet.GetCell(new CellAddress(targetSheet.Id, 8, 7))?.FormulaText.Should().BeNull();
            harness.CellFormula(8, 7).Should().Be("SUM('Revenue Data'!B2:C3,'Revenue Data'!D4:F6)");
            harness.CellValue(8, 7).Should().Be(new NumberValue(15));

            using var stream = new MemoryStream();
            new NativeJsonAdapter().Save(harness.ActiveWorkbook, stream);
            stream.Position = 0;
            var reopened = new NativeJsonAdapter().Load(stream);
            var reopenedSource = reopened.Sheets.Single(sheet => sheet.Name == "Source Sheet");
            var reopenedFormulaAddress = new CellAddress(reopenedSource.Id, 8, 7);
            reopenedSource.GetCell(reopenedFormulaAddress)!.FormulaText
                .Should().Be("SUM('Revenue Data'!B2:C3,'Revenue Data'!D4:F6)");
            reopenedSource.GetValue(reopenedFormulaAddress).Should().Be(new NumberValue(15));
        });
    }
}
