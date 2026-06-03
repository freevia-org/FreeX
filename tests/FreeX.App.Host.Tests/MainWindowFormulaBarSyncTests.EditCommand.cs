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
    public void EditInFormulaBar_WithFormulaCell_ShowsEditableFormulaAndPlacesCaretAtEnd()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellFormula(1, 1, "SUM(B1:C1)");
            harness.SelectActiveCell(1, 1);

            harness.EditActiveCellInFormulaBar();

            harness.FormulaBarText.Should().Be("=SUM(B1:C1)");
            harness.FormulaBarCaretIndex.Should().Be(harness.FormulaBarText.Length);
            harness.FormulaBarFocused.Should().BeTrue();
            harness.InlineEditorVisible.Should().BeFalse();
            harness.CellFormula(1, 1).Should().Be("SUM(B1:C1)");
        });
    }

    [Fact]
    public void EditInFormulaBar_LoadsActiveCellFormulaAndFocusesFormulaBar()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellFormula(1, 1, "SUM(B1:C1)");
            harness.SelectActiveCell(1, 1);

            harness.EditActiveCellInFormulaBar();

            harness.FormulaBarText.Should().Be("=SUM(B1:C1)");
            harness.FormulaBarCaretIndex.Should().Be(harness.FormulaBarText.Length);
            harness.InlineEditorVisible.Should().BeFalse();
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellFormula(1, 1).Should().Be("SUM(B1:C1)");
        });
    }

    [Fact]
    public void EditInFormulaBar_LoadsActiveCellTextAndFocusesFormulaBar()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "plain text");
            harness.SelectActiveCell(1, 1);

            harness.EditActiveCellInFormulaBar();

            harness.FormulaBarText.Should().Be("plain text");
            harness.FormulaBarCaretIndex.Should().Be(harness.FormulaBarText.Length);
            harness.InlineEditorVisible.Should().BeFalse();
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("plain text");
        });
    }

    [Fact]
    public void EditInFormulaBar_WithEmptyActiveCell_ClearsFormulaBarAndFocusesAtStart()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "stale");
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaBarText("stale draft");
            harness.SelectActiveCell(2, 2);

            harness.EditActiveCellInFormulaBar();

            harness.FormulaBarText.Should().BeEmpty();
            harness.FormulaBarCaretIndex.Should().Be(0);
            harness.InlineEditorVisible.Should().BeFalse();
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellText(2, 2).Should().BeNull();
        });
    }
}
