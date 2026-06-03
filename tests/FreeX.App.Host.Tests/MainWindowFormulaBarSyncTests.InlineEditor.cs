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
    public void InlineEditorTextChange_RefreshesFormulaBar()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);

            harness.SetInlineEditorText("typed inline");

            harness.FormulaBarText.Should().Be("typed inline");
        });
    }

    [Fact]
    public void FormulaBarTextChange_WhileInlineEditorVisible_RefreshesInlineEditor()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);

            harness.SetFormulaBarText("typed in formula bar");

            harness.InlineEditorText.Should().Be("typed in formula bar");
        });
    }

    [Fact]
    public void FocusFormulaBar_WhileInlineEditorVisible_DoesNotCommitDraftEdit()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);
            harness.SetInlineEditorText("draft edit");

            harness.FocusFormulaBar();

            harness.FormulaBarFocused.Should().BeTrue();
            harness.InlineEditorVisible.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");
            harness.FormulaBarText.Should().Be("draft edit");
        });
    }

    [Fact]
    public void EditInFormulaBar_WhileInlineEditorVisible_PreservesDraftAndFocusesFormulaBar()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);
            harness.SetInlineEditorText("draft edit");

            harness.EditActiveCellInFormulaBar();

            harness.FormulaBarText.Should().Be("draft edit");
            harness.FormulaBarCaretIndex.Should().Be(harness.FormulaBarText.Length);
            harness.InlineEditorVisible.Should().BeTrue();
            harness.InlineEditorText.Should().Be("draft edit");
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");
        });
    }

    [Fact]
    public void FormulaBarEdit_WhileInlineEditorVisible_SyncsInlineEditorAndPreservesFocus()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);
            harness.SetInlineEditorText("inline draft");
            harness.FocusFormulaBar();

            harness.SetFormulaBarText("formula bar draft");
            harness.SetFormulaBarCaretIndex("formula".Length);

            harness.FormulaBarText.Should().Be("formula bar draft");
            harness.FormulaBarCaretIndex.Should().Be("formula".Length);
            harness.InlineEditorVisible.Should().BeTrue();
            harness.InlineEditorText.Should().Be("formula bar draft");
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");
        });
    }
}
