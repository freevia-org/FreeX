using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Themes.Fluent;

using Free.Shared.Ribbon.Avalonia;

namespace Free.Shared.Ribbon.Tests;

public sealed class AvaloniaRibbonComboTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task EditableCombo_SelectionCommitsOnce_AndEnterDoesNotDuplicateIt()
    {
        await Session.Dispatch(() =>
        {
            var executed = new List<string?>();
            var registry = new RibbonCommandRegistry();
            registry.Register("font", new RecordingCommand(executed));
            var content = AvaloniaRibbonRenderer.BuildTabContent(BuildComboTab("font", "Calibri", "Arial"), registry);
            var window = Show(content);
            try
            {
                var combo = FindCombo(content);
                combo.SelectedIndex = 1;
                PressEnter(combo);
                combo.Text = "Consolas";
                PressEnter(combo);

                Assert.Equal(new[] { "Arial", "Consolas" }, executed);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EditableCombo_EscapeStartsNewCommitWindow_AndDoesNotSwallowLaterEnter()
    {
        await Session.Dispatch(() =>
        {
            var executed = new List<string?>();
            var registry = new RibbonCommandRegistry();
            registry.Register("font", new RecordingCommand(executed));
            var content = AvaloniaRibbonRenderer.BuildTabContent(BuildComboTab("font", "Calibri", "Arial"), registry);
            var window = Show(content);
            try
            {
                var combo = FindCombo(content);
                combo.SelectedIndex = 1;
                PressEnter(combo);
                Assert.Equal(new[] { "Arial" }, executed);

                PressKey(combo, Key.Escape);
                PressEnter(combo);

                Assert.Equal(new[] { "Arial", "Arial" }, executed);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EditableCombo_TypedTextCommitsOnEnter()
    {
        await Session.Dispatch(() =>
        {
            var executed = new List<string?>();
            var registry = new RibbonCommandRegistry();
            registry.Register("font", new RecordingCommand(executed));
            var content = AvaloniaRibbonRenderer.BuildTabContent(BuildComboTab("font", "Calibri", "Arial"), registry);
            var window = Show(content);
            try
            {
                var combo = FindCombo(content);
                combo.Text = "Consolas";
                PressEnter(combo);

                Assert.Equal(new[] { "Consolas" }, executed);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EditableCombo_EmptyTextFallsBackToTextLikeWpf()
    {
        await Session.Dispatch(() =>
        {
            var executed = new List<string?>();
            var registry = new RibbonCommandRegistry();
            registry.Register("font", new RecordingCommand(executed));
            var content = AvaloniaRibbonRenderer.BuildTabContent(BuildComboTab("font", "Calibri", "Arial"), registry);
            var window = Show(content);
            try
            {
                var combo = FindCombo(content);
                combo.SelectedIndex = 1;
                executed.Clear();
                combo.Text = string.Empty;
                PressEnter(combo);

                Assert.Equal(new[] { string.Empty }, executed);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EditableCombo_StateRefreshIsSilent_AndEnterCommitsRefreshedValue()
    {
        await Session.Dispatch(() =>
        {
            var command = new RecordingStatefulCommand("Calibri");
            var registry = new RibbonCommandRegistry();
            registry.Register("font", command);
            var content = AvaloniaRibbonRenderer.BuildTabContent(BuildComboTab("font", "Calibri", "Arial"), registry);
            var window = Show(content);
            try
            {
                var combo = FindCombo(content);
                command.State = new RibbonCommandState(Value: "Arial");
                AvaloniaRibbonRenderer.SyncToggleStates(content, registry);
                Assert.Equal("Arial", combo.Text);
                Assert.Empty(command.ExecutedValues);
                PressEnter(combo);

                command.State = new RibbonCommandState(Value: "Consolas");
                AvaloniaRibbonRenderer.SyncToggleStates(content, registry);
                Assert.Equal("Consolas", combo.Text);
                Assert.Equal(new[] { "Arial" }, command.ExecutedValues);
                PressEnter(combo);

                Assert.Equal("Consolas", combo.Text);
                Assert.Equal(new[] { "Arial", "Consolas" }, command.ExecutedValues);
                Assert.Equal(-1, combo.SelectedIndex);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EditableCombo_InitialStateValueOutsideItems_IsDisplayedWithoutExecuting()
    {
        await Session.Dispatch(() =>
        {
            var command = new RecordingStatefulCommand("Consolas");
            var registry = new RibbonCommandRegistry();
            registry.Register("font", command);
            var content = AvaloniaRibbonRenderer.BuildTabContent(BuildComboTab("font", "Calibri", "Arial"), registry);
            var window = Show(content);
            try
            {
                var combo = FindCombo(content);

                Assert.Equal("Consolas", combo.Text);
                Assert.Equal(-1, combo.SelectedIndex);
                Assert.Empty(command.ExecutedValues);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static RibbonTab BuildComboTab(string commandId, params string[] items) =>
        new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", tab => tab.Group("font", "Font", "F", 1, group =>
                group.ComboBox(commandId, "Font", combo => combo with { Items = items })))
            .Build()
            .FindTab("home")!;

    private static ComboBox FindCombo(Control content) =>
        content.GetLogicalDescendants().OfType<ComboBox>().Single();

    private static Window Show(Control content)
    {
        var window = new Window { Width = 420, Height = 160, Content = content };
        window.Show();
        window.Measure(new Size(420, 160));
        window.Arrange(new Rect(0, 0, 420, 160));
        return window;
    }

    private static void PressEnter(ComboBox combo)
        => PressKey(combo, Key.Enter);

    private static void PressKey(ComboBox combo, Key key)
    {
        combo.RaiseEvent(new KeyEventArgs
        {
            Key = key,
            RoutedEvent = InputElement.KeyDownEvent,
        });
    }

    private sealed class RecordingCommand(List<string?> executedValues) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => executedValues.Add(context.SelectedValue);
    }

    private sealed class RecordingStatefulCommand(string initialValue) : IRibbonStatefulCommand
    {
        public RibbonCommandState State { get; set; } = new(Value: initialValue);
        public List<string?> ExecutedValues { get; } = new();

        public void Execute(RibbonCommandContext context) => ExecutedValues.Add(context.SelectedValue);

        public RibbonCommandState GetState() => State;
    }
}
