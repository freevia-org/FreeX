using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class UnhideWindowDialogTests
{
    [Fact]
    public void UnhideWindowDialog_ExposesExcelLikeHiddenWindowList()
    {
        StaTestRunner.Run(() =>
        {
            var w1 = new TestWorkbookWindow();
            var w2 = new TestWorkbookWindow();
            var targets = new[]
            {
                new WorkbookWindowSelectionTarget(w1, "Book1 - 1", false, "1"),
                new WorkbookWindowSelectionTarget(w2, "Book1 - 3", false, "2")
            };
            var dialog = new UnhideWindowDialog(targets);

            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                PumpDispatcher();

                dialog.Title.Should().Be(UiText.Get("UnhideWindow_Title"));
                var list = WpfTestTree.FindLogicalDescendants<ListBox>(dialog)
                    .Should().ContainSingle().Which;
                AutomationProperties.GetName(list).Should().Be(UiText.Get("UnhideWindow_ListAutomationName"));
                list.Items.Cast<object>().Select(item => item.ToString())
                    .Should().Equal("Book1 - 1", "Book1 - 3");
                list.SelectedItem.Should().BeSameAs(targets[0]);
                WpfTestTree.FindLogicalDescendants<Button>(dialog)
                    .Select(button => button.Content?.ToString())
                    .Should().Contain([UiText.Ok, UiText.Cancel]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void CreateResult_UsesTheSelectedWorkbookWindow()
    {
        var window = new TestWorkbookWindow();
        var target = new WorkbookWindowSelectionTarget(window, "Book1 - 2", false, "1");

        UnhideWindowDialog.CreateResult(target).Should().Be(new UnhideWindowDialogResult(window));
    }

    [Fact]
    public void UnhideWindowDialog_UsesNonEditableSelectionList()
    {
        var source = DialogSourceTestSupport.ReadHostSources("UnhideWindowDialog.cs");

        source.Should().Contain("private readonly ListBox _windowBox");
        source.Should().Contain("_windowBox.SelectedItem");
        source.Should().NotContain("_windowBox.IsEditable = true");
        source.Should().NotContain("_windowBox.Text");
    }

    [Fact]
    public void UnhideWindowDialogOpenedFromKeyboard_FocusesWindowList()
    {
        var source = DialogSourceTestSupport.ReadHostSources("UnhideWindowDialog.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_windowBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_windowBox);");
    }

    [Fact]
    public void UnhideWindowDialog_OkButtonTracksSelectedWindow()
    {
        StaTestRunner.Run(() =>
        {
            var w1 = new TestWorkbookWindow();
            var w2 = new TestWorkbookWindow();
            var targets = new[]
            {
                new WorkbookWindowSelectionTarget(w1, "Book1 - 1", false, "1"),
                new WorkbookWindowSelectionTarget(w2, "Book1 - 3", false, "2")
            };
            var dialog = new UnhideWindowDialog(targets);
            var windowBox = GetField<ListBox>(dialog, "_windowBox");
            var okButton = GetField<Button>(dialog, "_okButton");

            okButton.IsDefault.Should().BeTrue();
            okButton.IsEnabled.Should().BeTrue();

            windowBox.SelectedItem = null;
            okButton.IsEnabled.Should().BeFalse();

            windowBox.SelectedItem = targets[1];
            okButton.IsEnabled.Should().BeTrue();
        });
    }

    [Fact]
    public void UnhideWindowDialogWindowList_DoubleClickAcceptsSelectedWindow()
    {
        StaTestRunner.Run(() =>
        {
            var w1 = new TestWorkbookWindow();
            var w2 = new TestWorkbookWindow();
            var targets = new[]
            {
                new WorkbookWindowSelectionTarget(w1, "Book1 - 1", false, "1"),
                new WorkbookWindowSelectionTarget(w2, "Book1 - 3", false, "2")
            };
            var dialog = new UnhideWindowDialog(targets);
            var windowBox = GetField<ListBox>(dialog, "_windowBox");
            dialog.Dispatcher.BeginInvoke(() =>
            {
                windowBox.SelectedItem = targets[1];
                var doubleClick = CreateMouseDoubleClickEvent();
                windowBox.RaiseEvent(doubleClick);
                doubleClick.Handled.Should().BeTrue();

                dialog.Dispatcher.BeginInvoke(() =>
                {
                    if (dialog.DialogResult is null)
                        dialog.Close();
                }, DispatcherPriority.ContextIdle);
            }, DispatcherPriority.ApplicationIdle);

            dialog.ShowDialog().Should().BeTrue();
            dialog.Result.Should().Be(new UnhideWindowDialogResult(w2));
        });
    }

    [Fact]
    public void UnhideWindowDialogWindowList_DoubleClickWithoutSelectionDoesNotHandleMouseEvent()
    {
        StaTestRunner.Run(() =>
        {
            var target = new WorkbookWindowSelectionTarget(new TestWorkbookWindow(), "Book1 - 1", false, "1");
            var dialog = new UnhideWindowDialog([target]);
            var windowBox = GetField<ListBox>(dialog, "_windowBox");
            windowBox.SelectedItem = null;

            var doubleClick = CreateMouseDoubleClickEvent();

            windowBox.RaiseEvent(doubleClick);

            doubleClick.Handled.Should().BeFalse();
            dialog.DialogResult.Should().BeNull();
        });
    }

    private static T GetField<T>(object instance, string name)
        where T : class
    {
        var field = instance.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(instance).Should().BeOfType<T>().Subject;
    }

    private static MouseButtonEventArgs CreateMouseDoubleClickEvent() =>
        new(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = Control.MouseDoubleClickEvent
        };

    private static void PumpDispatcher() =>
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => { }));
}
