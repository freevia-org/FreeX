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
    public void UseInFormulaInsertion_SeedsFormulaBarWithoutInlineEditorOverwrite()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaBarText("=");

            harness.InsertDefinedNameIntoFormula("SalesData");

            harness.FormulaBarText.Should().Be("=SalesData");
            harness.InlineEditorVisible.Should().BeFalse();
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");
        });
    }

    [Fact]
    public void UseInFormulaInsertion_ReplacesDisplayedActiveCellValueWithFormulaSeed()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);

            harness.InsertDefinedNameIntoFormula("SalesData");

            harness.FormulaBarText.Should().Be("=SalesData");
            harness.FormulaBarCaretIndex.Should().Be("=SalesData".Length);
            harness.InlineEditorVisible.Should().BeFalse();
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");
        });
    }

    [Fact]
    public void UseInFormulaInsertion_EnterCommitsFormulaToActiveCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);

            harness.InsertDefinedNameIntoFormula("SalesData");
            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();

            harness.CellFormula(1, 1).Should().Be("SalesData");
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
    public void UseInFormulaInsertion_EscapeRestoresOriginalCellText()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);

            harness.InsertDefinedNameIntoFormula("SalesData");
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

    [Fact]
    public void UseInFormulaInsertion_InsertsDefinedNameAtFormulaBarCaret()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaBarText("=SUM(,B1)");
            harness.SetFormulaBarCaretIndex("=SUM(".Length);

            harness.InsertDefinedNameIntoFormula("SalesData");

            harness.FormulaBarText.Should().Be("=SUM(SalesData,B1)");
            harness.FormulaBarCaretIndex.Should().Be("=SUM(SalesData".Length);
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");
        });
    }

    [Fact]
    public void UseInFormulaInsertion_InsertsDefinedNameIntoDisplayedActiveFormulaAtCaret()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellFormula(1, 1, "SUM(,B1)");
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaBarCaretIndex("=SUM(".Length);

            harness.InsertDefinedNameIntoFormula("SalesData");

            harness.FormulaBarText.Should().Be("=SUM(SalesData,B1)");
            harness.FormulaBarCaretIndex.Should().Be("=SUM(SalesData".Length);
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellFormula(1, 1).Should().Be("SUM(,B1)");
        });
    }

    [Fact]
    public void UseInFormulaInsertion_PrependsFormulaPrefixForPlainFormulaBarText()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaBarText("A1+");
            harness.SetFormulaBarCaretIndex("A1+".Length);

            harness.InsertDefinedNameIntoFormula("SalesData");

            harness.FormulaBarText.Should().Be("=A1+SalesData");
            harness.FormulaBarCaretIndex.Should().Be(harness.FormulaBarText.Length);
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");
        });
    }
}
