using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    internal bool RaiseFormulaReferenceGripDragForTest(int highlightIndex, CellAddress target)
    {
        var editor = GetFormulaReferenceHighlightEditor();
        var highlights = editor is null
            ? []
            : GetFormulaReferenceHighlights(editor.Text);
        if (editor is null || highlightIndex < 0 || highlightIndex >= highlights.Count ||
            highlights[highlightIndex].Range is not { } originalRange ||
            originalRange.Start.Sheet != target.Sheet)
        {
            return false;
        }

        var newRange = _formulaRangeEditingSession.PlanReferenceDrag(highlights[highlightIndex], target);
        if (newRange is null)
            return false;

        ApplyFormulaReferenceResize(editor, highlights[highlightIndex], newRange.Value);
        RefreshFormulaReferenceHighlights();
        return true;
    }

    internal string FormulaBoxTextForTest
    {
        get => FormulaBar.Text;
        set => FormulaBar.Text = value;
    }

    internal void BeginFormulaPointModeEditForTest(CellAddress address, string formulaText)
    {
        if (!_formulaRangeEditingSession.IsFormulaText(formulaText))
            throw new ArgumentException("Formula point-mode text must start with '='.", nameof(formulaText));

        SheetGrid.SelectedRange = new GridRange(address, address);
        BeginFormulaBarFormulaEdit(formulaText);
    }

    internal void RaiseFormulaBoxKeyDownForTest(KeyEventArgs e) => FormulaBar_KeyDown(FormulaBar, e);

    internal bool RouteFormulaPointSelectionForTest(
        GridRange range,
        bool append = false,
        bool extendSelection = false) =>
        TryRouteFormulaPointModeSelection(range, append, extendSelection);

    internal Control? FindRenderedRibbonCommandControlForTest(string commandName) =>
        FindRenderedRibbonControl(commandName);

    internal void PopulateTableDesignStyleGalleryMenuForTest() =>
        PopulateTableDesignStyleGalleryMenu();
}
