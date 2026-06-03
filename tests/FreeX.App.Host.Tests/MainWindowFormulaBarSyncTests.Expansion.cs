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
    public void FormulaBarExpandButton_TogglesMultilineEntryAndAccessibilityName()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            if (harness.FormulaBarAcceptsReturn)
                harness.ToggleFormulaBarExpansion();

            harness.FormulaBarAcceptsReturn.Should().BeFalse();
            harness.FormulaBarHeight.Should().Be(double.NaN);
            harness.FormulaBarExpandButtonAutomationName.Should().Be(UiText.Get("MainWindow_AutomationName_ExpandFormulaBar"));

            harness.ToggleFormulaBarExpansion();

            harness.FormulaBarAcceptsReturn.Should().BeTrue();
            harness.FormulaBarHeight.Should().Be(84);
            harness.FormulaBarExpandButtonAutomationName.Should().Be(UiText.Get("MainWindow_AutomationName_CollapseFormulaBar"));

            harness.ToggleFormulaBarExpansion();

            harness.FormulaBarAcceptsReturn.Should().BeFalse();
            harness.FormulaBarHeight.Should().Be(double.NaN);
            harness.FormulaBarExpandButtonAutomationName.Should().Be(UiText.Get("MainWindow_AutomationName_ExpandFormulaBar"));
        });
    }

    [Fact]
    public void FormulaBarExpandButton_WhileFormulaBarFocused_PreservesDraftCaretAndFocus()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            if (harness.FormulaBarAcceptsReturn)
                harness.ToggleFormulaBarExpansion();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("draft edit");
            harness.SetFormulaBarCaretIndex("draft".Length);

            harness.ToggleFormulaBarExpansion();

            harness.FormulaBarAcceptsReturn.Should().BeTrue();
            harness.FormulaBarText.Should().Be("draft edit");
            harness.FormulaBarCaretIndex.Should().Be("draft".Length);
            harness.FormulaBarFocused.Should().BeTrue();
            harness.InlineEditorVisible.Should().BeFalse();
            harness.CellText(1, 1).Should().Be("original");

            harness.ToggleFormulaBarExpansion();

            harness.FormulaBarAcceptsReturn.Should().BeFalse();
            harness.FormulaBarText.Should().Be("draft edit");
            harness.FormulaBarCaretIndex.Should().Be("draft".Length);
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");
        });
    }

    [Fact]
    public void FormulaBarExpandButton_WithInlineEditorDraft_PreservesSynchronizedDraft()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            if (harness.FormulaBarAcceptsReturn)
                harness.ToggleFormulaBarExpansion();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);
            harness.SetInlineEditorText("inline draft");
            harness.FocusFormulaBar();

            harness.ToggleFormulaBarExpansion();

            harness.FormulaBarAcceptsReturn.Should().BeTrue();
            harness.FormulaBarText.Should().Be("inline draft");
            harness.InlineEditorText.Should().Be("inline draft");
            harness.InlineEditorVisible.Should().BeTrue();
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");

            harness.ToggleFormulaBarExpansion();

            harness.FormulaBarAcceptsReturn.Should().BeFalse();
            harness.FormulaBarText.Should().Be("inline draft");
            harness.InlineEditorText.Should().Be("inline draft");
            harness.InlineEditorVisible.Should().BeTrue();
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");
        });
    }
}
