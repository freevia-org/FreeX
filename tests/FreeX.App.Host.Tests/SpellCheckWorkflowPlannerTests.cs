using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class SpellCheckWorkflowPlannerTests
{
    private static SpellingIssue Issue(
        CellAddress address,
        string word,
        string cellText,
        SpellingIssueSource source = SpellingIssueSource.CellText,
        int replyIndex = -1,
        int startIndex = -1) =>
        new(
            address,
            word,
            word.Equals("adn", StringComparison.OrdinalIgnoreCase) ? "and" : "the",
            cellText,
            startIndex,
            startIndex >= 0 ? word.Length : 0,
            source,
            replyIndex);

    private sealed class SimpleCtx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
