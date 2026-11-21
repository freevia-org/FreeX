using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeX.App.Services;
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
            var window = CreateWindow(new AppOptions());
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
            var window = CreateWindow(new AppOptions());
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

    [Fact]
    public Task View_toggle_persists_visibility_and_updates_independent_workbook_windows() =>
        Session.Dispatch(() =>
        {
            var saved = new AppOptions();
            var first = CreateWindow(saved);
            var otherWorkbook = CreateWindow(saved);
            MainWindow? openedWhileHidden = null;
            try
            {
                first.Show();
                otherWorkbook.Show();
                first.Session.Workbook.Should().NotBeSameAs(otherWorkbook.Session.Workbook);

                first.ToggleFormulaBarVisibilityForTest();
                saved.ShowFormulaBar.Should().BeFalse();
                first.FormulaBarVisibilityForTest.Should().Be((false, false, false));
                otherWorkbook.FormulaBarVisibilityForTest.Should().Be((false, false, false));

                openedWhileHidden = CreateWindow(saved);
                openedWhileHidden.Show();
                openedWhileHidden.FormulaBarVisibilityForTest.Should().Be((false, false, false));

                otherWorkbook.ToggleFormulaBarVisibilityForTest();
                saved.ShowFormulaBar.Should().BeTrue();
                first.FormulaBarVisibilityForTest.Should().Be((true, true, true));
                otherWorkbook.FormulaBarVisibilityForTest.Should().Be((true, true, true));
                openedWhileHidden.FormulaBarVisibilityForTest.Should().Be((true, true, true));
            }
            finally
            {
                if (openedWhileHidden is not null)
                    Close(openedWhileHidden);
                Close(otherWorkbook);
                Close(first);
            }
        }, CancellationToken.None);

    [Fact]
    public Task Chevron_and_shortcut_remember_expansion_for_new_windows_without_resizing_open_siblings() =>
        Session.Dispatch(async () =>
        {
            var saved = new AppOptions();
            var first = CreateWindow(saved);
            var sibling = CreateWindow(saved);
            MainWindow? reopened = null;
            try
            {
                first.Show();
                sibling.Show();
                var chevron = first.GetVisualDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetAutomationId(button) == "FormulaBarExpandBtn");
                chevron.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                first.FormulaBarExpandedForTest.Should().BeTrue();
                sibling.FormulaBarExpandedForTest.Should().BeFalse();
                saved.FormulaBarExpanded.Should().BeTrue();
                // WPF RibbonTooltip: a bold "Title (BX)" line over the grey description.
                ToolTip.GetTip(chevron).Should().BeOfType<StackPanel>().Which
                    .Children.OfType<TextBlock>().Select(text => text.Text).Should().Contain(
                        UiText.Get("MainWindow_TooltipTitle_CollapseFormulaBar") + " (BX)");

                reopened = CreateWindow(saved);
                reopened.Show();
                reopened.FormulaBarExpandedForTest.Should().BeTrue();
                await reopened.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.U,
                    KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
                });

                saved.FormulaBarExpanded.Should().BeFalse();
                reopened.FormulaBarExpandedForTest.Should().BeFalse();
                first.FormulaBarExpandedForTest.Should().BeTrue();
            }
            finally
            {
                if (reopened is not null)
                    Close(reopened);
                Close(sibling);
                Close(first);
            }
            return true;
        }, CancellationToken.None);

    private static MainWindow CreateWindow(AppOptions saved) => new([], null!, new FreeXOptionsRuntimeSession(
        load: () => saved.Clone(),
        save: options =>
        {
            saved.CopyFrom(options);
            return true;
        }));

    private static void Close(MainWindow window)
    {
        window.AllowCloseWithoutDirtyPromptForParityCapture();
        window.Close();
    }

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
