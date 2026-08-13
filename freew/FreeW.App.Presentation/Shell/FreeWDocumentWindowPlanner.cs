using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Shell;

/// <summary>
/// Immutable hand-off from the shared document tier to a renderer that is opening another
/// top-level view of the current document.
/// </summary>
public sealed record FreeWDocumentWindowPlan(
    TextDocument Document,
    string? CurrentPath,
    bool IsDirty,
    int WindowNumber)
{
    public string WindowSuffix => FreeWDocumentWindowPlanner.FormatWindowSuffix(WindowNumber);
}

/// <summary>
/// Owns the non-visual semantics of View &gt; New Window: every renderer receives an independent
/// in-memory snapshot of the live document, the same save target and dirty state, and a stable,
/// monotonically increasing window number. Renderers only construct a native window and load the plan.
/// </summary>
public sealed class FreeWDocumentWindowPlanner
{
    private int _lastWindowNumber = 1;

    public FreeWDocumentWindowPlan CreateNext(
        TextDocument document,
        string? currentPath,
        bool isDirty)
    {
        ArgumentNullException.ThrowIfNull(document);

        using var buffer = new MemoryStream();
        DocxWriter.Write(document, buffer);
        buffer.Position = 0;
        var snapshot = DocxReader.Read(buffer);

        var windowNumber = Interlocked.Increment(ref _lastWindowNumber);
        return new FreeWDocumentWindowPlan(
            snapshot,
            string.IsNullOrWhiteSpace(currentPath) ? null : currentPath,
            isDirty,
            windowNumber);
    }

    public static string FormatWindowSuffix(int windowNumber)
    {
        if (windowNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(windowNumber));

        return windowNumber > 1 ? $" : {windowNumber}" : string.Empty;
    }
}
