using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for the fill-handle Ctrl-flip wiring (K3/K27): GridView raises
/// <c>AutofillModifiersResolved</c> with the Ctrl-key state immediately before
/// <c>AutofillRequested</c>, but the WPF host previously never subscribed to it, so
/// <c>AutofillCommand</c> always received the default <c>ctrlHeld: false</c> and Excel's
/// copy&lt;-&gt;series flip was unreachable from the shipped app. These tests assert the host
/// subscribes to the event and threads the captured value into the command construction.
/// </summary>
public sealed class MainWindowAutofillCtrlModifierWiringTests
{
    [Fact]
    public void MainWindow_SubscribesToAutofillModifiersResolved()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");

        source.Should().Contain("SheetGrid.AutofillModifiersResolved +=");

        // Must be wired before AutofillRequested is subscribed, mirroring the order GridView raises
        // the two events (AutofillModifiersResolved immediately precedes AutofillRequested), so the
        // captured field is up to date by the time OnAutofillRequested runs.
        var modifiersSubscribeIndex = source.IndexOf("SheetGrid.AutofillModifiersResolved +=", StringComparison.Ordinal);
        var requestedSubscribeIndex = source.IndexOf("SheetGrid.AutofillRequested += OnAutofillRequested;", StringComparison.Ordinal);

        modifiersSubscribeIndex.Should().BeGreaterThanOrEqualTo(0);
        requestedSubscribeIndex.Should().BeGreaterThan(modifiersSubscribeIndex);
    }

    [Fact]
    public void MainWindow_DeclaresAutofillCtrlHeldField()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");

        source.Should().Contain("_autofillCtrlHeld");
        source.Should().Contain("SheetGrid.AutofillModifiersResolved += ctrlHeld => _autofillCtrlHeld = ctrlHeld;");
    }

    [Fact]
    public void OnAutofillRequested_PassesCapturedCtrlHeldIntoAutofillCommand()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.CellsCommands.cs");
        var handlerStart = source.IndexOf("private void OnAutofillRequested", StringComparison.Ordinal);
        var helperStart = source.IndexOf("private void SelectCompletedAutofillRange", StringComparison.Ordinal);

        handlerStart.Should().BeGreaterThanOrEqualTo(0);
        helperStart.Should().BeGreaterThan(handlerStart);

        var handler = source[handlerStart..helperStart];

        // The dragged-fill path must thread the captured Ctrl state through ExecuteAutofill. The
        // grouped-sheet implementation must preserve that explicit value for every remapped
        // AutofillCommand rather than falling back to the overload that defaults it to false.
        handler.Should().Contain("ExecuteAutofill(sourceRange, fillRange, _autofillCtrlHeld);");
        handler.Should().Contain("CurrentGroupedEditSheetIds()");
        handler.Should().Contain("new AutofillCommand(");
        handler.Should().Contain("GroupedSheetRangePlanner.RemapRangeToSheet(sourceRange, sheetId)");
        handler.Should().Contain("GroupedSheetRangePlanner.RemapRangeToSheet(fillRange, sheetId)");
        handler.Should().Contain("ctrlHeld))");
    }

    [Fact]
    public void OnAutofillHandleDoubleClicked_PassesHardcodedFalseIntoExecuteAutofill_NotStaleField()
    {
        // Regression guard for R79-commands-autofill-series-5-1: double-click never raises the
        // paired AutofillModifiersResolved event (that only fires at drag-release), so
        // _autofillCtrlHeld can hold a stale value from an earlier Ctrl-held drag. Excel's
        // double-click fill always behaves like a plain (non-Ctrl) drag, so the double-click
        // handler must pass a hardcoded ctrlHeld: false into ExecuteAutofill instead of reading
        // the possibly-stale field.
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var handlerStart = source.IndexOf("private void OnAutofillHandleDoubleClicked", StringComparison.Ordinal);
        var handlerEnd = source.IndexOf("private void ResolveAdjacentColumnLastPopulatedRow", StringComparison.Ordinal);
        if (handlerEnd < 0)
        {
            handlerEnd = source.IndexOf("private ", handlerStart + 1, StringComparison.Ordinal);
        }

        handlerStart.Should().BeGreaterThanOrEqualTo(0);
        handlerEnd.Should().BeGreaterThan(handlerStart);

        var handler = source[handlerStart..handlerEnd];

        // The fill call itself must pass a hardcoded false, not read the (possibly stale) field --
        // this only checks the actual argument expression, not any explanatory comment text that
        // may mention the field's name.
        handler.Should().Contain("ExecuteAutofill(source, fillRange.Value, ctrlHeld: false);");
        handler.Should().NotContain("ExecuteAutofill(source, fillRange.Value, _autofillCtrlHeld");
    }
}
