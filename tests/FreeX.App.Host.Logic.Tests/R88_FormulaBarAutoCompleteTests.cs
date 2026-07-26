using System.Reflection;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R88-app-autocomplete-picklist-5-3 (MainWindow.Editing.cs /
/// MainWindow.xaml.cs): typing directly into the Formula Bar -- the "click straight into the
/// Formula Bar without the inline editor being shown" edit-start path
/// (<c>EditActiveCellInFormulaBar</c>) -- never triggered Excel's "AutoComplete for cell values".
/// Only the in-cell inline editor's own TextChanged handler ever called into
/// <see cref="FreeX.Core.Commands.CellValueAutoCompleteSuggester"/>; the Formula Bar's TextChanged
/// handler never did.
/// </summary>
public sealed class R88_FormulaBarAutoCompleteTests
{
    [Fact]
    public void ApplyCellValueAutoCompleteSuggestion_OnFormulaBarEditor_SuggestsMatchingColumnEntry()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var a1 = new CellAddress(sheet.Id, 1, 1);
                var a2 = new CellAddress(sheet.Id, 2, 1);
                sheet.SetCell(a1, new TextValue("Apple"));

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", a2);
                R49MainWindowTestHarness.Invoke(window, "EditActiveCellInFormulaBar");

                GetInlineEditor(window).Should().BeNull(
                    "clicking straight into the Formula Bar must not also show the inline in-cell editor -- " +
                    "that gap is exactly what left the Formula Bar's own AutoComplete unwired");

                var formulaBar = (TextBox)window.FindName("FormulaBar")!;
                formulaBar.Text = "Ap";
                formulaBar.CaretIndex = formulaBar.Text.Length;
                formulaBar.SelectionLength = 0;

                InvokeApplyCellValueAutoCompleteSuggestion(window, formulaBar);

                formulaBar.Text.Should().Be(
                    "Apple",
                    "typing in the Formula Bar must offer the same column AutoComplete suggestion as the in-cell editor");
                formulaBar.SelectionStart.Should().Be(2);
                formulaBar.SelectionLength.Should().Be(3);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: a formula entry ("=...") in the Formula Bar must never be touched by
    // AutoComplete, matching the in-cell editor's own formula guard.
    [Fact]
    public void ApplyCellValueAutoCompleteSuggestion_OnFormulaBarEditor_IgnoresFormulaText()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var a1 = new CellAddress(sheet.Id, 1, 1);
                var a2 = new CellAddress(sheet.Id, 2, 1);
                sheet.SetCell(a1, new TextValue("Apple"));

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", a2);
                R49MainWindowTestHarness.Invoke(window, "EditActiveCellInFormulaBar");

                var formulaBar = (TextBox)window.FindName("FormulaBar")!;
                formulaBar.Text = "=Ap";
                formulaBar.CaretIndex = formulaBar.Text.Length;
                formulaBar.SelectionLength = 0;

                InvokeApplyCellValueAutoCompleteSuggestion(window, formulaBar);

                formulaBar.Text.Should().Be(
                    "=Ap", "a formula entry must never be auto-completed from column text entries");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static void InvokeApplyCellValueAutoCompleteSuggestion(MainWindow window, TextBox editor)
    {
        var method = typeof(MainWindow).GetMethod(
            "ApplyCellValueAutoCompleteSuggestion",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(TextBox)],
            modifiers: null)
            ?? throw new MissingMethodException(nameof(MainWindow), "ApplyCellValueAutoCompleteSuggestion(TextBox)");
        method.Invoke(window, [editor]);
    }

    private static TextBox? GetInlineEditor(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_inlineEditor", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_inlineEditor");
        return (TextBox?)field.GetValue(window);
    }
}
