namespace FreeP.App.Compositor;

/// <summary>
/// Shared accessibility contract for persistent and task panes rendered by both
/// FreeP hosts. Hosts own framework-specific attached-property calls; this
/// planner owns the stable vocabulary and ordering consumed by assistive tools.
/// </summary>
public static class PresentationPaneAccessibilityPlanner
{
    public const string SlidePaneId = "slide-pane";
    public const string NotesPaneId = "notes-pane";
    public const string CommentsPaneId = "comments-pane";
    public const string AccessibilityPaneId = "accessibility-pane";
    public const string AltTextPaneId = "alt-text-pane";
    public const string ReadingOrderPaneId = "reading-order-pane";
    public const string ProofingPaneId = "proofing-pane";
    public const string MediaCaptionPaneId = "media-caption-pane";
    public const string SmartArtTextPaneId = "smartart-text-pane";
    public const string SelectionPaneId = "selection-pane";
    public const string AnimationPaneId = "animation-pane";

    private static readonly IReadOnlyList<PresentationPaneAccessibilityDescriptor> descriptors =
    [
        new(SlidePaneId, "FreePSlidePane", "Slides", "Navigate slides and sections.", 0),
        new(NotesPaneId, "FreePNotesPane", "Notes", "Read or edit notes for the current slide.", 1),
        new(CommentsPaneId, "FreePCommentsPane", "Comments", "Review comments for the current slide.", 2),
        new(AccessibilityPaneId, "FreePAccessibilityPane", "Accessibility", "Review accessibility issues and details.", 3),
        new(AltTextPaneId, "FreePAltTextPane", "Alt Text", "Edit alternative text for the selected object.", 4),
        new(ReadingOrderPaneId, "FreePReadingOrderPane", "Reading Order", "Review and reorder objects for assistive reading.", 5),
        new(ProofingPaneId, "FreePProofingPane", "Spelling", "Review spelling and proofing suggestions.", 6),
        new(MediaCaptionPaneId, "FreePMediaCaptionPane", "Media Captions", "Edit captions and transcripts for media.", 7),
        new(SmartArtTextPaneId, "FreePSmartArtTextPane", "SmartArt Text Pane", "Edit the SmartArt outline and structure.", 8),
        new(SelectionPaneId, "FreePSelectionPane", "Selection Pane", "Select, rename, hide, and reorder objects.", 9),
        new(AnimationPaneId, "FreePAnimationPane", "Animation Pane", "Review and edit slide animations.", 10),
    ];

    public static IReadOnlyList<PresentationPaneAccessibilityDescriptor> Descriptors => descriptors;

    public static PresentationPaneAccessibilityDescriptor Get(string paneId) =>
        descriptors.First(descriptor => string.Equals(descriptor.PaneId, paneId, StringComparison.Ordinal));

    public static PresentationPaneAccessibilityItemDescriptor Item(
        string paneId,
        int index,
        string name,
        string? state = null,
        string? stableKey = null)
    {
        var descriptor = Get(paneId);
        var key = string.IsNullOrWhiteSpace(stableKey)
            ? (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : stableKey;
        return new(
            $"{descriptor.AutomationId}Item{key}",
            name,
            $"{descriptor.Name} item {key}.",
            index,
            state ?? string.Empty);
    }

    public static PresentationPaneAccessibilityPaneProjection ProjectPane(
        string paneId,
        bool isVisible,
        int itemCount = 0,
        int selectedIndex = -1)
    {
        var descriptor = Get(paneId);
        return new(
            new PresentationPaneAccessibilityState(paneId, isVisible, itemCount, selectedIndex),
            descriptor.AutomationId,
            descriptor.Name,
            descriptor.HelpText,
            FormatPaneStatus(isVisible, descriptor.Order),
            isVisible,
            descriptor.Order + 1);
    }

    public static PresentationPaneAccessibilityItemProjection ProjectItem(
        string paneId,
        int index,
        string name,
        string? state = null,
        string? stableKey = null)
    {
        var item = Item(paneId, index, name, state, stableKey);
        return new(
            item.AutomationId,
            item.Name,
            item.HelpText,
            FormatItemStatus(item));
    }

    public static IReadOnlyList<PresentationPaneAccessibilitySnapshotEntry> BuildSnapshot(
        IEnumerable<PresentationPaneAccessibilityState> states)
    {
        var stateById = states
            .GroupBy(state => state.PaneId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        return descriptors
            .Select(descriptor =>
            {
                stateById.TryGetValue(descriptor.PaneId, out var state);
                return new PresentationPaneAccessibilitySnapshotEntry(
                    descriptor.PaneId,
                    descriptor.AutomationId,
                    descriptor.Name,
                    descriptor.HelpText,
                    state?.IsVisible == true ? "Visible" : "Hidden",
                    descriptor.Order,
                    Math.Max(0, state?.ItemCount ?? 0),
                    state is { SelectedIndex: >= 0 } && state.SelectedIndex < Math.Max(0, state.ItemCount)
                        ? state.SelectedIndex
                        : -1);
            })
            .ToArray();
    }

    /// <summary>
    /// Stable line-oriented representation used by the WPF/Avalonia parity tests.
    /// Keep field order aligned with <see cref="PresentationPaneAccessibilitySnapshotEntry"/>.
    /// </summary>
    public static string SerializeSnapshot(IEnumerable<PresentationPaneAccessibilityState> states) =>
        string.Join(
            Environment.NewLine,
            BuildSnapshot(states).Select(entry =>
                $"{entry.Order:D2}|{entry.PaneId}|{entry.AutomationId}|{entry.Name}|{entry.HelpText}|{entry.State}|{entry.ItemCount}|{entry.SelectedIndex}"));

    private static string FormatPaneStatus(bool isVisible, int order) =>
        $"{(isVisible ? "Visible" : "Hidden")}; Order {order + 1}";

    private static string FormatItemStatus(PresentationPaneAccessibilityItemDescriptor item) =>
        string.IsNullOrWhiteSpace(item.State)
            ? $"Order {item.Order + 1}"
            : $"{item.State}; Order {item.Order + 1}";
}

public sealed record PresentationPaneAccessibilityDescriptor(
    string PaneId,
    string AutomationId,
    string Name,
    string HelpText,
    int Order);

public sealed record PresentationPaneAccessibilityItemDescriptor(
    string AutomationId,
    string Name,
    string HelpText,
    int Order,
    string State);

public sealed record PresentationPaneAccessibilityPaneProjection(
    PresentationPaneAccessibilityState State,
    string AutomationId,
    string Name,
    string HelpText,
    string ItemStatus,
    bool IsKeyboardNavigationEnabled,
    int KeyboardOrder);

public sealed record PresentationPaneAccessibilityItemProjection(
    string AutomationId,
    string Name,
    string HelpText,
    string ItemStatus);

public sealed record PresentationPaneAccessibilityState(
    string PaneId,
    bool IsVisible,
    int ItemCount = 0,
    int SelectedIndex = -1);

public sealed record PresentationPaneAccessibilitySnapshotEntry(
    string PaneId,
    string AutomationId,
    string Name,
    string HelpText,
    string State,
    int Order,
    int ItemCount,
    int SelectedIndex);
