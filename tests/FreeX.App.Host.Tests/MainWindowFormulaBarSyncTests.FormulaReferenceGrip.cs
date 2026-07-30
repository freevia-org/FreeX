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

    [Fact]
    public void ThreeDSheetRange_OnMiddleSheet_ShowsGripResizesAndCalculates()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var sourceSheet = harness.FirstSheet;
            sourceSheet.Name = "Source Sheet";
            var middleSheet = harness.AddSheet("Middle Sheet");
            var endSheet = harness.AddSheet("Final Sheet");

            for (uint row = 1; row <= 3; row++)
            {
                for (uint col = 1; col <= 3; col++)
                {
                    middleSheet.SetCell(new CellAddress(middleSheet.Id, row, col), new NumberValue(row * 3 + col));
                    endSheet.SetCell(new CellAddress(endSheet.Id, row, col), new NumberValue(9 + row * 3 + col));
                }
            }

            var formulaAddress = new CellAddress(sourceSheet.Id, 8, 7);
            var formula = "=SUM('Middle Sheet:Final Sheet'!A1:B2)";
            sourceSheet.SetCell(formulaAddress, Cell.FromFormula(formula[1..]));
            harness.SelectActiveCell(8, 7);
            harness.EditActiveCellInFormulaBar();
            harness.SelectFormulaSheetTab(middleSheet.Id, ModifierKeys.None);
            harness.CurrentSheetId.Should().Be(middleSheet.Id);

            harness.RaiseFormulaReferenceGripDrag(0, 3, 3).Should().BeTrue();
            harness.FormulaBarText.Should().Be("=SUM('Middle Sheet:Final Sheet'!A1:C3)");
            harness.CommitEdit().Should().BeTrue();

            sourceSheet.GetCell(formulaAddress)!.FormulaText.Should().Be("SUM('Middle Sheet:Final Sheet'!A1:C3)");
            sourceSheet.GetValue(formulaAddress).Should().Be(new NumberValue(225));
        });
    }

    [Fact]
    public void ThreeDSheetRange_NativeXlsxRoundTrip_PreservesEscapedReverseQualifierAndResult()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var summarySheet = harness.FirstSheet;
            summarySheet.Name = "Summary";
            var forwardSheet = harness.AddSheet("Revenue Data");
            var reverseSheet = harness.AddSheet("O'Brien Data");

            for (uint row = 2; row <= 4; row++)
            {
                for (uint col = 2; col <= 4; col++)
                {
                    forwardSheet.SetCell(new CellAddress(forwardSheet.Id, row, col),
                        new NumberValue((row - 1) * 3 + col - 1));
                    reverseSheet.SetCell(new CellAddress(reverseSheet.Id, row, col),
                        new NumberValue(10 + (row - 1) * 3 + col - 1));
                }
            }

            var formulaAddress = new CellAddress(summarySheet.Id, 8, 7);
            harness.SetCellFormula(8, 7, "SUM('O''Brien Data:Revenue Data'!B2:C3)");
            harness.SelectActiveCell(8, 7);
            harness.EditActiveCellInFormulaBar();
            harness.SelectFormulaSheetTab(forwardSheet.Id, ModifierKeys.None);
            harness.RaiseFormulaReferenceGripDrag(0, 4, 4).Should().BeTrue();
            harness.FormulaBarText.Should().Be("=SUM('O''Brien Data:Revenue Data'!B2:D4)");
            harness.CommitEdit().Should().BeTrue();

            summarySheet.GetCell(formulaAddress)!.FormulaText
                .Should().Be("SUM('O''Brien Data:Revenue Data'!B2:D4)");
            summarySheet.GetValue(formulaAddress).Should().Be(new NumberValue(234));

            using var stream = new MemoryStream();
            new XlsxFileAdapter().Save(harness.ActiveWorkbook, stream);
            stream.Position = 0;
            var reopened = new XlsxFileAdapter().Load(stream);
            var reopenedSummary = reopened.Sheets.Single(sheet => sheet.Name == "Summary");
            var reopenedFormulaAddress = new CellAddress(reopenedSummary.Id, 8, 7);
            reopenedSummary.GetCell(reopenedFormulaAddress)!.FormulaText
                .Should().Be("SUM('O''Brien Data:Revenue Data'!B2:D4)");
            reopenedSummary.GetValue(reopenedFormulaAddress).Should().Be(new NumberValue(234));
        });
    }
}
