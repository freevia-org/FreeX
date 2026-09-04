using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// r292: decides whether a save is about to discard worksheets, and says so in the same warning
/// channel the XLSX per-item warnings already use.
///
/// <para>r291 measured the behaviour: saving a three-sheet workbook as CSV, PRN, SLK, DIF or HTML
/// returns one sheet. The loss is inherent to those formats, but Excel warns first and FreeX did
/// not -- the other sheets were simply absent the next time the file was opened.</para>
///
/// <para>Split out as a planner rather than written inline at the save chokepoint so the decision
/// can be tested directly, without a file system, a stream or a shell.</para>
/// </summary>
public static class SingleSheetSaveWarningPlanner
{
    /// <summary>
    /// The warning for a save that will keep only the first sheet, or <see langword="null"/> when
    /// nothing is lost -- the adapter can hold every sheet, or there is only one to hold.
    /// </summary>
    public static string? DescribeDiscardedSheets(IFileAdapter adapter, Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(workbook);

        if (adapter is not ISingleSheetFileAdapter)
            return null;

        var sheets = workbook.Sheets;
        if (sheets.Count <= 1)
            return null;

        var kept = sheets[0].Name;
        var discarded = sheets.Skip(1).Select(sheet => sheet.Name).ToArray();

        // Named rather than counted: "2 sheets were not saved" leaves the user to work out WHICH,
        // and the whole point of the warning is that they can still act on it before closing.
        return $"{adapter.FormatName} stores a single worksheet. Only \"{kept}\" was saved; "
            + $"{FormatSheetList(discarded)} {(discarded.Length == 1 ? "was" : "were")} not.";
    }

    private static string FormatSheetList(IReadOnlyList<string> names) =>
        names.Count switch
        {
            1 => $"\"{names[0]}\"",
            2 => $"\"{names[0]}\" and \"{names[1]}\"",
            _ => string.Join(", ", names.Take(names.Count - 1).Select(name => $"\"{name}\""))
                + $" and \"{names[^1]}\"",
        };
}
