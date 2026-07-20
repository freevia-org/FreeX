using System.Windows.Controls;
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
    public void WpfMenusRenderSharedOrderEnabledAndCheckedStateAndExecute()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Add(new Slide { Title = "Second" });
        var section = new PresentationSection { Name = "Intro" };
        section.SlideIds.Add(presentation.Slides[0].Id);
        presentation.Sections.Add(section);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var pane = new SlidePane(editor);

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
