using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    internal static void ShiftCommentRowsUp<TValue>(Dictionary<CellAddress, TValue> comments, uint start, uint count)
    {
        var shifted = comments
            .Where(p => p.Key.Row >= start)
            .ToList();

        foreach (var (addr, _) in shifted)
            comments.Remove(addr);
        foreach (var (addr, comment) in shifted)
            comments[new CellAddress(addr.Sheet, addr.Row + count, addr.Col)] = comment;
    }

    internal static void ShiftCommentRowsDown<TValue>(Dictionary<CellAddress, TValue> comments, uint start, uint count)
    {
        var end = start + count - 1;
        var removed = comments.Keys.Where(addr => addr.Row >= start && addr.Row <= end).ToList();
        var shifted = comments
            .Where(p => p.Key.Row > end)
            .ToList();

        foreach (var addr in removed)
            comments.Remove(addr);
        foreach (var (addr, _) in shifted)
            comments.Remove(addr);
        foreach (var (addr, comment) in shifted)
            comments[new CellAddress(addr.Sheet, addr.Row - count, addr.Col)] = comment;
    }

    internal static void ShiftCommentColumnsUp<TValue>(Dictionary<CellAddress, TValue> comments, uint start, uint count)
    {
        var shifted = comments
            .Where(p => p.Key.Col >= start)
            .ToList();

        foreach (var (addr, _) in shifted)
            comments.Remove(addr);
        foreach (var (addr, comment) in shifted)
            comments[new CellAddress(addr.Sheet, addr.Row, addr.Col + count)] = comment;
    }

    internal static void ShiftCommentColumnsDown<TValue>(Dictionary<CellAddress, TValue> comments, uint start, uint count)
    {
        var end = start + count - 1;
        var removed = comments.Keys.Where(addr => addr.Col >= start && addr.Col <= end).ToList();
        var shifted = comments
            .Where(p => p.Key.Col > end)
            .ToList();

        foreach (var addr in removed)
            comments.Remove(addr);
        foreach (var (addr, _) in shifted)
            comments.Remove(addr);
        foreach (var (addr, comment) in shifted)
            comments[new CellAddress(addr.Sheet, addr.Row, addr.Col - count)] = comment;
    }
}
