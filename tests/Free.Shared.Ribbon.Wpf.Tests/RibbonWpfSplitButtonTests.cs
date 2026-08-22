using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Wpf;

namespace Free.Shared.Ribbon.Wpf.Tests;

[Trait("Category", "RibbonUiLane")]
public sealed class RibbonWpfSplitButtonTests
{
    [Fact]
    public void ExpandedSplitButton_SeparatesPrimaryAndDropdownActions()
    {
        var primary = new RecordingCommand();
        var menuAction = new RecordingCommand();

        StaTestRunner.Run(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("paste", primary);
            registry.Register("pasteSpecial", menuAction);
            var root = BuildRibbon(registry);
            Layout(root, 420, 130);

            var primaryButton = FindButton(root, "paste");
            var dropdownButton = FindButton(root, "paste.Dropdown");

            primaryButton.ContextMenu.Should().BeNull();
            dropdownButton.ContextMenu.Should().NotBeNull();
            primaryButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, primaryButton));
            primary.Invocations.Should().Be(1);

            dropdownButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, dropdownButton));
            dropdownButton.ContextMenu!.IsOpen.Should().BeTrue();
            var item = dropdownButton.ContextMenu.Items.OfType<MenuItem>()
                .Single(menuItem => Equals(menuItem.Header, "Paste Special"));
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
            menuAction.Invocations.Should().Be(1);
            root.Should().NotBeNull();
        });
    }

    [Theory]
    [InlineData(RibbonCommandLayoutKind.Large, 80d, 20d)]
    [InlineData(RibbonCommandLayoutKind.Medium, 20d, 22d)]
    [InlineData(RibbonCommandLayoutKind.Small, 14d, 22d)]
    public void SplitButton_UsesFixedDropdownZoneMetrics(
        RibbonCommandLayoutKind layout,
        double dropdownWidth,
        double dropdownHeight)
    {
        StaTestRunner.Run(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("paste", new RecordingCommand());
            var root = BuildRibbon(registry, layout: layout);
            Layout(root, 420, 130);

            var dropdown = FindButton(root, "paste.Dropdown");

            dropdown.Width.Should().Be(dropdownWidth);
            dropdown.Height.Should().Be(dropdownHeight);
            dropdown.ContextMenu.Should().NotBeNull();
        });
    }

    [Theory]
    [InlineData(RibbonCommandLayoutKind.Large)]
    [InlineData(RibbonCommandLayoutKind.Medium)]
    [InlineData(RibbonCommandLayoutKind.Small)]
    public void SplitButton_DropdownRemainsEnabledWhenPrimaryCommandIsUnavailable(RibbonCommandLayoutKind layout)
    {
        StaTestRunner.Run(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("pasteSpecial", new RecordingCommand());
            var root = BuildRibbon(registry, layout: layout);
            Layout(root, 420, 130);

            var dropdown = FindButton(root, "paste.Dropdown");
            var primary = FindButton(root, "paste");

            dropdown.IsEnabled.Should().BeTrue();
            primary.IsEnabled.Should().BeFalse();
            dropdown.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, dropdown));
            dropdown.ContextMenu!.IsOpen.Should().BeTrue();
        });
    }

    [Fact]
    public void CollapsedGroup_AssignsDerivedKeyTipToSplitOverflowButton()
    {
        StaTestRunner.Run(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("paste", new RecordingCommand());
            var root = BuildRibbon(registry);
            var group = Descendants(root).OfType<RibbonGroupHost>().Single();
            Layout(root, 420, 130);
            group.Collapsed = true;

            var collapsedGrid = Assert.IsType<Grid>(group.Content);
            var button = collapsedGrid.Children.OfType<Button>().Single();

            RibbonTooltip.GetKeyTip(button).Should().Be("CL");
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            button.ContextMenu!.IsOpen.Should().BeTrue();
        });
    }

    [Fact]
    public void CollapsedGroup_UsesFixedSingleLineEllipsisCaption()
    {
        StaTestRunner.Run(() =>
        {
            const string groupHeader = "Very Long Caption Group";
            var registry = new RibbonCommandRegistry();
            registry.Register("paste", new RecordingCommand());
            var root = BuildRibbon(registry, groupHeader: groupHeader);
            var group = Descendants(root).OfType<RibbonGroupHost>().Single();

            // Materialize the normal presentation before forcing the collapsed state. The panel is the
            // adaptive-state owner, so laying out the root again would select the expanded state at this
            // wide test viewport; arrange the forced host itself instead.
            Layout(root, 420, 130);
            group.Collapsed = true;
            group.Measure(new Size(RibbonGroupHost.CollapsedWidth, 130));
            group.Arrange(new Rect(0, 0, RibbonGroupHost.CollapsedWidth, 130));

            var collapsedGrid = Assert.IsType<Grid>(group.Content);
            var button = collapsedGrid.Children.OfType<Button>().Single();
            var content = Assert.IsType<StackPanel>(button.Content);
            var caption = content.Children.OfType<TextBlock>().Single(text => text.Text == groupHeader);

            collapsedGrid.Width.Should().Be(RibbonGroupHost.CollapsedWidth);
            button.Width.Should().Be(58);
            caption.Width.Should().Be(58);
            caption.TextWrapping.Should().Be(TextWrapping.NoWrap);
            caption.TextTrimming.Should().Be(TextTrimming.CharacterEllipsis);
            RibbonTooltip.GetTitle(button).Should().Be(groupHeader);
        });
    }

    [Fact]
    public void CollapsedGroupPopup_UsesPlacementAndEscapeDismissalContract()
    {
        StaTestRunner.Run(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("one", new RecordingCommand());
            registry.Register("two", new RecordingCommand());
            var root = RibbonWpfRenderer.BuildTabContent(
                new RibbonDefinitionBuilder()
                    .Tab("home", "Home", "H", tab => tab.Group("group", "Group", "G", 1, group => group
                        .Button("disabled", "Disabled")
                        .Button("one", "One")
                        .Button("two", "Two")))
                    .Build()
                    .FindTab("home")!,
                new Border(),
                registry);
            var group = Descendants(root).OfType<RibbonGroupHost>().Single();
            var window = new Window { Content = root, Width = 420, Height = 130 };
            window.Show();
            try
            {
                Layout(root, 420, 130);
                group.Collapsed = true;

                var button = Assert.IsType<Button>(Assert.IsType<Grid>(group.Content).Children[0]);
                var menu = Assert.IsType<ContextMenu>(button.ContextMenu);
                var items = menu.Items.OfType<MenuItem>().ToArray();

                menu.PlacementTarget.Should().BeSameAs(button);
                menu.Placement.Should().Be(PlacementMode.Custom);
                menu.CustomPopupPlacementCallback.Should().NotBeNull();
                menu.StaysOpen.Should().BeFalse();
                menu.MinWidth.Should().Be(RibbonVisualMetrics.PopupChrome.MinWidth);
                menu.MaxWidth.Should().Be(RibbonVisualMetrics.PopupChrome.MaxWidth);
                menu.Padding.Should().Be(new Thickness(4));
                menu.BorderThickness.Should().Be(new Thickness(1));
                menu.Background.Should().NotBeNull();
                menu.BorderBrush.Should().NotBeNull();
                menu.Effect.Should().BeOfType<System.Windows.Media.Effects.DropShadowEffect>();
                items[0].MinHeight.Should().Be(RibbonVisualMetrics.PopupChrome.ItemMinHeight);
                items[0].Padding.Should().Be(new Thickness(10, 5, 10, 5));
                items[0].IsEnabled.Should().BeFalse();

                window.Activate();
                button.Focus();
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
                menu.IsOpen.Should().BeTrue();
                RaiseKey(menu, Key.Escape, PresentationSource.FromVisual(window));
                menu.IsOpen.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void CollapsedSplitButton_FlattensPrimaryAndSkipsDuplicateMenuEntry()
    {
        var primary = new RecordingCommand();

        StaTestRunner.Run(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("paste", primary);
            registry.Register("pasteSpecial", new RecordingCommand());
            var root = BuildRibbon(registry);
            var group = Descendants(root).OfType<RibbonGroupHost>().Single();
            group.Collapsed = true;
            Layout(root, 420, 130);

            var button = Descendants(group).OfType<Button>()
                .Single(candidate => candidate.ContextMenu is not null);
            var items = button.ContextMenu!.Items.OfType<MenuItem>().ToList();
            items.Should().ContainSingle(item => Equals(item.Header, "Paste") && item.Items.Count == 0);
            items.Should().ContainSingle(item => Equals(item.Header, "Paste Special"));

            items.Single(item => Equals(item.Header, "Paste"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            primary.Invocations.Should().Be(1);
        });
    }

    [Fact]
    public void CollapsedGroupPopup_NestedMenuUsesSharedChromeAndLeftRestoresParentFocus()
    {
        StaTestRunner.Run(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("child", new RecordingCommand());
            var root = RibbonWpfRenderer.BuildTabContent(
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
                new Border(),
                registry);
            var group = Descendants(root).OfType<RibbonGroupHost>().Single();
            var window = new Window { Content = root, Width = 420, Height = 130 };
            window.Show();
            try
            {
                Layout(root, 420, 130);
                group.Collapsed = true;
                var button = Assert.IsType<Button>(Assert.IsType<Grid>(group.Content).Children[0]);
                var menu = Assert.IsType<ContextMenu>(button.ContextMenu);
                var parent = Assert.Single(menu.Items.OfType<MenuItem>());
                var child = Assert.Single(parent.Items.OfType<MenuItem>());

                parent.MinHeight.Should().Be(RibbonVisualMetrics.PopupChrome.ItemMinHeight);
                child.MinHeight.Should().Be(RibbonVisualMetrics.PopupChrome.Submenu.ItemMinHeight);
                child.Padding.Should().Be(new Thickness(10, 5, 10, 5));

                menu.IsOpen = true;
                parent.IsSubmenuOpen = true;
                parent.ApplyTemplate();
                var submenuPopup = FindPopup(parent);
                submenuPopup.Placement.Should().Be(PlacementMode.Custom);
                submenuPopup.CustomPopupPlacementCallback.Should().NotBeNull();
                child.Focus();
                RaiseKey(child, Key.Left, PresentationSource.FromVisual(window));

                parent.IsSubmenuOpen.Should().BeFalse();
                parent.IsEnabled.Should().BeTrue();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void DropdownPopup_NestedMenuUsesSharedChromeAndRightLeftNavigation()
    {
        StaTestRunner.Run(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("child", new RecordingCommand());
            var root = RibbonWpfRenderer.BuildTabContent(
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
                new Border(),
                registry);
            var window = new Window { Content = root, Width = 420, Height = 130 };
            window.Show();
            try
            {
                Layout(root, 420, 130);
                var dropdown = FindButton(root, "more");
                var menu = Assert.IsType<ContextMenu>(dropdown.ContextMenu);
                var parent = Assert.Single(menu.Items.OfType<MenuItem>());
                var child = Assert.Single(parent.Items.OfType<MenuItem>());

                menu.Placement.Should().Be(PlacementMode.Custom);
                menu.MinWidth.Should().Be(RibbonVisualMetrics.PopupChrome.MinWidth);
                parent.MinHeight.Should().Be(RibbonVisualMetrics.PopupChrome.ItemMinHeight);
                child.MinHeight.Should().Be(RibbonVisualMetrics.PopupChrome.Submenu.ItemMinHeight);

                menu.IsOpen = true;
                window.Dispatcher.Invoke(
                    static () => { },
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                parent.Focus();
                RaiseKey(parent, Key.Right, PresentationSource.FromVisual(window));
                parent.IsSubmenuOpen.Should().BeTrue();
                parent.ApplyTemplate();
                var submenuPopup = FindPopup(parent);
                submenuPopup.Placement.Should().Be(PlacementMode.Custom);
                submenuPopup.CustomPopupPlacementCallback.Should().NotBeNull();
                child.Focus();
                RaiseKey(child, Key.Left, PresentationSource.FromVisual(window));
                parent.IsSubmenuOpen.Should().BeFalse();
                menu.IsOpen.Should().BeTrue();
                window.Dispatcher.Invoke(
                    static () => { },
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                FocusManager.GetFocusedElement(menu).Should().BeSameAs(parent);
                parent.IsSubmenuOpen = true;
                child.Focus();
                RaiseKey(child, Key.Escape, PresentationSource.FromVisual(window));
                parent.IsSubmenuOpen.Should().BeFalse();
                window.Dispatcher.Invoke(
                    static () => { },
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                FocusManager.GetFocusedElement(menu).Should().BeSameAs(parent);
                menu.IsOpen = false;
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void SubmenuTemplateWithoutPartPopup_StillUsesSharedPlacementCallback()
    {
        StaTestRunner.Run(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("child", new RecordingCommand());
            var root = RibbonWpfRenderer.BuildTabContent(
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
                new Border(),
                registry);
            var window = new Window { Content = root, Width = 420, Height = 130 };
            window.Show();
            try
            {
                Layout(root, 420, 130);
                var dropdown = FindButton(root, "more");
                var menu = Assert.IsType<ContextMenu>(dropdown.ContextMenu);
                var parent = Assert.Single(menu.Items.OfType<MenuItem>());
                parent.Template = CreateTemplateWithRenamedPopup();
                parent.ApplyTemplate();
                parent.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, parent));

                var submenuPopup = FindPopup(parent);
                submenuPopup.PlacementTarget.Should().BeSameAs(parent);
                submenuPopup.Placement.Should().Be(PlacementMode.Custom);
                submenuPopup.CustomPopupPlacementCallback.Should().NotBeNull();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void DropdownMenu_PreservesDisabledParentsAndCheckedItems()
    {
        StaTestRunner.Run(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("menu", new RecordingCommand());
            registry.Register("child", new RecordingCommand());
            registry.Register("checked", new RecordingCommand());
            var root = BuildRibbon(registry, new RibbonMenu(new[]
            {
                new RibbonMenuItem("Disabled", Children: new[]
                {
                    new RibbonMenuItem("Child", "child", "C")
                }) { IsEnabled = false },
                new RibbonMenuItem("Checked", "checked") { IsChecked = true }
            }));
            Layout(root, 420, 130);

            var dropdown = FindButton(root, "paste.Dropdown");
            var items = dropdown.ContextMenu!.Items.OfType<MenuItem>().ToArray();

            items.Single(item => Equals(item.Header, "Disabled")).IsEnabled.Should().BeFalse();
            var checkedItem = items.Single(item => Equals(item.Header, "Checked"));
            checkedItem.IsCheckable.Should().BeTrue();
            checkedItem.IsChecked.Should().BeTrue();
        });
    }

    [Fact]
    public void DropdownMenu_RefreshesCheckableStateFromStoreWheneverOpened()
    {
        StaTestRunner.Run(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("menu", new RecordingCommand());
            registry.Register("choice", new RecordingCommand());
            var stateStore = new RibbonStateStore();
            stateStore.SetChecked("choice", false);
            var root = BuildRibbon(
                registry,
                new RibbonMenu(new[]
                {
                    new RibbonMenuItem("Choice", "choice") { IsChecked = false },
                }),
                stateStore: stateStore);
            var window = new Window { Content = root, Width = 420, Height = 130 };
            window.Show();
            try
            {
                Layout(root, 420, 130);
                var dropdown = FindButton(root, "paste.Dropdown");
                var menu = Assert.IsType<ContextMenu>(dropdown.ContextMenu);
                var item = Assert.Single(menu.Items.OfType<MenuItem>());
                item.IsChecked.Should().BeFalse();

                stateStore.SetChecked("choice", true);
                menu.IsOpen = true;

                item.IsChecked.Should().BeTrue();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void DropdownMenu_RendersNeutralInputGestureInShortcutColumn()
    {
        StaTestRunner.Run(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("save", new RecordingCommand());
            var root = BuildRibbon(registry, new RibbonMenu(new[]
            {
                new RibbonMenuItem("Save", "save", InputGesture: "Ctrl+S"),
            }));
            Layout(root, 420, 130);

            var dropdown = FindButton(root, "paste.Dropdown");
            var item = dropdown.ContextMenu!.Items.OfType<MenuItem>().Single();

            item.InputGestureText.Should().Be("Ctrl+S");
        });
    }

    [Fact]
    public void CollapsedGroup_ComboBoxProjectionMatchesAvaloniaEnablementAndExecutes()
    {
        var executions = new RecordingCommand();

        StaTestRunner.Run(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("font", executions);
            var root = BuildComboRibbon(registry);
            var group = Descendants(root).OfType<RibbonGroupHost>().Single();
            Layout(root, 420, 130);
            group.Collapsed = true;

            var collapsedGrid = Assert.IsType<Grid>(group.Content);
            var button = collapsedGrid.Children.OfType<Button>().Single();
            var projection = button.ContextMenu!.Items.OfType<MenuItem>().Single();

            projection.Header.Should().Be("Font");
            projection.IsEnabled.Should().BeTrue();
            projection.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, projection));
            executions.Invocations.Should().Be(1);
        });
    }

    [Fact]
    public void CollapsedGroup_ToggleProjectionRefreshesCheckedAndEnabledStateWhenOpened()
    {
        StaTestRunner.Run(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("toggle", new RecordingCommand());
            var stateStore = new RibbonStateStore();
            stateStore.SetState("toggle", new RibbonCommandState(IsEnabled: true, IsChecked: false));
            var root = RibbonWpfRenderer.BuildTabContent(
                new RibbonDefinitionBuilder()
                    .Tab("home", "Home", "H", tab => tab.Group("group", "Group", "G", 1, group =>
                        group.Toggle("toggle", "Toggle")))
                    .Build()
                    .FindTab("home")!,
                new Border(),
                registry,
                stateStore);
            var group = Descendants(root).OfType<RibbonGroupHost>().Single();
            var window = new Window { Content = root, Width = 420, Height = 130 };
            window.Show();
            try
            {
                Layout(root, 420, 130);
                group.Collapsed = true;

                var button = Assert.IsType<Button>(Assert.IsType<Grid>(group.Content).Children[0]);
                var menu = Assert.IsType<ContextMenu>(button.ContextMenu);
                var projection = Assert.Single(menu.Items.OfType<MenuItem>());
                projection.IsCheckable.Should().BeTrue();
                projection.IsChecked.Should().BeFalse();
                projection.IsEnabled.Should().BeTrue();

                stateStore.SetState("toggle", new RibbonCommandState(IsEnabled: false, IsChecked: true));
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

                projection.IsCheckable.Should().BeTrue();
                projection.IsChecked.Should().BeTrue();
                projection.IsEnabled.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void CollapsedGroup_OmitsSeparatorsAndRowBreaksFromOverflowMenu()
    {
        StaTestRunner.Run(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("one", new RecordingCommand());
            registry.Register("two", new RecordingCommand());
            var root = RibbonWpfRenderer.BuildTabContent(
                new RibbonDefinitionBuilder()
                    .Tab("home", "Home", "H", tab => tab
                        .Group("group", "Group", "G", 1, group => group
                            .Button("one", "One")
                            .Separator()
                            .RowBreak()
                            .Button("two", "Two")))
                    .Build()
                    .FindTab("home")!,
                new Border(),
                registry);
            var group = Descendants(root).OfType<RibbonGroupHost>().Single();
            Layout(root, 420, 130);
            group.Collapsed = true;

            var collapsedButton = Assert.IsType<Button>(Assert.IsType<Grid>(group.Content).Children[0]);
            var items = collapsedButton.ContextMenu!.Items.OfType<MenuItem>().ToArray();

            items.Select(item => item.Header).Should().Equal("One", "Two");
            collapsedButton.ContextMenu.Items.OfType<Separator>().Should().BeEmpty();
        });
    }

    [Fact]
    public void TypedCombo_DisplaysLabels_DispatchesValues_AndMatchesStateByValueWithoutExecuting()
    {
        StaTestRunner.Run(() =>
        {
            var command = new RecordingValueCommand();
            var registry = new RibbonCommandRegistry();
            registry.Register("theme", command);
            var stateStore = new RibbonStateStore();
            stateStore.SetValue("theme", "theme.slate");
            var root = RibbonWpfRenderer.BuildTabContent(
                new RibbonDefinitionBuilder()
                    .Tab("design", "Design", "D", tab => tab
                        .Group("themes", "Themes", "T", 1, group => group
                            .ComboBox("theme", "Theme", combo => combo with
                            {
                                Choices =
                                [
                                    new RibbonComboBoxChoice("theme.office", "Office"),
                                    new RibbonComboBoxChoice("theme.slate", "Slate"),
                                ],
                            })))
                    .Build()
                    .FindTab("design")!,
                new Border(),
                registry,
                stateStore);
            Layout(root, 420, 130);

            var combo = Descendants(root).OfType<ComboBox>().Single();
            combo.DisplayMemberPath.Should().Be(nameof(RibbonComboBoxChoice.Label));
            combo.SelectedIndex.Should().Be(1);
            combo.Text.Should().Be("Slate");

            combo.SelectedIndex = 0;
            command.Values.Should().Equal("theme.office");

            stateStore.SetValue("theme", "theme.office");
            stateStore.SetValue("theme", "theme.slate");
            combo.SelectedIndex.Should().Be(1);
            combo.Text.Should().Be("Slate");
            command.Values.Should().Equal("theme.office");
        });
    }

    private static FrameworkElement BuildRibbon(
        IRibbonCommandRegistry registry,
        RibbonMenu? menu = null,
        RibbonCommandLayoutKind layout = RibbonCommandLayoutKind.Medium,
        IRibbonStateStore? stateStore = null,
        string groupHeader = "Clipboard") =>
        RibbonWpfRenderer.BuildTabContent(
            new RibbonDefinitionBuilder()
                .Tab("home", "Home", "H", tab => tab
                    .Group("clipboard", groupHeader, "C", 1, group => group
                        .SplitButton("paste", "Paste", menu ?? new RibbonMenu(new[]
                        {
                            new RibbonMenuItem("Paste", "paste"),
                            new RibbonMenuItem("Paste Special", "pasteSpecial")
                        }), control => control with
                        {
                            PreferredLayout = layout,
                            KeyTip = "P"
                        })))
                .Build()
                .FindTab("home")!,
            new Border(),
            registry,
            stateStore);

    private static FrameworkElement BuildComboRibbon(IRibbonCommandRegistry registry) =>
        RibbonWpfRenderer.BuildTabContent(
            new RibbonDefinitionBuilder()
                .Tab("home", "Home", "H", tab => tab
                    .Group("font", "Font", "F", 1, group => group
                        .ComboBox("font", "Font", combo => combo with { Items = new[] { "Arial" } })))
                .Build()
                .FindTab("home")!,
            new Border(),
            registry);

    private static void Layout(FrameworkElement root, double width, double height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
    }

    private static void RaiseKey(UIElement target, Key key, PresentationSource? inputSource = null)
    {
        var args = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            inputSource ?? PresentationSource.FromVisual(target)!,
            Environment.TickCount,
            key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        target.RaiseEvent(args);
    }

    private static Button FindButton(DependencyObject root, string commandName) =>
        Descendants(root).OfType<Button>()
            .Single(button => string.Equals(RibbonMetadata.GetCommandName(button), commandName, StringComparison.Ordinal));

    private static Popup FindPopup(DependencyObject root) =>
        Descendants(root).OfType<Popup>().Single();

    private static ControlTemplate CreateTemplateWithRenamedPopup()
    {
        var template = new ControlTemplate(typeof(MenuItem));
        var root = new FrameworkElementFactory(typeof(Grid));
        var popup = new FrameworkElementFactory(typeof(Popup))
        {
            Name = "ThemeSubmenuFlyout",
        };
        root.AppendChild(popup);
        template.VisualTree = root;
        return template;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private sealed class RecordingCommand : IRibbonCommand
    {
        public int Invocations { get; private set; }
        public void Execute(RibbonCommandContext context) => Invocations++;
    }

    private sealed class RecordingValueCommand : IRibbonCommand
    {
        public List<string?> Values { get; } = new();
        public void Execute(RibbonCommandContext context) => Values.Add(context.SelectedValue);
    }

    private static class StaTestRunner
    {
        private static readonly object Sync = new();
        private static readonly Lazy<System.Windows.Threading.Dispatcher> Dispatcher = new(CreateDispatcher);

        public static void Run(Action action)
        {
            var dispatcher = Dispatcher.Value;
            if (dispatcher.CheckAccess())
            {
                action();
                return;
            }

            lock (Sync)
            {
                Exception? failure = null;
                dispatcher.Invoke(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                });
                if (failure is not null)
                    ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        private static System.Windows.Threading.Dispatcher CreateDispatcher()
        {
            System.Windows.Threading.Dispatcher? dispatcher = null;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                ready.Set();
                System.Windows.Threading.Dispatcher.Run();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            ready.Wait();
            return dispatcher!;
        }
    }
}
