using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Free.Shared.Ribbon.KeyTips;
using FreeX.App.Services.Ribbon;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class Wave161AvaloniaPivotChartContextMenuKeyTipParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public void WpfAuthority_BuildsPivotChartMenuFromPlannerAndAssignsUniqueKeyTips()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "MainWindow.PivotChartCommands.cs"));

        source.Should().Contain(
            "PivotChartFieldContextMenuPlanner.BuildCommands(BuildPivotChartFieldContextMenuState())");
        source.Should().Contain(
            "MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());");
        source.Should().Contain("private void AddPivotChartFieldContextMenuItem");
    }

    [Fact]
    public Task PivotChartFieldMenu_PresentsWpfAssignedGesturesForEveryPlannerVariant() =>
        Session.Dispatch(() =>
        {
            foreach (var state in ApplicableStates())
            {
                var commands = PivotChartFieldContextMenuPlanner.BuildCommands(state)
                    .Where(command => !command.IsSeparator)
                    .ToArray();
                var items = AvaloniaPivotChartFieldContextMenu.BuildItems(state, _ => { })
                    .OfType<MenuItem>()
                    .ToArray();

                items.Select(item => item.Header).Should().Equal(commands.Select(command => command.Header));
                items.Select(item => item.IsEnabled).Should().Equal(commands.Select(command => command.IsEnabled));
                items.Select(item => Assert.IsType<KeyGesture>(item.InputGesture).Key)
                    .Should()
                    .Equal(AssignWpfKeyTips(commands).Select(ParseKey));
                items.Select(item => Assert.IsType<KeyGesture>(item.InputGesture).Key)
                    .Should()
                    .OnlyHaveUniqueItems();
            }
        }, CancellationToken.None);

    [Fact]
    public Task PivotChartFieldMenu_RoutesAtOpenMenuRootAndHonorsDisabledEscapeAndScope() =>
        Session.Dispatch(() =>
        {
            var actions = new List<PivotChartFieldContextMenuAction>();
            var anchor = new Button();
            var window = new Window { Content = anchor };
            var state = FilteredState();
            var menu = AvaloniaManagedContextMenu.Attach(
                anchor,
                () => AvaloniaPivotChartFieldContextMenu.BuildItems(state, actions.Add));

            window.Show();
            menu.Open(anchor);
            menu.IsOpen.Should().BeTrue();

            var sortAscending = FindItem(menu, PivotChartFieldContextMenuAction.SortAscending);
            var sortKey = Assert.IsType<KeyGesture>(sortAscending.InputGesture).Key;
            sortAscending.Focus().Should().BeTrue();
            var keyDown = RaiseKey(sortAscending, sortKey);

            keyDown.Handled.Should().BeTrue();
            actions.Should().Equal(PivotChartFieldContextMenuAction.SortAscending);
            menu.IsOpen.Should().BeFalse();

            menu.Open(anchor);
            var clear = FindItem(menu, PivotChartFieldContextMenuAction.ClearFilter);
            clear.IsEnabled.Should().BeTrue();

            menu.Close();
            state = NoFilterState();
            menu.Open(anchor);
            var disabledNoFilter = FindItem(menu, PivotChartFieldContextMenuAction.SelectItems);
            disabledNoFilter.IsEnabled.Should().BeFalse();
            var disabledKey = Assert.IsType<KeyGesture>(disabledNoFilter.InputGesture).Key;
            var disabledKeyDown = RaiseKey(menu, disabledKey);

            disabledKeyDown.Handled.Should().BeFalse();
            actions.Should().Equal(PivotChartFieldContextMenuAction.SortAscending);
            menu.IsOpen.Should().BeTrue();

            var escapeItem = menu.Items.OfType<MenuItem>().First(item => item.IsEnabled);
            escapeItem.Focus().Should().BeTrue();
            var escape = RaiseKey(escapeItem, Key.Escape);
            escape.Handled.Should().BeTrue();
            menu.IsOpen.Should().BeFalse();

            var outside = RaiseKey(anchor, sortKey);
            outside.Handled.Should().BeFalse();
            actions.Should().Equal(PivotChartFieldContextMenuAction.SortAscending);

            window.Close();
        }, CancellationToken.None);

    private static IReadOnlyList<PivotChartFieldContextMenuState> ApplicableStates() =>
    [
        FilteredState(),
        NoFilterState(),
        NoFilterState() with { CanValueFieldSettings = true },
    ];

    private static PivotChartFieldContextMenuState FilteredState() => new(
        HasFilterState: true,
        OverallSummary: "Region: Filtered",
        SelectItemsHeader: "Select Items... (1 selected)",
        LabelFilterHeader: "Label Filter... (equals \"East\")",
        ValueFilterHeader: "Value Filter...",
        ClearFilterHeader: "Clear Filters from \"Region\"",
        CanValueFilter: true,
        HasAnyFilter: true,
        CanValueFieldSettings: true);

    private static PivotChartFieldContextMenuState NoFilterState() => new(
        HasFilterState: false,
        OverallSummary: "",
        SelectItemsHeader: "Select Items...",
        LabelFilterHeader: "Label Filter...",
        ValueFilterHeader: "Value Filter...",
        ClearFilterHeader: "Clear Filters from Field",
        CanValueFilter: false,
        HasAnyFilter: false,
        CanValueFieldSettings: false);

    private static MenuItem FindItem(ContextMenu menu, PivotChartFieldContextMenuAction action) =>
        menu.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, action));

    private static KeyEventArgs RaiseKey(Control target, Key key)
    {
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = KeyModifiers.None,
            Source = target,
        };
        target.RaiseEvent(args);
        return args;
    }

    private static IReadOnlyList<string> AssignWpfKeyTips(
        IReadOnlyList<PivotChartFieldContextMenuCommand> commands)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keyTips = new List<string>(commands.Count);
        foreach (var command in commands)
        {
            var keyTip = RibbonKeyTipText.CreateUniqueKeyTip(command.Header, used);
            keyTips.Add(keyTip);
            used.Add(keyTip);
        }

        return keyTips;
    }

    private static Key ParseKey(string keyTip) => KeyGesture.Parse(keyTip).Key;

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeX.slnx", RepoFile);
}
