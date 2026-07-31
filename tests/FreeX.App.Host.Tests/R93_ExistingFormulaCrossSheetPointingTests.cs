using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class R93_ExistingFormulaCrossSheetPointingTests
{
    [Fact]
    public void ExistingFormulaEdit_ShiftedSheetTabs_PreservesThreeDSheetQualifier()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();
            var sourceSheet = harness.FirstSheet;
            sourceSheet.Name = "Summary";
            var middleSheet = harness.AddSheet("Middle Sheet");
            var endSheet = harness.AddSheet("Final Sheet");
            var formulaAddress = new CellAddress(sourceSheet.Id, 8, 7);

            sourceSheet.SetCell(formulaAddress, Cell.FromFormula("SUM("));
            harness.SelectActiveCell(8, 7);
            harness.EditActiveCellInFormulaBar();
            harness.FormulaBarText.Should().Be("=SUM(");
            harness.FormulaRangeEntryMode.Should().BeFalse();

            harness.SelectFormulaSheetTab(middleSheet.Id, ModifierKeys.None);
            harness.SelectFormulaSheetTab(endSheet.Id, ModifierKeys.Shift);
            harness.FormulaEditCell.Should().Be(formulaAddress);

            harness.PressFormulaBarKey(Key.F2).Should().BeTrue();
            harness.FormulaRangeEntryMode.Should().BeTrue();
            harness.SetFormulaBarCaretIndex("=SUM(".Length);

            harness.ApplyFormulaRangeSelection(endSheet.Id, 2, 2, extend: false).Should().BeTrue();
            harness.FormulaBarText.Should().Be("=SUM('Middle Sheet:Final Sheet'!B2");
        });
    }
}
