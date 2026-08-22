using Free.Shared.Commands;

namespace FreeX.App.Presentation.Shell;

/// <summary>
/// Projects the command bus's in-memory undo/redo labels for a read-only Review surface.
/// This is intentionally a session view, not a persisted revision-history or collaboration model.
/// </summary>
public static class SessionChangesPlanner
{
    public const string Title = "Changes in this session";
    public const string ScopeMessage =
        "Shows local actions recorded while this workbook is open. This is not a saved revision history and does not include collaborators.";
    public const string UndoSectionTitle = "Can undo";
    public const string RedoSectionTitle = "Can redo";
    public const string EmptySectionMessage = "No changes in this session.";
    public const int MaxEntries = 100;

    public static SessionChangesPlan Create(
        IReadOnlyList<CommandHistoryEntry> undoHistory,
        IReadOnlyList<CommandHistoryEntry> redoHistory)
    {
        ArgumentNullException.ThrowIfNull(undoHistory);
        ArgumentNullException.ThrowIfNull(redoHistory);

        return new SessionChangesPlan(
            undoHistory.Select(entry => entry.Label).ToArray(),
            redoHistory.Select(entry => entry.Label).ToArray());
    }
}

/// <summary>Read-only labels currently retained by the local command stack.</summary>
public sealed record SessionChangesPlan(
    IReadOnlyList<string> UndoEntries,
    IReadOnlyList<string> RedoEntries)
{
    public bool IsEmpty => UndoEntries.Count == 0 && RedoEntries.Count == 0;
}
