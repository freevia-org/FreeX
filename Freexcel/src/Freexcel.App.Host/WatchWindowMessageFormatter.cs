namespace Freexcel.App.Host;

public static class WatchWindowMessageFormatter
{
    public static string FormatAddResult(int added, string rangeText) =>
        added > 0
            ? $"{added} cell{(added == 1 ? "" : "s")} added to Watch Window."
            : $"{rangeText} is already watched.";

    public static string FormatRemoveResult(int removed, string rangeText) =>
        removed > 0
            ? $"{removed} cell{(removed == 1 ? "" : "s")} removed from Watch Window."
            : $"{rangeText} is not watched.";
}
