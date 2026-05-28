namespace Freexcel.Core.Commands;

internal static class PivotTableCommandCollections
{
    public static void Replace<T>(List<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        target.AddRange(source);
    }
}
