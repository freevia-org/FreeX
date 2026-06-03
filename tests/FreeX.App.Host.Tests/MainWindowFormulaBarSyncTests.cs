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
    public void NewWorkbook_SelectsA1AndBindsFormulaBarEditsToA1()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var expected = new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 1),
                new CellAddress(harness.CurrentSheetId, 1, 1));

            harness.SelectedRange.Should().Be(expected);
            harness.CellAddressBoxText.Should().Be("A1");

            harness.SetFormulaBarText("fresh value");
            harness.CommitEdit().Should().BeTrue();

            harness.CellText(1, 1).Should().Be("fresh value");
            harness.SelectedRange.Should().Be(expected);
            harness.CellAddressBoxText.Should().Be("A1");
        });
    }

    [Fact]
    public void InsertedSheet_RebindsActiveCellToCurrentSheet()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var firstSheetId = harness.CurrentSheetId;

            harness.SetFormulaBarText("first sheet");
            harness.CommitEdit().Should().BeTrue();
            harness.InsertNewSheet();

            harness.CurrentSheetId.Should().NotBe(firstSheetId);
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 1),
                new CellAddress(harness.CurrentSheetId, 1, 1)));
            harness.CellAddressBoxText.Should().Be("A1");

            harness.SetFormulaBarText("second sheet");
            harness.CommitEdit().Should().BeTrue();

            harness.CellText(1, 1, firstSheetId).Should().Be("first sheet");
            harness.CellText(1, 1, harness.CurrentSheetId).Should().Be("second sheet");
        });
    }

    [Fact]
    public void ClearSelection_RefreshesFormulaBarForClearedActiveCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "stale text");
            harness.SelectActiveCell(1, 1);
            harness.FormulaBarText.Should().Be("stale text");

            harness.ClearSelectedContents();

            harness.CellText(1, 1).Should().BeNull();
            harness.FormulaBarText.Should().BeEmpty();
        });
    }
}
