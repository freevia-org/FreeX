using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class R90_CrossWorkbookFormulaPointModeAvaloniaTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task TwoWorkbookWindows_RouteReplaceAppendF4AndCommitToFormulaOwner()
    {
        await Session.Dispatch(() =>
        {
            var owner = new MainWindow([]);
            var source = new MainWindow([]);
            try
            {
                owner.Session.Workbook.Name = "Owner.xlsx";
                source.Session.Workbook.Name = "Source.xlsx";
                owner.Session.ActiveSheet.Name = "Owner";
                source.Session.ActiveSheet.Name = "Input Data";
                owner.Show();
                source.Show();
                var formulaCell = new CellAddress(owner.Session.ActiveSheet.Id, 8, 7);
                var firstRange = Range(source.Session.ActiveSheet.Id, 2, 2, 2, 2);
                var secondRange = Range(source.Session.ActiveSheet.Id, 4, 3, 4, 3);

                owner.Session.SelectCell(formulaCell);
                owner.BeginFormulaPointModeEditForTest(formulaCell, "=SUM(");
                source.RouteFormulaPointSelectionForTest(firstRange).Should().BeTrue();
                owner.FormulaBoxTextForTest.Should().Be("=SUM('[Source.xlsx]Input Data'!B2");
                source.Session.SelectedRange.Should().Be(firstRange);

                source.RouteFormulaPointSelectionForTest(secondRange, append: true).Should().BeTrue();
                owner.FormulaBoxTextForTest.Should().Be(
                    "=SUM('[Source.xlsx]Input Data'!B2,'[Source.xlsx]Input Data'!C4");

                source.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.F4 });
                owner.FormulaBoxTextForTest.Should().Contain("'[Source.xlsx]Input Data'!$C$4");
                owner.FormulaBoxTextForTest += ")";
                source.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter });
                owner.Session.ActiveSheet.GetCell(formulaCell)!.FormulaText.Should().Contain(
                    "'[Source.xlsx]Input Data'!");
                owner.HasActiveFormulaPointMode.Should().BeFalse();
            }
            finally
            {
                source.Close();
                owner.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TwoWorkbookWindows_RouteEscapeToOwnerAndRestoreOriginalCell()
    {
        await Session.Dispatch(() =>
        {
            var owner = new MainWindow([]);
            var source = new MainWindow([]);
            try
            {
                owner.Session.Workbook.Name = "Owner.xlsx";
                source.Session.Workbook.Name = "Source.xlsx";
                owner.Show();
                source.Show();
                var formulaCell = new CellAddress(owner.Session.ActiveSheet.Id, 8, 7);
                var sourceRange = Range(source.Session.ActiveSheet.Id, 3, 3, 4, 4);

                owner.Session.SelectCell(formulaCell);
                owner.BeginFormulaPointModeEditForTest(formulaCell, "=SUM(");
                source.RouteFormulaPointSelectionForTest(sourceRange).Should().BeTrue();
                owner.FormulaBoxTextForTest.Should().Contain("[Source.xlsx]");

                source.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Escape });

                owner.HasActiveFormulaPointMode.Should().BeFalse();
                owner.Session.ActiveSheet.GetCell(formulaCell)?.FormulaText.Should().BeNull();
                owner.FormulaBoxTextForTest.Should().BeEmpty();
            }
            finally
            {
                source.Close();
                owner.Close();
            }
        }, CancellationToken.None);
    }

    private static GridRange Range(SheetId sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet, startRow, startCol), new CellAddress(sheet, endRow, endCol));
}
