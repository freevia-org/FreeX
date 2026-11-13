using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class OwnedDialogKeyboardTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private sealed class Rig
    {
        public Window Owner = null!;
        public TextBox OwnerBox = null!;
        public Window Dialog = null!;
        public TextBox First = null!;
        public TextBox Second = null!;
        public CheckBox Third = null!;
        public Button Reset = null!;
        public Button Ok = null!;
        public Button Cancel = null!;
        public ListBox Gallery = null!;
    }

    private static Rig Open(bool withCancel, bool galleryFirst = false)
    {
        OwnedDialogKeyboard.Install();
        var rig = new Rig { OwnerBox = new TextBox() };
        rig.Owner = new Window { Content = rig.OwnerBox, Width = 400, Height = 300 };
        rig.Owner.Show();
        rig.OwnerBox.Focus();

        rig.First = new TextBox();
        rig.Second = new TextBox();
        rig.Third = new CheckBox { Content = "c" };
        rig.Reset = new Button { Content = "Reset" };
        rig.Gallery = new ListBox { ItemsSource = new[] { "a", "b", "c" }, SelectedIndex = 0 };
        var page1Children = new StackPanel();
        if (galleryFirst)
            page1Children.Children.Add(rig.Gallery);
        page1Children.Children.Add(rig.First);
        page1Children.Children.Add(rig.Second);
        page1Children.Children.Add(rig.Third);
        page1Children.Children.Add(rig.Reset);
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "One", Content = page1Children });
        tabs.Items.Add(new TabItem { Header = "Two", Content = new TextBox() });
        rig.Ok = new Button { Content = "OK", IsDefault = true };
        rig.Cancel = new Button { Content = "Cancel", IsCancel = withCancel };
        var footer = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal };
        footer.Children.Add(rig.Ok);
        footer.Children.Add(rig.Cancel);
        var root = new StackPanel();
        root.Children.Add(tabs);
        root.Children.Add(footer);
        rig.Dialog = new Window { Content = root, Width = 400, Height = 300 };
        rig.Dialog.Show(rig.Owner);
        Dispatcher.UIThread.RunJobs();
        return rig;
    }

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public async Task Tab_cycles_through_the_page_and_buttons_and_wraps_in_both_directions()
    {
        await Session.Dispatch(() =>
        {
            var rig = Open(withCancel: true);
            rig.First.Focus();
            Dispatcher.UIThread.RunJobs();

            var forward = new List<object?>();
            for (var step = 0; step < 8; step++)
            {
                Press(rig.Dialog, Key.Tab);
                forward.Add(rig.Dialog.FocusManager!.GetFocusedElement());
            }

            // Second, Third, Reset, OK, Cancel, then the wrap lands on the first field again.
            forward.Take(5).Should().Equal(rig.Second, rig.Third, rig.Reset, rig.Ok, rig.Cancel);
            forward[5].Should().BeSameAs(rig.First);
            forward[6].Should().BeSameAs(rig.Second);

            rig.First.Focus();
            Press(rig.Dialog, Key.Tab, RawInputModifiers.Shift);
            rig.Dialog.FocusManager!.GetFocusedElement().Should().BeSameAs(rig.Cancel);
            Press(rig.Dialog, Key.Tab, RawInputModifiers.Shift);
            rig.Dialog.FocusManager!.GetFocusedElement().Should().BeSameAs(rig.Ok);

            rig.Dialog.Close();
            rig.Owner.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Shift_Tab_from_a_first_list_stop_wraps_to_the_last_button()
    {
        await Session.Dispatch(() =>
        {
            var rig = Open(withCancel: true, galleryFirst: true);
            rig.Gallery.Focus();
            Dispatcher.UIThread.RunJobs();

            Press(rig.Dialog, Key.Tab, RawInputModifiers.Shift);

            rig.Dialog.FocusManager!.GetFocusedElement().Should().BeSameAs(rig.Cancel);

            rig.Dialog.Close();
            rig.Owner.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Tab_from_the_last_button_wraps_to_a_first_list_stop()
    {
        await Session.Dispatch(() =>
        {
            var rig = Open(withCancel: true, galleryFirst: true);
            rig.Cancel.Focus();
            Dispatcher.UIThread.RunJobs();

            Press(rig.Dialog, Key.Tab);

            var focused = rig.Dialog.FocusManager!.GetFocusedElement() as Visual;
            (ReferenceEquals(focused, rig.Gallery) || rig.Gallery.IsVisualAncestorOf(focused)).Should().BeTrue("focused " + focused?.GetType().Name + " stops=" + string.Join(",", OwnedDialogKeyboard.GetTabStops(rig.Dialog).Select(c => c.GetType().Name)));

            rig.Dialog.Close();
            rig.Owner.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Tab_from_the_last_button_wraps_to_a_first_editable_combo_box()
    {
        await Session.Dispatch(() =>
        {
            OwnedDialogKeyboard.Install();
            var owner = new Window { Width = 400, Height = 300 };
            owner.Show();
            var combo = new ComboBox { IsEditable = true, ItemsSource = new[] { "a", "b" }, SelectedIndex = 0 };
            var caption = new TextBox();
            var ok = new Button { Content = "OK" };
            var cancel = new Button { Content = "Cancel" };
            var panel = new StackPanel();
            panel.Children.Add(combo);
            panel.Children.Add(caption);
            panel.Children.Add(ok);
            panel.Children.Add(cancel);
            var dialog = new Window { Content = panel, Width = 400, Height = 300 };
            dialog.Show(owner);
            Dispatcher.UIThread.RunJobs();
            cancel.Focus();
            Dispatcher.UIThread.RunJobs();

            Press(dialog, Key.Tab);

            var focused = dialog.FocusManager!.GetFocusedElement() as Visual;
            (ReferenceEquals(focused, combo) || combo.IsVisualAncestorOf(focused)).Should().BeTrue(
                "focused " + focused?.GetType().Name + " stops=" + string.Join(",", OwnedDialogKeyboard.GetTabStops(dialog).Select(c => c.GetType().Name)));

            dialog.Close();
            owner.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Escape_closes_a_dialog_without_a_cancel_button_and_returns_focus_to_the_owner()
    {
        await Session.Dispatch(() =>
        {
            var rig = Open(withCancel: false);
            rig.Dialog.IsVisible.Should().BeTrue();

            Press(rig.Dialog, Key.Escape);
            Dispatcher.UIThread.RunJobs();

            rig.Dialog.IsVisible.Should().BeFalse();
            rig.Owner.FocusManager!.GetFocusedElement().Should().BeSameAs(rig.OwnerBox);

            rig.Owner.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Escape_clicks_the_cancel_button()
    {
        await Session.Dispatch(() =>
        {
            var rig = Open(withCancel: true);
            var cancelled = false;
            rig.Cancel.Click += (_, _) => cancelled = true;

            Press(rig.Dialog, Key.Escape);

            cancelled.Should().BeTrue();
            rig.Dialog.IsVisible.Should().BeFalse();
            rig.Owner.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task A_dialog_that_focuses_nothing_gets_its_first_control_focused()
    {
        await Session.Dispatch(() =>
        {
            var rig = Open(withCancel: true);
            for (var pass = 0; pass < 4; pass++)
            {
                rig.Dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            rig.Dialog.FocusManager!.GetFocusedElement().Should().BeSameAs(rig.First);

            rig.Dialog.Close();
            rig.Owner.Close();
            return true;
        }, CancellationToken.None);
    }
}
