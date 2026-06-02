using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    internal static void ShiftCommentRowsUp<TValue>(Dictionary<CellAddress, TValue> comments, uint start, uint count)
    {
        List<KeyValuePair<CellAddress, TValue>>? shifted = null;
        foreach (var pair in comments)
        {
            if (pair.Key.Row >= start)
                (shifted ??= new List<KeyValuePair<CellAddress, TValue>>(comments.Count)).Add(pair);
        }

        if (shifted is null)
            return;

        foreach (var (addr, _) in shifted)
            comments.Remove(addr);
        foreach (var (addr, comment) in shifted)
            comments[new CellAddress(addr.Sheet, addr.Row + count, addr.Col)] = comment;
    }

    internal static void ShiftCommentRowsDown<TValue>(Dictionary<CellAddress, TValue> comments, uint start, uint count)
    {
        var end = start + count - 1;
        List<CellAddress>? removed = null;
        List<KeyValuePair<CellAddress, TValue>>? shifted = null;
        foreach (var pair in comments)
        {
            if (pair.Key.Row > end)
                (shifted ??= new List<KeyValuePair<CellAddress, TValue>>(comments.Count)).Add(pair);
            else if (pair.Key.Row >= start)
                (removed ??= []).Add(pair.Key);
        }

        if (removed is not null)
        {
            foreach (var addr in removed)
                comments.Remove(addr);
        }
        if (shifted is not null)
        {
            foreach (var (addr, _) in shifted)
                comments.Remove(addr);
            foreach (var (addr, comment) in shifted)
                comments[new CellAddress(addr.Sheet, addr.Row - count, addr.Col)] = comment;
        }
    }

    internal static void ShiftCommentColumnsUp<TValue>(Dictionary<CellAddress, TValue> comments, uint start, uint count)
    {
        List<KeyValuePair<CellAddress, TValue>>? shifted = null;
        foreach (var pair in comments)
        {
            if (pair.Key.Col >= start)
                (shifted ??= new List<KeyValuePair<CellAddress, TValue>>(comments.Count)).Add(pair);
        }

        if (shifted is null)
            return;

        foreach (var (addr, _) in shifted)
            comments.Remove(addr);
        foreach (var (addr, comment) in shifted)
            comments[new CellAddress(addr.Sheet, addr.Row, addr.Col + count)] = comment;
    }

    internal static void ShiftCommentColumnsDown<TValue>(Dictionary<CellAddress, TValue> comments, uint start, uint count)
    {
        var end = start + count - 1;
        List<CellAddress>? removed = null;
        List<KeyValuePair<CellAddress, TValue>>? shifted = null;
        foreach (var pair in comments)
        {
            if (pair.Key.Col > end)
                (shifted ??= new List<KeyValuePair<CellAddress, TValue>>(comments.Count)).Add(pair);
            else if (pair.Key.Col >= start)
                (removed ??= []).Add(pair.Key);
        }

        if (removed is not null)
        {
            foreach (var addr in removed)
                comments.Remove(addr);
        }
        if (shifted is not null)
        {
            foreach (var (addr, _) in shifted)
                comments.Remove(addr);
            foreach (var (addr, comment) in shifted)
                comments[new CellAddress(addr.Sheet, addr.Row, addr.Col - count)] = comment;
        }
    }
}
