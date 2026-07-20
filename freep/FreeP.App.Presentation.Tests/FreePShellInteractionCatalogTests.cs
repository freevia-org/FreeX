using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class FreePShellInteractionCatalogTests
{
    [Fact]
    public void ShortcutCatalogDefinesTheCompleteHostUnionWithoutGestureCollisions()
    {
        FreePKeyboardShortcutCatalog.All.Should().HaveCount(18);
        FreePKeyboardShortcutCatalog.All
            .Select(shortcut => (shortcut.Key, shortcut.Modifiers))
            .Should().OnlyHaveUniqueItems();
        FreePKeyboardShortcutCatalog.All
            .Select(shortcut => shortcut.Command)
            .Should().Contain(Enum.GetValues<FreePKeyboardCommand>());
    }

    [Fact]
    public void EveryShortcutDispatchesItsDeclaredCommand()
    {
        foreach (var shortcut in FreePKeyboardShortcutCatalog.All)
        {
            FreePKeyboardCommand? dispatched = null;

            FreePKeyboardShortcutCatalog.TryDispatch(
                    shortcut.Key,
                    shortcut.Modifiers,
                    command => dispatched = command)
                .Should().BeTrue();
            dispatched.Should().Be(shortcut.Command);
        }
    }

    [Fact]
    public void SlideMenuPreservesWpfOrderSeparatorsAndDynamicState()
    {
        var presentation = Presentation.CreateEmpty();

        var single = FreePContextMenuCatalog.BuildSlideMenu(
            presentation.Slides,
            presentation.Sections,
            slideIndex: 0);

        single.Select(Describe).Should().Equal(
            "AddSection:Add Section:enabled:unchecked",
            "separator",
            "NewSlide:New Slide:enabled:unchecked",
            "DuplicateSlide:Duplicate Slide:enabled:unchecked",
            "separator",
            "DeleteSlide:Delete Slide:disabled:unchecked");

        presentation.Slides.Add(new Slide());
        var multiple = FreePContextMenuCatalog.BuildSlideMenu(
            presentation.Slides,
            presentation.Sections,
            slideIndex: 1);
        multiple[^1].IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void SectionMenuPreservesWpfOrderSeparatorsAndDynamicState()
    {
        var presentation = Presentation.CreateEmpty();
        var section = new PresentationSection { Name = "Intro" };
        section.SlideIds.Add(presentation.Slides[0].Id);
        presentation.Sections.Add(section);

        var valid = FreePContextMenuCatalog.BuildSectionHeaderMenu(
            presentation.Sections,
            sectionIndex: 0,
            slideIndex: 0);

        valid.Select(Describe).Should().Equal(
            "RenameSection:Rename Section:enabled:unchecked",
            "separator",
            "RemoveSection:Remove Section:enabled:unchecked",
            "RemoveAllSections:Remove All Sections:enabled:unchecked");

        var invalid = FreePContextMenuCatalog.BuildSectionHeaderMenu(
            presentation.Sections,
            sectionIndex: 4,
            slideIndex: 0);
        invalid[0].IsEnabled.Should().BeFalse();
        invalid[2].IsEnabled.Should().BeFalse();
        invalid[3].IsEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(FreePKeyboardKey.Apps, FreePKeyboardModifiers.None, true)]
    [InlineData(FreePKeyboardKey.F10, FreePKeyboardModifiers.Shift, true)]
    [InlineData(FreePKeyboardKey.F10, FreePKeyboardModifiers.None, false)]
    [InlineData(FreePKeyboardKey.Apps, FreePKeyboardModifiers.Control, false)]
    public void ContextMenuInvocationOwnsOnlyMenuAndShiftF10(
        FreePKeyboardKey key,
        FreePKeyboardModifiers modifiers,
        bool expected) =>
        FreePContextMenuCatalog.IsKeyboardInvocation(key, modifiers).Should().Be(expected);

    [Theory]
    [InlineData(FreePKeyboardKey.Escape, FreePKeyboardModifiers.None, true)]
    [InlineData(FreePKeyboardKey.Escape, FreePKeyboardModifiers.Shift, false)]
    [InlineData(FreePKeyboardKey.F10, FreePKeyboardModifiers.None, false)]
    public void ContextMenuDismissalOwnsOnlyUnmodifiedEscape(
        FreePKeyboardKey key,
        FreePKeyboardModifiers modifiers,
        bool expected) =>
        FreePContextMenuCatalog.IsKeyboardDismissal(key, modifiers).Should().Be(expected);

    private static string Describe(FreePContextMenuEntryPlan entry) =>
        entry.Kind == FreePContextMenuEntryKind.Separator
            ? "separator"
            : $"{entry.Command}:{entry.Text}:{(entry.IsEnabled ? "enabled" : "disabled")}:" +
              $"{(entry.IsChecked ? "checked" : "unchecked")}";
}
