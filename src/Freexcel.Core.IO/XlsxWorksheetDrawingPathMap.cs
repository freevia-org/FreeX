namespace Freexcel.Core.IO;

internal sealed record XlsxWorksheetDrawingPathMap(
    IReadOnlyDictionary<string, string> SourceDrawingPaths,
    IReadOnlyDictionary<string, string> TargetDrawingPaths)
{
    public static XlsxWorksheetDrawingPathMap Empty { get; } = new(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}
