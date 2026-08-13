using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class DialogTabCycleLogicalGraphTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task TemplatedControls_MoveOnceThroughAuthoredLogicalStops_AndWrapBothWays()
    {
        await Session.Dispatch(async () =>
        {
            var textBox = new TextBox { Text = "Field" };
            var comboBox = new ComboBox { ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0 };
            var checkBox = new CheckBox { Content = "Enabled", IsChecked = true };
            var listBox = new ListBox { ItemsSource = new[] { "Alpha", "Beta" }, SelectedIndex = 0 };
            var authoredStops = new Control[] { textBox, comboBox, checkBox, listBox };
            var root = new StackPanel { Children = { textBox, comboBox, checkBox, listBox } };
            var dialog = new Window { Content = root, Width = 320, Height = 280 };

            ConfigureTabCycle(dialog, root);
            dialog.Show();
            dialog.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

            try
            {
                textBox.Focus().Should().BeTrue();
                AssertStep(dialog, authoredStops, comboBox, reverse: false);
                AssertStep(dialog, authoredStops, checkBox, reverse: false);
                AssertStep(dialog, authoredStops, listBox, reverse: false);
                AssertStep(dialog, authoredStops, textBox, reverse: false);

                textBox.Focus().Should().BeTrue();
                AssertStep(dialog, authoredStops, listBox, reverse: true);
                AssertStep(dialog, authoredStops, checkBox, reverse: true);
                AssertStep(dialog, authoredStops, comboBox, reverse: true);
                AssertStep(dialog, authoredStops, textBox, reverse: true);
            }
            finally
            {
                dialog.Close();
            }

            await Task.CompletedTask;
            return 0;
        }, CancellationToken.None);
    }

    private static void ConfigureTabCycle(Window dialog, Control root)
    {
        MainWindow.ConfigureDialogTabCycleForTest(dialog, root);
    }

    private static void AssertStep(
        Window dialog,
        IReadOnlyList<Control> authoredStops,
        Control expected,
        bool reverse)
    {
        MainWindow.SendDialogKeyForTest(
                dialog,
                Key.Tab,
                reverse ? RawInputModifiers.Shift : RawInputModifiers.None,
                out var error)
            .Should().BeTrue(error);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

        var focused = dialog.FocusManager?.GetFocusedElement() as Control;
        focused.Should().NotBeNull();
        ResolveAuthoredStop(focused!, authoredStops).Should().BeSameAs(expected);
    }

    private static Control? ResolveAuthoredStop(Control focused, IReadOnlyList<Control> authoredStops)
    {
        if (authoredStops.Contains(focused))
            return focused;

        var ancestors = focused.GetVisualAncestors().OfType<Control>().ToHashSet();
        return authoredStops.FirstOrDefault(ancestors.Contains);
    }
}
