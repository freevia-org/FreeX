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

        // The command must be constructed with the captured Ctrl state, not the 3-arg overload that
        // silently defaults ctrlHeld to false.
        handler.Should().Contain("new AutofillCommand(_currentSheetId, sourceRange, fillRange, _autofillCtrlHeld);");
    }
}
