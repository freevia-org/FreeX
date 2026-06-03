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
    public void NameBoxEnter_NavigatesRefreshesFormulaBarAndReturnsFocusToGrid()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(5, 3, "target cell");
            harness.SetCellAddressBoxText("C5");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 5, 3),
                new CellAddress(harness.CurrentSheetId, 5, 3)));
            harness.CellAddressBoxText.Should().Be("C5");
            harness.FormulaBarText.Should().Be("target cell");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void NameBoxEnter_WithPaddedCellReference_NavigatesRefreshesFormulaBarAndReturnsFocusToGrid()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(5, 3, "padded target cell");
            harness.SetCellAddressBoxText("  C5  ");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 5, 3),
                new CellAddress(harness.CurrentSheetId, 5, 3)));
            harness.CellAddressBoxText.Should().Be("C5");
            harness.FormulaBarText.Should().Be("padded target cell");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void NameBoxEnter_WithRangeReference_SelectsRangeAndRefreshesFormulaBarFromStartCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(2, 2, "range start");
            harness.SetCellText(3, 3, "range end");
            harness.SetCellAddressBoxText("B2:C3");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 2),
                new CellAddress(harness.CurrentSheetId, 3, 3)));
            harness.CellAddressBoxText.Should().Be("B2:C3");
            harness.FormulaBarText.Should().Be("range start");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void NameBoxEnter_WithDefinedName_SelectsNamedRangeAndRefreshesFormulaBarFromStartCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var expectedRange = new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 2),
                new CellAddress(harness.CurrentSheetId, 3, 3));

            harness.SetCellText(2, 2, "named range start");
            harness.DefineNamedRange("SalesData", expectedRange);
            harness.SelectActiveCell(1, 1);
            harness.SetCellAddressBoxText("SalesData");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.SelectedRange.Should().Be(expectedRange);
            harness.CellAddressBoxText.Should().Be("B2:C3");
            harness.FormulaBarText.Should().Be("named range start");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void NameBoxEnter_WithDifferentCaseDefinedName_SelectsNamedRangeAndRefreshesFormulaBarFromStartCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var expectedRange = new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 2),
                new CellAddress(harness.CurrentSheetId, 3, 3));

            harness.SetCellText(2, 2, "case-insensitive name start");
            harness.DefineNamedRange("SalesData", expectedRange);
            harness.SelectActiveCell(1, 1);
            harness.SetCellAddressBoxText("salesdata");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.SelectedRange.Should().Be(expectedRange);
            harness.CellAddressBoxText.Should().Be("B2:C3");
            harness.FormulaBarText.Should().Be("case-insensitive name start");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void NameBoxEnter_WithPaddedDefinedName_SelectsNamedRangeAndRefreshesFormulaBarFromStartCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var expectedRange = new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 2),
                new CellAddress(harness.CurrentSheetId, 3, 3));

            harness.SetCellText(2, 2, "padded name start");
            harness.DefineNamedRange("SalesData", expectedRange);
            harness.SelectActiveCell(1, 1);
            harness.SetCellAddressBoxText("  SalesData  ");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.SelectedRange.Should().Be(expectedRange);
            harness.CellAddressBoxText.Should().Be("B2:C3");
            harness.FormulaBarText.Should().Be("padded name start");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void NameBoxEnter_WithInvalidReference_DoesNotChangeSelectionOrFormulaBar()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "active cell");
            harness.SelectActiveCell(1, 1);
            harness.SetCellAddressBoxText("not a reference");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 1),
                new CellAddress(harness.CurrentSheetId, 1, 1)));
            harness.CellAddressBoxText.Should().Be("not a reference");
            harness.CellAddressBoxFocused.Should().BeTrue();
            harness.CellAddressBoxSelectionLength.Should().Be(harness.CellAddressBoxText.Length);
            harness.FormulaBarText.Should().Be("active cell");
        });
    }

    [Fact]
    public void NameBoxEnter_WithValidNewName_DefinesNameForSelectedRangeAndReturnsFocusToGrid()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var expectedRange = new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 2),
                new CellAddress(harness.CurrentSheetId, 4, 3));

            harness.SelectRange(2, 2, 4, 3);
            harness.SetCellAddressBoxText("SalesData");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.NamedRange("SalesData").Should().Be(expectedRange);
            harness.SelectedRange.Should().Be(expectedRange);
            harness.CellAddressBoxText.Should().Be("SalesData");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void NameBoxEnter_WithPaddedValidNewName_DefinesTrimmedNameForSelectedRange()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var expectedRange = new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 2),
                new CellAddress(harness.CurrentSheetId, 4, 3));

            harness.SelectRange(2, 2, 4, 3);
            harness.SetCellAddressBoxText("  SalesData  ");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.NamedRange("SalesData").Should().Be(expectedRange);
            harness.SelectedRange.Should().Be(expectedRange);
            harness.CellAddressBoxText.Should().Be("SalesData");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void NameBoxEscape_RestoresSelectedRangeReferenceAndReturnsFocusToGrid()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var expectedRange = new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 2),
                new CellAddress(harness.CurrentSheetId, 3, 3));

            harness.SelectRange(2, 2, 3, 3);
            harness.SetCellAddressBoxText("Z99");

            harness.PressCellAddressBoxKey(Key.Escape).Should().BeTrue();

            harness.SelectedRange.Should().Be(expectedRange);
            harness.CellAddressBoxText.Should().Be("B2:C3");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }
}
