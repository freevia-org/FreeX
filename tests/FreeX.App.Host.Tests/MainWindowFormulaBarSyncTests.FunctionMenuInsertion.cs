using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowFormulaBarSyncTests
{
    [Fact]
    public void FunctionMenuInsertion_SeedsFormulaBarWithoutInlineEditorOverwrite()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);

            harness.InsertFormulaFunction("SUM");

            harness.FormulaBarText.Should().Be("=SUM(");
            harness.FormulaBarCaretIndex.Should().Be("=SUM(".Length);
            harness.InlineEditorVisible.Should().BeFalse();
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");
        });
    }

    [Fact]
    public void FunctionMenuInsertion_EnterCommitsCompletedFormulaToActiveCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);

            harness.InsertFormulaFunction("SUM");
            harness.SetFormulaBarText("=SUM(1,2)");
            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();

            harness.CellFormula(1, 1).Should().Be("SUM(1,2)");
            harness.CellText(1, 1).Should().BeNull();
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 1),
                new CellAddress(harness.CurrentSheetId, 2, 1)));
            harness.CellAddressBoxText.Should().Be("A2");
            harness.FormulaBarText.Should().BeEmpty();
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void FunctionMenuInsertion_EscapeRestoresOriginalCellText()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);

            harness.InsertFormulaFunction("SUM");
            harness.SetFormulaBarText("=SUM(1,2)");
            harness.PressFormulaBarKey(Key.Escape).Should().BeTrue();

            harness.CellText(1, 1).Should().Be("original");
            harness.CellFormula(1, 1).Should().BeNull();
            harness.FormulaBarText.Should().Be("original");
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 1),
                new CellAddress(harness.CurrentSheetId, 1, 1)));
            harness.SheetGridFocused.Should().BeTrue();
        });
    }
}
