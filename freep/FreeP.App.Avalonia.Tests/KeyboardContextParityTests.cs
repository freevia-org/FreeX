using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Free.Shared.Drawing;
using Free.Shared.Ribbon;
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
        await Session.Dispatch(() =>
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
                var shapeCountBeforeCut = window.Editor.CurrentSlide.Shapes.Count;
                window.Editor.Select(shape.Id);
                Press(window, Key.C, KeyModifiers.Control).Handled.Should().BeTrue();
                Press(window, Key.X, KeyModifiers.Control).Handled.Should().BeTrue();
                window.Editor.CurrentSlide.Shapes.Should().NotContain(candidate => candidate.Id == shape.Id);
                Press(window, Key.V, KeyModifiers.Control).Handled.Should().BeTrue();
                window.Editor.CurrentSlide.Shapes.Should().HaveCount(shapeCountBeforeCut);

                Press(window, Key.F, KeyModifiers.Control).Handled.Should().BeTrue();
                window.IsFindReplaceDialogVisible.Should().BeTrue();
                window.IsFindReplaceReplaceInputVisible.Should().BeFalse();
                Press(window, Key.H, KeyModifiers.Control).Handled.Should().BeTrue();
                window.IsFindReplaceReplaceInputVisible.Should().BeTrue();
                Press(window, Key.P, KeyModifiers.Control).Handled.Should().BeTrue();
                window.IsPrintOptionsPaneVisible.Should().BeTrue();
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

                Press(window, Key.RightAlt);
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
                window.Show();
                var combo = window.RibbonControlForTests!
                    .GetLogicalDescendants()
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
                menu!.Items.Should().HaveCount(11);
                menu.Items.OfType<MenuItem>().Select(item => item.Header).Should().Equal(
                    "Insert Row Above", "Insert Row Below", "Insert Column Left", "Insert Column Right",
                    "Delete Row", "Delete Column", "Merge with Right Cell", "Split Cell");
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
