using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class MainWindowAutofillSelectionSourceTests
{
    [Fact]
    public void AutofillRequestSelectsCompletedSourceAndFillRangeAfterCommandSucceeds()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.CellsCommands.cs");
        var handlerStart = source.IndexOf("private void OnAutofillRequested", StringComparison.Ordinal);
        var helperStart = source.IndexOf("private void SelectCompletedAutofillRange", StringComparison.Ordinal);
        var nextHandlerStart = source.IndexOf("private void OnSelectionMoveRequested", StringComparison.Ordinal);

        handlerStart.Should().BeGreaterThanOrEqualTo(0);
        helperStart.Should().BeGreaterThan(handlerStart);
        nextHandlerStart.Should().BeGreaterThan(helperStart);

        var handler = source[handlerStart..helperStart];
        handler.Should().Contain("if (!TryExecuteCommand(cmd, \"Autofill\", out var outcome))");
        handler.Should().Contain("SelectCompletedAutofillRange(sourceRange, fillRange);");
        handler.IndexOf("SelectCompletedAutofillRange(sourceRange, fillRange);", StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(handler.IndexOf("if (!TryExecuteCommand(cmd, \"Autofill\", out var outcome))", StringComparison.Ordinal));
        handler.Should().NotContain("RecalculateIfAutomatic(outcome.AffectedCells ?? []);");

        var helper = source[helperStart..nextHandlerStart];
        helper.Should().Contain("GridAutofillPlanner.CalculateCompletedSelectionRange(sourceRange, fillRange)");
        helper.Should().Contain("sheet.ActiveRow = selectionRange.Start.Row;");
        helper.Should().Contain("sheet.ActiveCol = selectionRange.Start.Col;");
        helper.Should().Contain("SetSelectedRangesIfChanged(null);");
        helper.Should().Contain("SheetGrid.SelectedRange = selectionRange;");
        helper.Should().Contain("SetCellAddressBoxSelectionText(FormatNameBoxSelectionText(selectionRange));");
    }
}
