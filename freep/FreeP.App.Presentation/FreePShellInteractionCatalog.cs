using FreeP.Core.Model;
using Free.Shared.Shell;

namespace FreeP.App.Compositor;

[Flags]
public enum FreePKeyboardModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
}

public enum FreePKeyboardKey
{
    A,
    C,
    D,
    F,
    H,
    N,
    O,
    P,
    S,
    V,
    X,
    Y,
    Z,
    Delete,
    F5,
    F10,
    Apps,
    Escape,
}

public enum FreePKeyboardCommand
{
    NewPresentation,
    OpenPresentation,
    SavePresentation,
    SavePresentationAs,
    PrintPresentation,
    Undo,
    Redo,
    DeleteSelectedShapes,
    DuplicateCurrentSlide,
    StartSlideShowFromBeginning,
    StartSlideShowFromCurrentSlide,
    Copy,
    Cut,
    Paste,
    Find,
    Replace,
    SelectAll,
}

public readonly record struct FreePKeyboardShortcut(
    FreePKeyboardCommand Command,
    FreePKeyboardKey Key,
    FreePKeyboardModifiers Modifiers);

/// <summary>
/// Host-neutral main-window shortcut contract shared by the WPF and Avalonia shells.
/// The catalog is the union of the WPF command routes and Avalonia's direct Select All route.
/// </summary>
public static class FreePKeyboardShortcutCatalog
{
    private static readonly FreePKeyboardShortcut[] Shortcuts =
    [
        new(FreePKeyboardCommand.NewPresentation, FreePKeyboardKey.N, FreePKeyboardModifiers.Control),
        new(FreePKeyboardCommand.OpenPresentation, FreePKeyboardKey.O, FreePKeyboardModifiers.Control),
        new(FreePKeyboardCommand.SavePresentation, FreePKeyboardKey.S, FreePKeyboardModifiers.Control),
        new(FreePKeyboardCommand.SavePresentationAs, FreePKeyboardKey.S, FreePKeyboardModifiers.Control | FreePKeyboardModifiers.Shift),
        new(FreePKeyboardCommand.PrintPresentation, FreePKeyboardKey.P, FreePKeyboardModifiers.Control),
        new(FreePKeyboardCommand.Undo, FreePKeyboardKey.Z, FreePKeyboardModifiers.Control),
        new(FreePKeyboardCommand.Redo, FreePKeyboardKey.Y, FreePKeyboardModifiers.Control),
        new(FreePKeyboardCommand.Redo, FreePKeyboardKey.Z, FreePKeyboardModifiers.Control | FreePKeyboardModifiers.Shift),
        new(FreePKeyboardCommand.DeleteSelectedShapes, FreePKeyboardKey.Delete, FreePKeyboardModifiers.None),
        new(FreePKeyboardCommand.DuplicateCurrentSlide, FreePKeyboardKey.D, FreePKeyboardModifiers.Control),
        new(FreePKeyboardCommand.StartSlideShowFromBeginning, FreePKeyboardKey.F5, FreePKeyboardModifiers.None),
        new(FreePKeyboardCommand.StartSlideShowFromCurrentSlide, FreePKeyboardKey.F5, FreePKeyboardModifiers.Shift),
        new(FreePKeyboardCommand.Copy, FreePKeyboardKey.C, FreePKeyboardModifiers.Control),
        new(FreePKeyboardCommand.Cut, FreePKeyboardKey.X, FreePKeyboardModifiers.Control),
        new(FreePKeyboardCommand.Paste, FreePKeyboardKey.V, FreePKeyboardModifiers.Control),
        new(FreePKeyboardCommand.Find, FreePKeyboardKey.F, FreePKeyboardModifiers.Control),
        new(FreePKeyboardCommand.Replace, FreePKeyboardKey.H, FreePKeyboardModifiers.Control),
        new(FreePKeyboardCommand.SelectAll, FreePKeyboardKey.A, FreePKeyboardModifiers.Control),
    ];

    private static readonly ApplicationKeyboardShortcutCatalog<
        FreePKeyboardCommand,
        FreePKeyboardKey,
        FreePKeyboardModifiers> Resolver = new(
            Shortcuts.Select(shortcut => new ApplicationKeyboardShortcut<
                FreePKeyboardCommand,
                FreePKeyboardKey,
                FreePKeyboardModifiers>(
                    shortcut.Command,
                    shortcut.Key,
                    shortcut.Modifiers)));

    public static IReadOnlyList<FreePKeyboardShortcut> All => Shortcuts;

