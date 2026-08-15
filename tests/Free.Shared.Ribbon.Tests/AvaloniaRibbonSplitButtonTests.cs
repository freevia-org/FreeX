using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Ribbon.Avalonia;

namespace Free.Shared.Ribbon.Tests;

public sealed class AvaloniaRibbonSplitButtonTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(RibbonCommandLayoutKind.Large, 80d)]
    [InlineData(RibbonCommandLayoutKind.Medium, 20d)]
    [InlineData(RibbonCommandLayoutKind.Small, 14d)]
    public async Task SplitButton_SeparatesPrimaryAndDropdownActions(
        RibbonCommandLayoutKind layout,
        double dropdownWidth)
    {
        await Session.Dispatch(() =>
        {
            var primaryExecutions = 0;
            var menuExecutions = 0;
            var registry = new RibbonCommandRegistry();
            registry.Register("paste", new RecordingCommand(() => primaryExecutions++));
            registry.Register("pasteSpecial", new RecordingCommand(() => menuExecutions++));

            var content = AvaloniaRibbonRenderer.BuildTabContent(BuildSplitTab(layout), registry);
            var window = Show(content);
            try
            {
                var buttons = content.GetLogicalDescendants().OfType<Button>().ToArray();
                var primary = Assert.Single(buttons, button => Equals(button.Tag, "paste"));
                var dropdown = Assert.Single(buttons, button => Equals(button.Tag, "paste.Dropdown"));
                var flyout = Assert.IsType<MenuFlyout>(dropdown.Flyout);

                Assert.Equal(dropdownWidth, dropdown.Width);
                primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, primary));
                Assert.Equal(1, primaryExecutions);

                var item = Assert.Single(flyout.Items.OfType<MenuItem>());
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
                Assert.Equal(1, menuExecutions);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(RibbonCommandLayoutKind.Large)]
    [InlineData(RibbonCommandLayoutKind.Medium)]
    [InlineData(RibbonCommandLayoutKind.Small)]
    public async Task SplitButton_DropdownRemainsEnabledWhenPrimaryCommandIsUnavailable(RibbonCommandLayoutKind layout)
    {
        await Session.Dispatch(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("pasteSpecial", new RecordingCommand(() => { }));

            var content = AvaloniaRibbonRenderer.BuildTabContent(BuildSplitTab(layout), registry);
            var window = Show(content);
            try
            {
                var dropdown = Assert.Single(
                    content.GetLogicalDescendants().OfType<Button>(),
                    button => Equals(button.Tag, "paste.Dropdown"));
                var primary = Assert.Single(
                    content.GetLogicalDescendants().OfType<Button>(),
                    button => Equals(button.Tag, "paste"));

                Assert.True(dropdown.IsEnabled);
                Assert.False(primary.IsEnabled);
                Assert.IsType<MenuFlyout>(dropdown.Flyout);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CollapsedGroup_AssignsDerivedKeyTipAndRoutesMenuKeyTip()
    {
        await Session.Dispatch(() =>
        {
            var executions = 0;
            var registry = new RibbonCommandRegistry();
            registry.Register("one", new RecordingCommand(() => executions++));
            registry.Register("two", new RecordingCommand(() => { }));

            var definition = new RibbonDefinitionBuilder()
                .Tab("home", "Home", "H", tab => tab.Group("group", "Group", "G", 1, group =>
                {
                    group.Button("one", "One", control => control with
                    {
                        KeyTip = "O",
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                    });
                    group.Button("two", "Two", control => control with
                    {
                        KeyTip = "T",
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                    });
                }))
                .Build();
            var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry);
            var window = Show(ribbon, 180);
            try
            {
                var collapsed = ribbon.GetLogicalDescendants().OfType<Button>()
                    .First(button => button.Classes.Contains("free-ribbon-collapsed-group"));

                Assert.True(AvaloniaRibbonRenderer.TryActivateKeyTip(ribbon, "HGR"));
                Assert.NotNull(collapsed.Flyout);
                Assert.True(collapsed.Flyout!.IsOpen);
                collapsed.Flyout.Hide();
                Assert.True(AvaloniaRibbonRenderer.TryActivateKeyTip(ribbon, "HGRO"));
                Assert.Equal(1, executions);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(RibbonCommandLayoutKind.Medium)]
    [InlineData(RibbonCommandLayoutKind.Small)]
    public async Task SplitButton_KeyTipReachesDropdownMenu(RibbonCommandLayoutKind layout)
    {
        await Session.Dispatch(() =>
        {
            var menuExecutions = 0;
            var registry = new RibbonCommandRegistry();
            registry.Register("paste", new RecordingCommand(() => { }));
            registry.Register("pasteSpecial", new RecordingCommand(() => menuExecutions++));

            var ribbon = AvaloniaRibbonRenderer.BuildRibbon(BuildSplitDefinition(layout), registry);
            var window = Show(ribbon);
            try
            {
                Assert.True(AvaloniaRibbonRenderer.TryActivateKeyTip(ribbon, "HPS"));
                Assert.Equal(1, menuExecutions);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CollapsedGroup_ComboBoxProjectionMatchesWpfEnablementAndExecutes()
    {
        await Session.Dispatch(() =>
        {
            var executions = 0;
            var registry = new RibbonCommandRegistry();
            registry.Register("font", new RecordingCommand(() => executions++));

            var content = AvaloniaRibbonRenderer.BuildTabContent(
                new RibbonDefinitionBuilder()
                    .Tab("home", "Home", "H", tab => tab.Group("font", "Font", "F", 1, group =>
                        group.ComboBox("font", "Font", combo => combo with { Items = new[] { "Arial" } })))
                    .Build()
                    .FindTab("home")!,
                registry);
            var window = Show(content, 90);
            try
            {
                var collapsed = content.GetLogicalDescendants().OfType<Button>()
                    .Single(button => button.Classes.Contains("free-ribbon-collapsed-group"));
                var flyout = Assert.IsType<MenuFlyout>(collapsed.Flyout);
                var projection = Assert.Single(flyout.Items.OfType<MenuItem>());

                Assert.Equal("Font", projection.Header);
                Assert.True(projection.IsEnabled);
                projection.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, projection));
                Assert.Equal(1, executions);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CollapsedGroup_ToggleProjectionRefreshesCheckedAndEnabledStateWhenOpened()
    {
        await Session.Dispatch(() =>
        {
            var command = new MutableStatefulCommand(
                new RibbonCommandState(IsEnabled: true, IsChecked: false));
            var registry = new RibbonCommandRegistry();
            registry.Register("toggle", command);
            var content = AvaloniaRibbonRenderer.BuildTabContent(
                new RibbonDefinitionBuilder()
                    .Tab("home", "Home", "H", tab => tab.Group("group", "Group", "G", 1, group =>
                        group.Toggle("toggle", "Toggle")))
                    .Build()
                    .FindTab("home")!,
                registry);
            var window = Show(content, 90);
            try
            {
                var collapsed = content.GetLogicalDescendants().OfType<Button>()
                    .Single(button => button.Classes.Contains("free-ribbon-collapsed-group"));
                var flyout = Assert.IsType<MenuFlyout>(collapsed.Flyout);
                var projection = Assert.Single(flyout.Items.OfType<MenuItem>());
                Assert.Equal(MenuItemToggleType.CheckBox, projection.ToggleType);
                Assert.False(projection.IsChecked);
                Assert.True(projection.IsEnabled);

                command.State = new RibbonCommandState(IsEnabled: false, IsChecked: true);
                flyout.ShowAt(collapsed);

                Assert.Equal(MenuItemToggleType.CheckBox, projection.ToggleType);
                Assert.True(projection.IsChecked);
                Assert.False(projection.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CollapsedGroupPopup_FocusesEnabledItemsTraversesAndRestoresAnchorOnEscape()
    {
        await Session.Dispatch(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("one", new RecordingCommand(() => { }));
            registry.Register("two", new RecordingCommand(() => { }));
            var ribbon = AvaloniaRibbonRenderer.BuildRibbon(
                new RibbonDefinitionBuilder()
                    .Tab("home", "Home", "H", tab => tab.Group("group", "Group", "G", 1, group => group
                        .Button("disabled", "Disabled")
                        .Button("one", "One")
                        .Button("two", "Two")))
                    .Build(),
                registry);
            var window = Show(ribbon, 90);
            try
            {
                var collapsed = ribbon.GetLogicalDescendants().OfType<Button>()
                    .First(button => button.Classes.Contains("free-ribbon-collapsed-group") &&
                                     Equals(button.Tag, "collapsed:group"));
                var flyout = Assert.IsType<MenuFlyout>(collapsed.Flyout);
                var items = flyout.Items.OfType<MenuItem>().ToArray();

                Assert.Equal(PlacementMode.Bottom, flyout.Placement);
                Assert.Equal(
                    PopupPositionerConstraintAdjustment.SlideX | PopupPositionerConstraintAdjustment.FlipY,
                    flyout.PlacementConstraintAdjustment);
                Assert.NotNull(Application.Current);
                Assert.Contains(
                    Application.Current!.Styles.OfType<Style>(),
                    style => style.Setters.OfType<Setter>().Any(setter =>
                        setter.Property == TemplatedControl.MinWidthProperty &&
                        Equals(setter.Value, RibbonVisualMetrics.PopupChrome.MinWidth)));
                Assert.Contains("free-ribbon-popup-chrome", flyout.FlyoutPresenterClasses);
                Assert.False(items[0].IsEnabled);
                Assert.Equal(RibbonVisualMetrics.PopupChrome.ItemMinHeight, items[0].MinHeight);
                Assert.Equal(new Thickness(10, 5, 10, 5), items[0].Padding);

                flyout.ShowAt(collapsed);
                Assert.True(flyout.IsOpen);
                Assert.True(items[1].IsFocused);

                RaiseKey(items[1], Key.Down);
                Assert.True(items[2].IsFocused);
                RaiseKey(items[2], Key.Up);
                Assert.True(items[1].IsFocused);

                RaiseKey(items[1], Key.Escape);
                Assert.False(flyout.IsOpen);
                Assert.True(collapsed.IsFocused);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CollapsedGroup_OmitsSeparatorsAndRowBreaksFromOverflowMenu()
    {
        await Session.Dispatch(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("one", new RecordingCommand(() => { }));
            registry.Register("two", new RecordingCommand(() => { }));
            var content = AvaloniaRibbonRenderer.BuildTabContent(
                new RibbonDefinitionBuilder()
                    .Tab("home", "Home", "H", tab => tab
                        .Group("group", "Group", "G", 1, group => group
                            .Button("one", "One")
                            .Separator()
                            .RowBreak()
                            .Button("two", "Two")))
                    .Build()
                    .FindTab("home")!,
                registry);
            var window = Show(content, 90);
            try
            {
                var collapsed = content.GetLogicalDescendants().OfType<Button>()
                    .Single(button => button.Classes.Contains("free-ribbon-collapsed-group"));
                var flyout = Assert.IsType<MenuFlyout>(collapsed.Flyout);
                var items = flyout.Items.OfType<MenuItem>().ToArray();

                Assert.Equal(new[] { "One", "Two" }, items.Select(item => item.Header));
                Assert.DoesNotContain(flyout.Items, item => item is Separator);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CollapsedGroupPopup_NestedMenuUsesSharedChromeAndLeftRestoresParentFocus()
    {
        await Session.Dispatch(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("child", new RecordingCommand(() => { }));
            var content = AvaloniaRibbonRenderer.BuildRibbon(
                new RibbonDefinitionBuilder()
                    .Tab("home", "Home", "H", tab => tab.Group("group", "Group", "G", 1, group =>
                        group.Dropdown("more", "More", new RibbonMenu(new[]
                        {
                            new RibbonMenuItem("More", Children: new[]
                            {
                                new RibbonMenuItem("Child", "child")
                            }),
                        }))))
                    .Build(),
                registry);
            var window = Show(content, 90);
            try
            {
                var collapsed = content.GetLogicalDescendants().OfType<Button>()
                    .First(button => button.Classes.Contains("free-ribbon-collapsed-group") &&
                                     Equals(button.Tag, "collapsed:group"));
                var flyout = Assert.IsType<MenuFlyout>(collapsed.Flyout);
                var parent = Assert.Single(flyout.Items.OfType<MenuItem>());
                var child = Assert.Single(parent.Items.OfType<MenuItem>());

                Assert.Equal(RibbonVisualMetrics.PopupChrome.ItemMinHeight, parent.MinHeight);
                Assert.Equal(RibbonVisualMetrics.PopupChrome.Submenu.ItemMinHeight, child.MinHeight);
                Assert.Equal(new Thickness(10, 5, 10, 5), child.Padding);

                flyout.ShowAt(collapsed);
                parent.IsSubMenuOpen = true;
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                var submenuPopup = Assert.Single(parent.GetVisualDescendants().OfType<Popup>());
                Assert.Equal(PlacementMode.Right, submenuPopup.Placement);
                Assert.Equal(
                    PopupPositionerConstraintAdjustment.FlipX | PopupPositionerConstraintAdjustment.SlideY,
                    submenuPopup.PlacementConstraintAdjustment);
                Assert.Equal(RibbonVisualMetrics.PopupChrome.Submenu.AnchorGap, submenuPopup.HorizontalOffset);
                child.Focus(NavigationMethod.Directional);
                RaiseKey(child, Key.Left);

                Assert.False(parent.IsSubMenuOpen);
                Assert.True(parent.IsSelected);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DropdownPopup_NestedMenuUsesSharedChromeAndRightLeftNavigation()
    {
        await Session.Dispatch(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("child", new RecordingCommand(() => { }));
            var content = AvaloniaRibbonRenderer.BuildTabContent(
                new RibbonDefinitionBuilder()
                    .Tab("home", "Home", "H", tab => tab.Group("group", "Group", "G", 1, group =>
                        group.Dropdown("more", "More", new RibbonMenu(new[]
                        {
                            new RibbonMenuItem("More", Children: new[]
                            {
                                new RibbonMenuItem("Child", "child")
                            }),
                        }))))
                    .Build()
                    .FindTab("home")!,
                registry);
            var window = Show(content);
            try
            {
                var dropdown = content.GetLogicalDescendants().OfType<Button>()
                    .Single(button => Equals(button.Tag, "more"));
                var flyout = Assert.IsType<MenuFlyout>(dropdown.Flyout);
                var parent = Assert.Single(flyout.Items.OfType<MenuItem>());
                var child = Assert.Single(parent.Items.OfType<MenuItem>());

                Assert.Equal(PlacementMode.Bottom, flyout.Placement);
                Assert.Equal(RibbonVisualMetrics.PopupChrome.ItemMinHeight, parent.MinHeight);
                Assert.Equal(RibbonVisualMetrics.PopupChrome.Submenu.ItemMinHeight, child.MinHeight);
                Assert.Contains("free-ribbon-popup-chrome", flyout.FlyoutPresenterClasses);

                flyout.ShowAt(dropdown);
                parent.Focus();
                RaiseKey(parent, Key.Right);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                Assert.True(parent.IsSubMenuOpen);
                Assert.True(child.IsFocused);
                var submenuPopup = Assert.Single(parent.GetVisualDescendants().OfType<Popup>());
                Assert.Equal(PlacementMode.Right, submenuPopup.Placement);
                Assert.Equal(
                    PopupPositionerConstraintAdjustment.FlipX | PopupPositionerConstraintAdjustment.SlideY,
                    submenuPopup.PlacementConstraintAdjustment);
                Assert.Equal(RibbonVisualMetrics.PopupChrome.Submenu.AnchorGap, submenuPopup.HorizontalOffset);

                RaiseKey(child, Key.Left);
                Assert.False(parent.IsSubMenuOpen);
                Assert.True(parent.IsFocused || parent.IsSelected);

                parent.IsSubMenuOpen = true;
                child.Focus(NavigationMethod.Directional);
                RaiseKey(child, Key.Escape);
                Assert.False(parent.IsSubMenuOpen);
                Assert.True(parent.IsFocused || parent.IsSelected);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static RibbonTab BuildSplitTab(RibbonCommandLayoutKind layout) =>
        BuildSplitDefinition(layout)
            .FindTab("home")!;

    private static RibbonDefinition BuildSplitDefinition(RibbonCommandLayoutKind layout) =>
        new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", tab => tab.Group("group", "Group", "G", 1, group =>
                group.SplitButton("paste", "Paste", new RibbonMenu(new[]
                {
                    new RibbonMenuItem("Paste Special", "pasteSpecial", "S")
                }), control => control with
                {
                    PreferredLayout = layout,
                    KeyTip = "P"
                })))
            .Build();

    private static Window Show(Control content, double width = 420)
    {
        var window = new Window { Width = width, Height = 160, Content = content };
        window.Show();
        window.Measure(new Size(width, 160));
        window.Arrange(new Rect(0, 0, width, 160));
        return window;
    }

    private static void RaiseKey(Control target, Key key) =>
        target.RaiseEvent(new KeyEventArgs
        {
            Key = key,
            RoutedEvent = InputElement.KeyDownEvent,
        });

    private sealed class RecordingCommand(Action action) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => action();
    }

    private sealed class MutableStatefulCommand(RibbonCommandState initialState) : IRibbonStatefulCommand
    {
        public RibbonCommandState State { get; set; } = initialState;

        public void Execute(RibbonCommandContext context)
        {
        }

        public RibbonCommandState GetState() => State;
    }
}
