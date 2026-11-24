using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Ribbon.Avalonia;

namespace Free.Shared.Ribbon.Tests;

public sealed class AvaloniaRibbonComboTypingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData("24")]
    [InlineData("80")]
    public Task FocusedTypingDoesNotCommitAnIntermediateValueOrMoveFocus(string value) =>
        Session.Dispatch(() =>
        {
            var command = new RecordingCommand();
            var registry = new RibbonCommandRegistry();
            registry.Register("size", command);
            var document = new TextBox();
            var content = AvaloniaRibbonRenderer.BuildTabContent(BuildTab(), registry,
                afterExecute: () => document.Focus());
            var window = Show(new StackPanel { Children = { content, document } });
            try
            {
                var combo = content.GetLogicalDescendants().OfType<ComboBox>().Single();
                var input = combo.GetVisualDescendants().OfType<TextBox>().Single();
                input.Focus().Should().BeTrue();
                input.SelectAll();

                foreach (var character in value)
                {
                    window.KeyTextInput(character.ToString());
                    command.Values.Should().BeEmpty("typing is not a catalog selection or a commit");
                    input.IsFocused.Should().BeTrue();
                }

                combo.Text.Should().Be(value);
                PressEnter(window);
                command.Values.Should().Equal(value);
                document.IsFocused.Should().BeTrue();
                document.Text.Should().BeNullOrEmpty("no remaining digit or Enter belongs to the document");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

    [Theory]
    [InlineData("24")]
    [InlineData("10,5")]
    [InlineData("")]
    [InlineData("invalid")]
    public Task GenuineFocusExitCommitsTheEntireTypedValueOnce(string value) =>
        WithCombo((window, combo, input, document, command) =>
        {
            input.Focus();
            input.SelectAll();
            if (value.Length == 0)
                window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            foreach (var character in value)
                window.KeyTextInput(character.ToString());
            command.Values.Should().BeEmpty();

            document.Focus();
            command.Values.Should().Equal(value);
            document.Text.Should().BeNullOrEmpty();
        });

    [Fact]
    public Task EnterThenFocusExitDoesNotCommitTheTypedValueTwice() =>
        WithCombo((window, combo, input, document, command) =>
        {
            input.Focus();
            input.SelectAll();
            window.KeyTextInput("24");
            PressEnter(window);
            command.Values.Should().Equal("24");
            document.Focus();
            command.Values.Should().Equal("24");
        }, moveFocusAfterExecute: false);

    [Theory]
    [InlineData(3, "24")]
    [InlineData(2, "12")]
    public Task ExplicitDropdownPickCommitsEvenWhenTypingAlreadyMatchedTheItem(int index, string expected) =>
        WithCombo((window, combo, input, document, command) =>
        {
            input.Focus();
            input.SelectAll();
            window.KeyTextInput("24");
            command.Values.Should().BeEmpty();
            combo.SelectedIndex.Should().Be(3);
            combo.IsDropDownOpen = true;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            command.Values.Should().BeEmpty("opening the catalog is not confirmation");

            var item = combo.ContainerFromIndex(index).Should().BeAssignableTo<Control>().Subject;
            var popup = TopLevel.GetTopLevel(item)!;
            var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), popup)!.Value;
            popup.MouseDown(point, MouseButton.Left);
            popup.MouseUp(point, MouseButton.Left);

            command.Values.Should().Equal(expected);
            combo.IsDropDownOpen.Should().BeFalse();
            document.IsFocused.Should().BeTrue();
        });

    [Fact]
    public Task EscapeClosesThePopupWithoutCommittingAndALaterEnterStillCommits() =>
        WithCombo((window, combo, input, document, command) =>
        {
            input.Focus();
            input.SelectAll();
            window.KeyTextInput("24");
            combo.IsDropDownOpen = true;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var item = combo.ContainerFromIndex(combo.SelectedIndex)!;
            var popup = TopLevel.GetTopLevel(item)!;
            popup.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            command.Values.Should().BeEmpty();
            combo.IsDropDownOpen.Should().BeFalse();

            PressEnter(window);
            command.Values.Should().Equal("24");
        });

    [Fact]
    public Task UnconsumedKeyboardCatalogNavigationStillCommitsImmediately() =>
        WithCombo((window, combo, input, document, command) =>
        {
            input.Focus();
            // Exercise ComboBox's catalog navigation when the framework routes an unconsumed
            // key to it. The separate stock-control test covers raw input in its inner TextBox.
            var key = new KeyEventArgs { Key = Key.Down, RoutedEvent = InputElement.KeyDownEvent };
            combo.RaiseEvent(key);
            key.Handled.Should().BeTrue();
            command.Values.Should().Equal("12");
            document.IsFocused.Should().BeTrue();
        });

    [Fact]
    public Task AcceptedDirectionalFocusSelectionKeepsTheFrameworkCatalogCommit() =>
        WithCombo((window, combo, input, document, command) =>
        {
            input.Focus();
            combo.IsDropDownOpen = true;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var item = combo.ContainerFromIndex(2)!;
            var focus = new FocusChangedEventArgs(InputElement.GotFocusEvent)
            {
                NavigationMethod = NavigationMethod.Directional,
                NewFocusedElement = item,
            };

            combo.UpdateSelectionFromEvent(item, focus).Should().BeTrue();
            command.Values.Should().Equal("12");
            document.IsFocused.Should().BeTrue();
        });

    [Fact]
    public Task FocusedClosedDownMatchesTheFrameworkEditableComboBehavior() => Session.Dispatch(() =>
    {
        int Navigate(ComboBox combo, Control content)
        {
            var window = Show(content);
            try
            {
                combo.GetVisualDescendants().OfType<TextBox>().Single().Focus();
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                return combo.SelectedIndex;
            }
            finally
            {
                window.Close();
            }
        }

        var frameworkCombo = new ComboBox { IsEditable = true, ItemsSource = new[] { "8", "11", "12", "24" }, SelectedIndex = 1 };
        var frameworkIndex = Navigate(frameworkCombo, frameworkCombo);
        var command = new RecordingCommand();
        var registry = new RibbonCommandRegistry();
        registry.Register("size", command);
        var content = AvaloniaRibbonRenderer.BuildTabContent(BuildTab(), registry);
        var combo = content.GetLogicalDescendants().OfType<ComboBox>().Single();
        Navigate(combo, content).Should().Be(frameworkIndex);
    }, CancellationToken.None);

    [Fact]
    public Task EnterWithSynchronousStateRefreshThenFocusExitCommitsOnce() => Session.Dispatch(() =>
    {
        var command = new RecordingCommand { UpdateStateOnExecute = true };
        var registry = new RibbonCommandRegistry();
        registry.Register("size", command);
        var document = new TextBox();
        Control? content = null;
        content = AvaloniaRibbonRenderer.BuildTabContent(BuildTab(), registry,
            afterExecute: () => AvaloniaRibbonRenderer.SyncToggleStates(content!, registry));
        var window = Show(new StackPanel { Children = { content, document } });
        try
        {
            var input = content.GetLogicalDescendants().OfType<ComboBox>().Single()
                .GetVisualDescendants().OfType<TextBox>().Single();
            input.Focus();
            input.SelectAll();
            window.KeyTextInput("24");
            PressEnter(window);
            input.IsFocused.Should().BeTrue();
            command.Values.Should().Equal("24");
            document.Focus();
            command.Values.Should().Equal("24");
        }
        finally
        {
            window.Close();
        }
    }, CancellationToken.None);

    private static Task WithCombo(Action<Window, ComboBox, TextBox, TextBox, RecordingCommand> action,
        bool moveFocusAfterExecute = true) => Session.Dispatch(() =>
    {
        var command = new RecordingCommand();
        var registry = new RibbonCommandRegistry();
        registry.Register("size", command);
        var document = new TextBox();
        var content = AvaloniaRibbonRenderer.BuildTabContent(BuildTab(), registry,
            afterExecute: moveFocusAfterExecute ? () => document.Focus() : null);
        var window = Show(new StackPanel { Children = { content, document } });
        try
        {
            var combo = content.GetLogicalDescendants().OfType<ComboBox>().Single();
            var input = combo.GetVisualDescendants().OfType<TextBox>().Single();
            action(window, combo, input, document, command);
        }
        finally
        {
            window.Close();
        }
    }, CancellationToken.None);

    private static RibbonTab BuildTab() => new RibbonDefinitionBuilder()
        .Tab("home", "Home", "H", tab => tab.Group("font", "Font", "F", 1, group =>
            group.ComboBox("size", "Size", combo => combo with { Items = ["8", "11", "12", "24"] })))
        .Build().FindTab("home")!;

    private static Window Show(Control content)
    {
        var window = new Window { Width = 420, Height = 220, Content = content };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static void PressEnter(Window window) =>
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");

    private sealed class RecordingCommand : IRibbonStatefulCommand
    {
        public List<string?> Values { get; } = [];
        public bool UpdateStateOnExecute { get; init; }
        private string? _value = "11";
        public void Execute(RibbonCommandContext context)
        {
            Values.Add(context.SelectedValue);
            if (UpdateStateOnExecute)
                _value = context.SelectedValue;
        }
        public RibbonCommandState GetState() => new(Value: _value);
    }
}
