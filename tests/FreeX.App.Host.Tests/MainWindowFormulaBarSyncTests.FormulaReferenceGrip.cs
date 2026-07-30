using FluentAssertions;
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
}
