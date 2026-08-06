using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FreeP.App.Compositor;

namespace FreeP.App.Host.Tests;

public sealed class KeyboardContextParityTests
{
    [StaFact]
    public void WpfWindowInstallsEverySharedShortcutGesture()
    {
        var window = new MainWindow();
        try
        {
            var actual = window.InputBindings
                .OfType<KeyBinding>()
                .Where(binding => binding.Command is RoutedUICommand command &&
                    command.Name.StartsWith("FreeP", StringComparison.Ordinal))
                .Select(binding => (KeyGesture)binding.Gesture)
                .Select(gesture => (gesture.Key, gesture.Modifiers))
                .ToArray();
            var expected = FreePKeyboardShortcutCatalog.All
                .Select(shortcut => (ToWpfKey(shortcut.Key), ToWpfModifiers(shortcut.Modifiers)))
                .ToArray();

            actual.Should().HaveCount(18);
            actual.Should().BeEquivalentTo(expected);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void WpfSharedShortcutsExecuteModelEffects()
    {
        var window = new MainWindow(
            new FreePOptions(),
            messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Shapes.Add(new SlideShape { Id = 41, Name = "One" });
            window.Editor.CurrentSlide.Shapes.Add(new SlideShape { Id = 42, Name = "Two" });

            Execute(window, Key.A, ModifierKeys.Control);
            window.Editor.SelectedShapeIds.Should().Contain(new uint[] { 41, 42 });

            var before = window.Editor.Presentation.Slides.Count;
            Execute(window, Key.D, ModifierKeys.Control);
            window.Editor.Presentation.Slides.Should().HaveCount(before + 1);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void WpfCtrlPOpensPrintBackstageAndBuildsSharedPlan()
    {
        var window = new MainWindow(
            new FreePOptions(),
            messageService: TestUserMessageService.DiscardUnsavedChanges,
            nativePrintCapability: WpfNativePrintCapability.Unavailable("Test printer handoff deferred."));
        try
        {
            Execute(window, Key.P, ModifierKeys.Control);

            window.IsBackstageOpen.Should().BeTrue();
            window.CurrentBackstagePaneLabel.Should().Be("Print");
            window.LastPrintBackstagePlan.Should().NotBeNull();
            window.LastPrintOutputPackage.Should().BeNull();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void WpfMenusRenderSharedOrderEnabledAndCheckedStateAndExecute()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Add(new Slide { Title = "Second" });
        var section = new PresentationSection { Name = "Intro" };
        section.SlideIds.Add(presentation.Slides[0].Id);
        presentation.Sections.Add(section);
        var pane = SlidePaneTestFactory.Create(presentation);

        var slideMenu = pane.BuildSlideContextMenuForTests(0);
        AssertMenuMatches(
            slideMenu,
            FreePContextMenuCatalog.BuildSlideMenu(presentation.Slides, presentation.Sections, 0));

        var sectionEntry = new SlidePaneEntry(
            SlidePaneEntryKind.SectionHeader,
            SlideIndex: 0,
            Text: "Intro  (1)",
            SectionSlideCount: 1,
            SectionIndex: 0,
            SectionId: section.Id);
        AssertMenuMatches(
            pane.BuildSectionContextMenuForTests(sectionEntry),
            FreePContextMenuCatalog.BuildSectionHeaderMenu(presentation.Sections, 0, 0));

        var before = presentation.Slides.Count;
        slideMenu.Items.OfType<MenuItem>()
            .Single(item => Equals(item.Tag, FreePContextMenuCommand.DuplicateSlide))
            .RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
        presentation.Slides.Should().HaveCount(before + 1);

        var hiddenMenu = pane.BuildSlideContextMenuForTests(0);
        hiddenMenu.Items.OfType<MenuItem>()
            .Single(item => Equals(item.Tag, FreePContextMenuCommand.ToggleHiddenSlide))
            .RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
        presentation.Slides[0].IsHidden.Should().BeTrue();
    }

    [StaFact]
    public void WpfTableContextMenuExecutesCellMutations()
    {
        var window = new MainWindow(
            new FreePOptions(),
            messageService: TestUserMessageService.DiscardUnsavedChanges);
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
            menu!.Placement.Should().Be(PlacementMode.MousePoint);
            menu.Items.Cast<object>().Should().HaveCount(12);
            menu.Items.OfType<MenuItem>().Select(item => item.Header).Should().Equal(
                "Insert Row Above", "Insert Row Below", "Insert Column Left", "Insert Column Right",
                "Delete Row", "Delete Column", "Column Width", "Merge with Right Cell", "Split Cell");
            var widthMenu = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Column Width"));
            widthMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "1.50 in"))
                .RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
            table.Table.ColumnWidthsEmu[0].Should().Be(1371600);
            menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Merge with Right Cell"))
                .IsEnabled.Should().BeTrue();
            menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Split Cell"))
                .IsEnabled.Should().BeFalse();

            menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Merge with Right Cell"))
                .RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
            table.Table.Rows[0].Cells[0].GridSpan.Should().Be(2);
            table.Table.Rows[0].Cells[1].HMerge.Should().BeTrue();

            var splitMenu = window.BuildTableContextMenuForTests(table.Id)!;
            splitMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Split Cell"))
                .IsEnabled.Should().BeTrue();
            splitMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Split Cell"))
                .RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
            table.Table.Rows[0].Cells[0].GridSpan.Should().Be(1);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void WpfChartContextMenuUsesSharedWaterfallStateAndCommands()
    {
        var window = new MainWindow(
            new FreePOptions(),
            messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var chart = window.Editor.InsertChart(ChartType.Waterfall);
            var hit = new ChartSubtargetHit(
                chart.Id,
                ChartSubtargetKind.Point,
                SeriesIndex: 0,
                PointIndex: 1);

            var menu = window.BuildChartContextMenuForTests(hit);
            menu.Items.OfType<MenuItem>().First().Header.Should().Be("Set as Total");
            menu.Items.OfType<MenuItem>().First()
                .RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
            chart.Chart!.WaterfallTotalPointIndices.Should().Contain(1);

            window.BuildChartContextMenuForTests(hit)
                .Items.OfType<MenuItem>().First().Header.Should().Be("Clear Total");
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertMenuMatches(
        ContextMenu menu,
        IReadOnlyList<FreePContextMenuEntryPlan> expected)
    {
        menu.Items.Cast<object>().Should().HaveCount(expected.Count);
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
            item.IsCheckable.Should().Be(expected[index].IsCheckable);
            item.IsChecked.Should().Be(expected[index].IsChecked);
        }
    }

    private static void Execute(MainWindow window, Key key, ModifierKeys modifiers)
    {
        var binding = window.InputBindings.OfType<KeyBinding>()
            .Single(candidate => candidate.Gesture is KeyGesture gesture &&
                gesture.Key == key && gesture.Modifiers == modifiers);
        ((RoutedCommand)binding.Command).Execute(null, window);
    }

    private static Key ToWpfKey(FreePKeyboardKey key) => Enum.Parse<Key>(key.ToString());

    private static ModifierKeys ToWpfModifiers(FreePKeyboardModifiers modifiers)
    {
        var result = ModifierKeys.None;
        if ((modifiers & FreePKeyboardModifiers.Control) != 0)
            result |= ModifierKeys.Control;
        if ((modifiers & FreePKeyboardModifiers.Shift) != 0)
            result |= ModifierKeys.Shift;
        if ((modifiers & FreePKeyboardModifiers.Alt) != 0)
            result |= ModifierKeys.Alt;
        return result;
    }
}
