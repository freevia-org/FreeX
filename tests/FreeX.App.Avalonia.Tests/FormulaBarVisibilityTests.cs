using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class FormulaBarVisibilityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task Options_show_formula_bar_restores_the_row_hidden_by_the_View_toggle()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                window.FormulaBarVisibilityForTest.Should().Be((true, true, true));

                // View > Show > Formula Bar hides the whole row.
                window.ToggleFormulaBarVisibilityForTest();
                window.FormulaBarVisibilityForTest.Should().Be((false, false, false));

                // File > Options > Show formula bar must bring the whole row back (WPF collapses one
                // FormulaBarBorder from both paths). Before the fix only the input and Name Box were
                // re-shown inside a still-collapsed row, so the bar stayed invisible.
                window.ApplyOptionsShowFormulaBarForTest(showFormulaBar: true);
                window.FormulaBarVisibilityForTest.Should().Be((true, true, true));

                // And the reverse: Options hides the row, the View toggle shows it again in one press.
                window.ApplyOptionsShowFormulaBarForTest(showFormulaBar: false);
                window.FormulaBarVisibilityForTest.Should().Be((false, false, false));
                window.ToggleFormulaBarVisibilityForTest();
                window.FormulaBarVisibilityForTest.Should().Be((true, true, true));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Enter_in_the_expanded_formula_bar_commits_instead_of_typing_a_line_break()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                var sheet = window.Session.ActiveSheet;
                var address = new CellAddress(sheet.Id, 2, 2);
                window.Session.SelectCell(address);
                var formulaBox = window.GetVisualDescendants()
                    .OfType<TextBox>()
                    .Single(control => AutomationProperties.GetAutomationId(control) == "FormulaBox");

                // Ctrl+Shift+U / the chevron expands the bar into a multi-line editor.
                window.ToggleFormulaBarExpansionForTest();
                formulaBox.AcceptsReturn.Should().BeTrue();

                formulaBox.Focus().Should().BeTrue();
                await DrainInputAsync();
                formulaBox.SelectAll();
                formulaBox.RaiseEvent(new TextInputEventArgs
                {
                    RoutedEvent = InputElement.TextInputEvent,
                    Source = formulaBox,
                    Text = "42",
                });
                await DrainInputAsync();

                // Excel and the WPF host commit on Enter in the expanded bar (Alt+Enter is the line
                // break). Avalonia's TextBox consumed Enter first and typed a newline instead.
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await DrainInputAsync();

                sheet.GetValue(address).Should().Be(new NumberValue(42));
                window.Session.FormulaEditAddress.Should().BeNull();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
