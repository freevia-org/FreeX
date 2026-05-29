namespace FreeX.App.Host;

public static class WatchWindowMessageFormatter
{
    public static string FormatAddResult(int added, string rangeText) =>
        added > 0
            ? $"{FormatCellCount(added)} added to Watch Window."
            : $"{rangeText} is already watched.";

    public static string FormatRemoveResult(int removed, string rangeText) =>
        removed > 0
            ? $"{FormatCellCount(removed)} removed from Watch Window."
            : $"{rangeText} is not watched.";

    private static string FormatCellCount(int count) =>
        $"{count} cell{(count == 1 ? "" : "s")}";
}
