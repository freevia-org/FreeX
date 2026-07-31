using Avalonia.Controls;
using Avalonia.Media;
using FluentAssertions;
using FreeX.App.Presentation;
using FreeX.App.Presentation.FormulaBar;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

public sealed class R89_ExternalFormulaReferenceParityTests
{
    [Fact]
    public void FormulaBar_ExternalWorkbookReferences_AreAtomicTextHighlightsWithoutLocalGridProjection()
    {
        var currentSheet = SheetId.New();
        var formula = "=SUM('[Data File.xlsx]Sheet1'!A1,[Data File.xlsx]Sheet1!B2)";
        var highlights = FormulaReferenceHighlightPlanner.GetHighlights(
            formula,
            currentSheet,
            sheetName => sheetName == "Sheet1" ? currentSheet : null);

        highlights.Select(static highlight => highlight.Text).Should().Equal(
            "'[Data File.xlsx]Sheet1'!A1",
            "[Data File.xlsx]Sheet1!B2");
        foreach (var highlight in highlights)
        {
            highlight.ExternalWorkbookName.Should().Be("Data File.xlsx");
            highlight.SheetName.Should().Be("Sheet1");
            highlight.Range.Should().BeNull();
        }

        var editor = new TextBox();
        var overlay = new TextBlock();
        MainWindow.ApplyFormulaReferenceTextOverlayForTest(
            editor,
            overlay,
            Brushes.Black,
            formula,
            highlights);

        overlay.IsVisible.Should().BeTrue();
        overlay.Inlines!.Count.Should().Be(5);
        editor.Foreground.Should().BeSameAs(Brushes.Transparent);
    }

    [Fact]
    public void FormulaBarF4_ExternalWorkbookReference_CyclesAnchorsAndPreservesQualifier()
    {
        const string formula = "=SUM('[Data File.xlsx]Sheet1'!$A$1)";
        ExcelTextEditorPlanner.TryCycleFormulaReference(
                formula,
                formula.IndexOf("A$1", StringComparison.Ordinal) + 1,
                out var edit)
            .Should()
            .BeTrue();

        edit.Text.Should().Be("=SUM('[Data File.xlsx]Sheet1'!A$1)");
        edit.Text.Should().Contain("'[Data File.xlsx]Sheet1'!");
        edit.SelectionLength.Should().Be("'[Data File.xlsx]Sheet1'!A$1".Length);
    }
}
