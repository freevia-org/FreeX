using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Drawing;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

public sealed class KeyboardContextParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    [Fact]
    public async Task AvaloniaMissingWpfRoutesExecuteRealCommandEffects()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var originalSlideCount = window.Editor.Presentation.Slides.Count;
                Press(window, Key.D, KeyModifiers.Control).Handled.Should().BeTrue();
                window.Editor.Presentation.Slides.Should().HaveCount(originalSlideCount + 1);

                Press(window, Key.Z, KeyModifiers.Control).Handled.Should().BeTrue();
                window.Editor.Presentation.Slides.Should().HaveCount(originalSlideCount);
                Press(window, Key.Z, KeyModifiers.Control | KeyModifiers.Shift).Handled.Should().BeTrue();
                window.Editor.Presentation.Slides.Should().HaveCount(originalSlideCount + 1);

                var shape = new SlideShape { Id = 701, Name = "Clipboard shape" };
                window.Editor.CurrentSlide!.Shapes.Add(shape);
                var secondShape = new SlideShape { Id = 702, Name = "Second clipboard shape" };
                window.Editor.CurrentSlide.Shapes.Add(secondShape);
                var shapeCountBeforeCut = window.Editor.CurrentSlide.Shapes.Count;
                window.Editor.Select(shape.Id);

                Press(window, Key.A, KeyModifiers.Control).Handled.Should().BeTrue();
                window.Editor.SelectedShapeIds.Should().Equal(shape.Id, secondShape.Id);
                Press(window, Key.C, KeyModifiers.Control).Handled.Should().BeTrue();
                await window.ClipboardOperationForTests;
                Press(window, Key.X, KeyModifiers.Control).Handled.Should().BeTrue();
                await window.ClipboardOperationForTests;
                window.Editor.CurrentSlide.Shapes.Should().NotContain(candidate => candidate.Id == shape.Id);
                Press(window, Key.V, KeyModifiers.Control).Handled.Should().BeTrue();
                await window.ClipboardOperationForTests;
                window.Editor.CurrentSlide.Shapes.Should().HaveCount(shapeCountBeforeCut);

                Press(window, Key.Z, KeyModifiers.Control).Handled.Should().BeTrue();
                window.Editor.CurrentSlide.Shapes.Should().BeEmpty();
                Press(window, Key.Z, KeyModifiers.Control | KeyModifiers.Shift).Handled.Should().BeTrue();
                window.Editor.CurrentSlide.Shapes.Should().HaveCount(shapeCountBeforeCut);

                Press(window, Key.F, KeyModifiers.Control).Handled.Should().BeTrue();
                window.IsFindReplaceDialogVisible.Should().BeTrue();
                window.IsFindReplaceReplaceInputVisible.Should().BeFalse();
                Press(window, Key.H, KeyModifiers.Control).Handled.Should().BeTrue();
                window.IsFindReplaceReplaceInputVisible.Should().BeTrue();
                Press(window, Key.P, KeyModifiers.Control).Handled.Should().BeTrue();
                window.IsBackstageOpen.Should().BeTrue();
                window.CurrentBackstagePaneLabel.Should().Be("Print");
                window.LastPrintBackstagePlan.Should().NotBeNull();
                window.LastPrintOutputPackage.Should().BeNull();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AvaloniaAltKeyTipsActivateTabsAndEscapeDismisses()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                Press(window, Key.LeftAlt).Handled.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTests.Should().BeTrue();

                Press(window, Key.Escape).Handled.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTests.Should().BeFalse();

                Press(window, Key.F10).Handled.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTests.Should().BeTrue();
                Press(window, Key.Escape).Handled.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTests.Should().BeFalse();

                Press(window, Key.RightAlt).Handled.Should().BeTrue();
                Press(window, Key.N).Handled.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTests.Should().BeTrue();
                var tabs = window.RibbonControlForTests.Should().BeOfType<TabControl>().Subject;
                ((TabItem)tabs.SelectedItem!).Tag.Should().Be("insert");

                Press(window, Key.Escape).Handled.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTests.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AvaloniaBackstageEscapeUsesWindowRouteAndRestoresSlideFocus()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.ShowBackstageForTests();
                window.IsBackstageOpen.Should().BeTrue();

                Press(window, Key.Escape).Handled.Should().BeTrue();

                window.IsBackstageOpen.Should().BeFalse();
                window.SlideCanvasFocusedForTests.Should().BeTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AvaloniaAltKeyTipsContinueThroughGroupAndExecuteRibbonCommand()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var before = window.Editor.CurrentSlide!.Shapes.Count;

                Press(window, Key.LeftAlt).Handled.Should().BeTrue();
                Press(window, Key.N).Handled.Should().BeTrue(); // Insert tab
                Press(window, Key.T).Handled.Should().BeTrue(); // Text group
                Press(window, Key.X).Handled.Should().BeTrue(); // Text Box

                window.RibbonKeyTipsVisibleForTests.Should().BeFalse();
                window.Editor.CurrentSlide.Shapes.Should().HaveCount(before + 1);
                window.Editor.CurrentSlide.Shapes.Last().Kind.Should().Be(SlideShapeKind.AutoShape);
                window.Editor.CurrentSlide.Shapes.Last().TextBody.Should().NotBeNull();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AvaloniaAltKeyTipsEnterDropdownMenuAndExecuteNestedMenuCommand()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var shape = new SlideShape { Id = 703, Name = "Animation target" };
                window.Editor.CurrentSlide!.Shapes.Add(shape);
                window.Editor.Select(shape.Id);

                Press(window, Key.LeftAlt).Handled.Should().BeTrue();
                Press(window, Key.A).Handled.Should().BeTrue(); // Animations tab
                Press(window, Key.N).Handled.Should().BeTrue(); // Animation group
                Press(window, Key.B).Handled.Should().BeTrue(); // Blinds In dropdown prefix
                Press(window, Key.I).Handled.Should().BeTrue(); // Blinds In dropdown
                window.RibbonKeyTipMenuOpenForTests.Should().BeTrue();
                Press(window, Key.C).Handled.Should().BeTrue(); // Checkerboard In menu prefix
                Press(window, Key.I).Handled.Should().BeTrue(); // Checkerboard In menu item

                window.RibbonKeyTipsVisibleForTests.Should().BeFalse();
                window.RibbonKeyTipMenuOpenForTests.Should().BeFalse();
                window.Editor.CurrentSlide.Animations.Should().ContainSingle(animation =>
                    animation.ShapeId == shape.Id && animation.Preset == AnimationPreset.Checkerboard);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AvaloniaAltKeyTipsDefersExactBlinkUntilLongerBlindsPrefixIsResolved()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var shape = new SlideShape { Id = 704, Name = "Animation target" };
            try
            {
                window.Editor.CurrentSlide!.Shapes.Add(shape);
                window.Editor.Select(shape.Id);

                Press(window, Key.LeftAlt);
                Press(window, Key.A);
                Press(window, Key.N);
                Press(window, Key.B).Handled.Should().BeTrue();

                window.RibbonKeyTipsVisibleForTests.Should().BeTrue();
                window.RibbonKeyTipMenuOpenForTests.Should().BeFalse();
                window.Editor.CurrentSlide.Animations.Should().BeEmpty(
                    "Blink=B must wait while Blinds In=BI remains an enabled prefix");

                Press(window, Key.I).Handled.Should().BeTrue();
                window.RibbonKeyTipMenuOpenForTests.Should().BeTrue();

                Press(window, Key.Escape).Handled.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTests.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AvaloniaAltKeyTipsExecutesUniqueExactAnimationLeaf()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var shape = new SlideShape { Id = 705, Name = "Animation target" };
            try
            {
                window.Editor.CurrentSlide!.Shapes.Add(shape);
                window.Editor.Select(shape.Id);

                Press(window, Key.LeftAlt).Handled.Should().BeTrue();
                PressKeyTip(window, "ANF");

                window.RibbonKeyTipsVisibleForTests.Should().BeFalse();
                window.Editor.CurrentSlide.Animations.Should().ContainSingle(animation =>
                    animation.ShapeId == shape.Id && animation.Preset == AnimationPreset.Fade);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AvaloniaAltKeyTipsCancelAndRejectUnmatchedDropdownMenuInput()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                Press(window, Key.LeftAlt);
                Press(window, Key.A);
                Press(window, Key.N);
                Press(window, Key.B);
                Press(window, Key.I);
                window.RibbonKeyTipMenuOpenForTests.Should().BeTrue();

                Press(window, Key.Escape).Handled.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTests.Should().BeFalse();
                window.RibbonKeyTipMenuOpenForTests.Should().BeFalse();

                Press(window, Key.LeftAlt);
                Press(window, Key.A);
                Press(window, Key.N);
                Press(window, Key.B);
                Press(window, Key.I);
                Press(window, Key.Q).Handled.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTests.Should().BeFalse();
                window.RibbonKeyTipMenuOpenForTests.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AvaloniaAltKeyTipsKeepsModeOnUnmatchedPlannerInputUntilEscape()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                Press(window, Key.LeftAlt);
                Press(window, Key.A);
                Press(window, Key.N);
                Press(window, Key.Q).Handled.Should().BeTrue();

                window.RibbonKeyTipsVisibleForTests.Should().BeTrue();
                window.RibbonKeyTipMenuOpenForTests.Should().BeFalse();
                Press(window, Key.Escape).Handled.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTests.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AvaloniaAltKeyTipsDoNotExecuteDisabledNestedMenuCommand()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var disabled = new DisabledRibbonCommand();
            try
            {
                window.RibbonCommandRegistryForTests.Register(
                    new RibbonCommandId("freep.anim.entrance.checkerboard"),
                    disabled);

                Press(window, Key.LeftAlt);
                Press(window, Key.A);
                Press(window, Key.N);
                Press(window, Key.B);
                Press(window, Key.I);
                Press(window, Key.C);
                Press(window, Key.I).Handled.Should().BeTrue();

                disabled.ExecuteCount.Should().Be(0);
                window.RibbonKeyTipsVisibleForTests.Should().BeFalse();
                window.RibbonKeyTipMenuOpenForTests.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AvaloniaAltKeyTipsOpenComboBoxAndLeaveLeafCommandsUntouched()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Width = 2400;
                window.Show();
                // Ribbon content is created inside the rendered TabItem template; it is
                // reliably discoverable in the visual tree after Show(), but is not part
                // of the logical descendants exposed by the headless presenter.
                Dispatcher.UIThread.RunJobs();
                var combo = window.RibbonControlForTests!
                    .GetVisualDescendants()
                    .OfType<ComboBox>()
                    .First(control => Equals(control.Tag, "freep.font-family"));
                var definition = FreePRibbonAvalonia.Build();
                var home = definition.Tabs.Single(tab => tab.Id == "home");
                var font = home.Groups.Single(group => group.Id == "font");
                var comboDefinition = font.Controls.Single(control => control.CommandId.Value == "freep.font-family");

                Press(window, Key.LeftAlt).Handled.Should().BeTrue();
                PressKeyTip(window, home.KeyTip!);
                PressKeyTip(window, font.KeyTip!);
                PressKeyTip(window, comboDefinition.KeyTip!);

                combo.IsDropDownOpen.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTests.Should().BeFalse();
                window.RibbonKeyTipMenuOpenForTests.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AvaloniaAltKeyTipsOpenRenderedCollapsedGroupOverflowScope()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                window.MinWidth = 0;
                window.Width = 800;
                window.Height = 600;
                window.Show();

                var ribbon = window.RibbonControlForTests!;
                var definition = FreePRibbonAvalonia.Build();
                var home = definition.Tabs.Single(tab => tab.Id == "home");
                Button? collapsedButton = null;

                foreach (var width in new[] { 800d, 680d, 560d })
                {
                    window.Width = width;
                    Dispatcher.UIThread.RunJobs();
                    collapsedButton = ribbon
                        .GetVisualDescendants()
                        .OfType<Button>()
                        .FirstOrDefault(button => (button.Tag as string)?.StartsWith("collapsed:", StringComparison.Ordinal) == true);
                    if (collapsedButton is not null)
                        break;
                }

                collapsedButton.Should().NotBeNull("the rendered ribbon must expose an overflow button at a constrained width");
                var groupId = (collapsedButton!.Tag as string)!["collapsed:".Length..];
                var group = home.Groups.Single(candidate => candidate.Id == groupId);
                var usedGroupKeyTips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var collapsedGroupKeyTip = home.Groups
                    .Select(candidate =>
                    {
                        var keyTip = RibbonCollapsedGroupPresentationPlanner.DeriveGroupKeyTip(
                            candidate.Header,
                            usedGroupKeyTips);
                        return (candidate, keyTip);
                    })
                    .Single(entry => entry.candidate.Id == group.Id)
                    .keyTip;

                Press(window, Key.LeftAlt).Handled.Should().BeTrue();
                PressKeyTip(window, home.KeyTip!);
                PressKeyTip(window, collapsedGroupKeyTip);

                window.RibbonKeyTipMenuOpenForTests.Should().BeTrue();
                window.RibbonKeyTipFlyoutOpenForTests.Should().BeTrue();
                window.RibbonKeyTipRenderedMenuItemsForTests.Should().NotBeEmpty(
                    "collapsed-group key tips must bind to the actual renderer-created overflow items");

                Press(window, Key.Escape).Handled.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTests.Should().BeFalse();
                window.RibbonKeyTipMenuOpenForTests.Should().BeFalse();
                window.RibbonKeyTipFlyoutOpenForTests.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RendererBackedNestedKeyTipTraversalOpensVisibleChildSubmenu()
    {
        await Session.Dispatch(() =>
        {
            var commandId = new RibbonCommandId("test.nested.menu.leaf");
            var menu = new RibbonMenu([
                new RibbonMenuItem(
                    "More",
                    KeyTip: "M",
                    Children: [new RibbonMenuItem("Leaf", commandId, "L")])
            ]);
            var definition = new RibbonDefinitionBuilder()
                .Tab("home", "Home", "H", tab => tab.Group("test", "Test", "T", 100, group =>
                    group.Dropdown("test.nested.menu", "More", menu)))
                .Build();
            var registry = new RibbonCommandRegistry();
            registry.Register(commandId, new ActionRibbonCommand(() => { }));
            var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry);
            var host = new Window
            {
                Width = 600,
                Height = 200,
                Content = ribbon,
            };
            var window = new MainWindow([]);
            MenuFlyout? flyout = null;
            try
            {
                host.Show();
                var dropdown = ribbon
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Single(button => button.Flyout is MenuFlyout);
                flyout = (MenuFlyout)dropdown.Flyout!;
                flyout.ShowAt(dropdown);
                var parent = flyout.Items.OfType<MenuItem>().Single();

                window.SetRibbonKeyTipMenuScopeForTests(menu, flyout);
                window.HandleRibbonMenuKeyTipForTests("M").Should().BeTrue();
                parent.IsSubMenuOpen.Should().BeTrue(
                    "deeper key-tip traversal must open the visible renderer submenu");
                window.RibbonKeyTipRenderedMenuItemsForTests
                    .Should().ContainSingle(item => Equals(item.Header, "Leaf"));
            }
            finally
            {
                flyout?.Hide();
                host.Close();
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RendererBackedLargeSplitKeyTipLookupOpensDropdownFlyout()
    {
        await Session.Dispatch(() =>
        {
            var commandId = new RibbonCommandId("test.large-split");
            var menuCommandId = new RibbonCommandId("test.large-split.menu");
            var definition = new RibbonDefinitionBuilder()
                .Tab("home", "Home", "H", tab => tab.Group("clipboard", "Clipboard", "C", 100, group =>
                    group.SplitButton(
                        commandId.Value,
                        "Paste",
                        new RibbonMenu([new RibbonMenuItem("Paste Special", menuCommandId, "S")]),
                        split => split with { PreferredLayout = RibbonCommandLayoutKind.Large })))
                .Build();
            var registry = new RibbonCommandRegistry();
            registry.Register(commandId, new ActionRibbonCommand(() => { }));
            registry.Register(menuCommandId, new ActionRibbonCommand(() => { }));
            var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry);
            var host = new Window
            {
                Width = 600,
                Height = 200,
                Content = ribbon,
            };
            FlyoutBase? flyout = null;
            try
            {
                host.Show();
                var dropdown = ribbon
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Single(button => Equals(button.Tag, $"{commandId.Value}.Dropdown"));

                dropdown.Flyout.Should().NotBeNull();
                flyout = MainWindow.ShowRibbonFlyout(ribbon, commandId);

                flyout.Should().BeSameAs(dropdown.Flyout);
                flyout!.IsOpen.Should().BeTrue();
            }
            finally
            {
                flyout?.Hide();
                host.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AvaloniaMenusRenderSharedStateExecuteAndSupportKeyboardLifecycle()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Editor.InsertSlide();
                var slideMenu = window.BuildSlidePaneContextMenuForTests(0);
                AssertMenuMatches(
                    slideMenu,
                    FreePContextMenuCatalog.BuildSlideMenu(
                        window.Editor.Presentation.Slides,
                        window.Editor.Presentation.Sections,
                        0));

                var before = window.Editor.Presentation.Slides.Count;
                slideMenu.Items.OfType<MenuItem>()
                    .Single(item => Equals(item.Tag, FreePContextMenuCommand.DuplicateSlide))
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                window.Editor.Presentation.Slides.Should().HaveCount(before + 1);

                window.Show();
                var slideItem = window.GetLogicalDescendants()
                    .OfType<ListBoxItem>()
                    .First(item => item.Tag is int);
                slideItem.ContextMenu.Should().NotBeNull();
                var keyboardMenu = slideItem.ContextMenu!;

                var open = RoutedKey(Key.F10, KeyModifiers.Shift, slideItem);
                slideItem.RaiseEvent(open);
                open.Handled.Should().BeTrue();
                keyboardMenu.IsOpen.Should().BeTrue();

                var escape = RoutedKey(Key.Escape, KeyModifiers.None, keyboardMenu);
                keyboardMenu.RaiseEvent(escape);
                escape.Handled.Should().BeTrue();
                keyboardMenu.IsOpen.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SlidePaneFocusSurvivesDuplicateUndoRedoAndRoutesDeleteToSharedPlanner()
    {
        MainWindow? window = null;
        await Session.Dispatch(() =>
        {
            window = new MainWindow([]);
            window.Show();
            window.Editor.InsertSlide();
        }, CancellationToken.None);

        try
        {
            await Session.Dispatch(() =>
            {
                window.Should().NotBeNull();
                window!.Activate();
                var selected = window.SelectedSlidePaneItemForTests;
                selected.Should().NotBeNull();
                selected!.Focus().Should().BeTrue();

                var duplicate = RoutedKey(Key.D, KeyModifiers.Control, selected);
                selected.RaiseEvent(duplicate);
                duplicate.Handled.Should().BeTrue();
                window.Editor.Presentation.Slides.Should().HaveCount(3);

                window.Editor.Undo();
                window.Editor.Redo();
            }, CancellationToken.None);

            await Session.Dispatch(() =>
            {
                window.Should().NotBeNull();
                var rebuiltSelected = window.SelectedSlidePaneItemForTests;
                rebuiltSelected.Should().NotBeNull();
                rebuiltSelected!.IsFocused.Should().BeTrue(
                    "slide-pane rebuilds after undo/redo must preserve keyboard routing");

                var delete = RoutedKey(Key.Delete, KeyModifiers.None, rebuiltSelected);
                rebuiltSelected.RaiseEvent(delete);
                delete.Handled.Should().BeTrue();
                window.Editor.Presentation.Slides.Should().HaveCount(2);
            }, CancellationToken.None);
        }
        finally
        {
            await Session.Dispatch(() =>
            {
                try
                {
                    window?.Close();
                }
                catch
                {
                }
            }, CancellationToken.None);
        }
    }

    [Fact]
    public async Task AvaloniaTableContextMenuMatchesWpfAndExecutesCellMutations()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var table = new SlideShape
                {
                    Id = 702,
                    Kind = SlideShapeKind.Table,
                    Table = new TableShape
                    {
                        ColumnWidthsEmu = { 100, 100 },
                        Rows =
                        {
                            new TableRow { Cells = { new TableCell(), new TableCell() } },
                            new TableRow { Cells = { new TableCell(), new TableCell() } },
                        }
                    }
                };
                window.Editor.CurrentSlide!.Shapes.Add(table);
                window.Editor.Select(table.Id);
                window.Editor.SetActiveTableCell(0, 0);

                var menu = window.BuildTableContextMenuForTests(table.Id);
                menu.Should().NotBeNull();
                menu!.Placement.Should().Be(PlacementMode.Pointer);
                menu.Items.Should().HaveCount(12);
                menu.Items.OfType<MenuItem>().Select(item => item.Header).Should().Equal(
                    "Insert Row Above", "Insert Row Below", "Insert Column Left", "Insert Column Right",
                    "Delete Row", "Delete Column", "Column Width", "Merge with Right Cell", "Split Cell");
                var widthMenu = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Column Width"));
                widthMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "1.50 in"))
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                table.Table.ColumnWidthsEmu[0].Should().Be(1371600);
                menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Merge with Right Cell"))
                    .IsEnabled.Should().BeTrue();
                menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Split Cell"))
                    .IsEnabled.Should().BeFalse();

                menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Merge with Right Cell"))
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                table.Table.Rows[0].Cells[0].GridSpan.Should().Be(2);
                table.Table.Rows[0].Cells[1].HMerge.Should().BeTrue();

                var splitMenu = window.BuildTableContextMenuForTests(table.Id)!;
                splitMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Split Cell"))
                    .IsEnabled.Should().BeTrue();
                splitMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Split Cell"))
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                table.Table.Rows[0].Cells[0].GridSpan.Should().Be(1);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static void AssertMenuMatches(
        ContextMenu menu,
        IReadOnlyList<FreePContextMenuEntryPlan> expected)
    {
        menu.Items.Should().HaveCount(expected.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            if (expected[index].Kind == FreePContextMenuEntryKind.Separator)
            {
                menu.Items[index].Should().BeOfType<Separator>();
                continue;
            }

            var item = menu.Items[index].Should().BeOfType<MenuItem>().Subject;
            item.Tag.Should().Be(expected[index].Command);
            item.Header.Should().Be(expected[index].Text);
            item.IsEnabled.Should().Be(expected[index].IsEnabled);
            item.IsChecked.Should().Be(expected[index].IsChecked);
            (item.ToggleType == MenuItemToggleType.CheckBox)
                .Should().Be(expected[index].IsCheckable);
        }
    }

    private static KeyEventArgs Press(
        MainWindow window,
        Key key,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = modifiers };
        window.RaiseKeyDownForTests(args);
        return args;
    }

    private static void PressKeyTip(MainWindow window, string keyTip)
    {
        foreach (var character in keyTip)
        {
            var key = char.IsAsciiDigit(character)
                ? Enum.Parse<Key>("D" + character)
                : Enum.Parse<Key>(char.ToUpperInvariant(character).ToString());
            Press(window, key).Handled.Should().BeTrue();
        }
    }

    private static KeyEventArgs RoutedKey(Key key, KeyModifiers modifiers, object source) => new()
    {
        RoutedEvent = InputElement.KeyDownEvent,
        Key = key,
        KeyModifiers = modifiers,
        Source = source,
    };

    private sealed class DisabledRibbonCommand : IRibbonStatefulCommand
    {
        public int ExecuteCount { get; private set; }

        public void Execute(RibbonCommandContext context) => ExecuteCount++;

        public RibbonCommandState GetState() => new(IsEnabled: false);
    }
}
