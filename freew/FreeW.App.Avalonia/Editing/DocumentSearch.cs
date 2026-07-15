using FreeW.Core.Model;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// Compatibility adapter for the Avalonia editor's existing search tests. The option-aware matching
/// policy is owned by <see cref="FindReplaceDialogPlanner"/>.
/// </summary>
internal static class DocumentSearch
{
    internal readonly record struct Match(int Block, int Start, int Length);

    public static Match? FindNext(TextDocument document, string query, int fromBlock, int fromOffset)
    {
        var match = FindReplaceDialogPlanner.FindNextMatch(
            document,
            query,
            new FindReplaceSearchOptions(),
            fromBlock,
            fromOffset);
        return match is { } value ? new Match(value.Block, value.Start, value.Length) : null;
    }
}