    public static FreePKeyboardCommand? Resolve(
        FreePKeyboardKey key,
        FreePKeyboardModifiers modifiers) =>
        Resolver.Resolve(key, modifiers);

    public static bool TryDispatch(
        FreePKeyboardKey key,
        FreePKeyboardModifiers modifiers,
        Action<FreePKeyboardCommand> dispatch) =>
        Resolver.TryDispatch(key, modifiers, dispatch);
}

public enum FreePContextMenuCommand
{
    AddSection,
    NewSlide,
    DuplicateSlide,
    DeleteSlide,
    ToggleHiddenSlide,
    RenameSection,
    RemoveSection,
    RemoveAllSections,
}

public enum FreePContextMenuEntryKind
{
    Command,
    Separator,
}

public sealed record FreePContextMenuEntryPlan(
    FreePContextMenuEntryKind Kind,
    FreePContextMenuCommand? Command,
    string Text,
    bool IsEnabled,
    bool IsCheckable = false,
    bool IsChecked = false)
{
    public static FreePContextMenuEntryPlan Separator() =>
        new(FreePContextMenuEntryKind.Separator, null, string.Empty, false);
}

/// <summary>
/// Exact shared structure and state for the WPF-authored slide-pane context menus.
/// </summary>
public static class FreePContextMenuCatalog
{
    public static IReadOnlyList<FreePContextMenuEntryPlan> BuildSlideMenu(
        IReadOnlyList<Slide> slides,
        IReadOnlyList<PresentationSection> sections,
        int slideIndex)
    {
        ArgumentNullException.ThrowIfNull(slides);
        ArgumentNullException.ThrowIfNull(sections);

        var addSection = SlideSectionPlanner.BuildSlideContextActions(slides, sections, slideIndex).Single();
        var slideActions = SlidePanePlanner.BuildContextActions(slides.Count, slideIndex);
        var hiddenAction = SlidePanePlanner.BuildHiddenSlideAction(slides, slideIndex);

        return
        [
            Command(FreePContextMenuCommand.AddSection, addSection.Text, addSection.IsEnabled),
            FreePContextMenuEntryPlan.Separator(),
            Command(FreePContextMenuCommand.NewSlide, slideActions[0].Text, slideActions[0].IsEnabled),
            Command(FreePContextMenuCommand.DuplicateSlide, slideActions[1].Text, slideActions[1].IsEnabled),
            Command(FreePContextMenuCommand.ToggleHiddenSlide, hiddenAction.Text, hiddenAction.IsEnabled,
                isCheckable: true, isChecked: hiddenAction.IsChecked),
            FreePContextMenuEntryPlan.Separator(),
            Command(FreePContextMenuCommand.DeleteSlide, slideActions[2].Text, slideActions[2].IsEnabled),
        ];
    }

    public static IReadOnlyList<FreePContextMenuEntryPlan> BuildSectionHeaderMenu(
        IReadOnlyList<PresentationSection> sections,
        int sectionIndex,
        int slideIndex)
    {
        ArgumentNullException.ThrowIfNull(sections);

        var actions = SlideSectionPlanner.BuildSectionHeaderActions(sections, sectionIndex, slideIndex);
        return
        [
            Command(FreePContextMenuCommand.RenameSection, actions[0].Text, actions[0].IsEnabled),
            FreePContextMenuEntryPlan.Separator(),
            Command(FreePContextMenuCommand.RemoveSection, actions[1].Text, actions[1].IsEnabled),
            Command(FreePContextMenuCommand.RemoveAllSections, actions[2].Text, actions[2].IsEnabled),
        ];
    }

    public static bool IsKeyboardInvocation(
        FreePKeyboardKey key,
        FreePKeyboardModifiers modifiers) =>
        key == FreePKeyboardKey.Apps && modifiers == FreePKeyboardModifiers.None ||
        key == FreePKeyboardKey.F10 && modifiers == FreePKeyboardModifiers.Shift;

    public static bool IsKeyboardDismissal(
        FreePKeyboardKey key,
        FreePKeyboardModifiers modifiers) =>
        key == FreePKeyboardKey.Escape && modifiers == FreePKeyboardModifiers.None;

    private static FreePContextMenuEntryPlan Command(
        FreePContextMenuCommand command,
        string text,
        bool isEnabled,
        bool isCheckable = false,
        bool isChecked = false) =>
        new(FreePContextMenuEntryKind.Command, command, text, isEnabled, isCheckable, isChecked);
}
