using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Immutable hand-off for an independently editable FreeP document window.</summary>
public sealed record PresentationDocumentWindowPlan(
    Presentation Presentation,
    string? CurrentPath,
    bool IsDirty,
    int WindowNumber)
{
    public string WindowSuffix => PresentationDocumentWindowPlanner.FormatWindowSuffix(WindowNumber);
}

/// <summary>
/// Owns View &gt; New Window snapshot semantics. The package round trip deliberately avoids sharing
/// a mutable <see cref="Presentation"/> between native windows while retaining the same file
/// target and dirty state for normal Save behavior.
/// </summary>
public sealed class PresentationDocumentWindowPlanner
{
    private int _lastWindowNumber = 1;

    public PresentationDocumentWindowPlan CreateNext(
        Presentation presentation,
        string? currentPath,
        bool isDirty)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        using var buffer = new MemoryStream();
        PptxPackageWriter.Write(presentation, buffer);
        buffer.Position = 0;
        var snapshot = PptxPackageReader.Read(buffer);
        var windowNumber = Interlocked.Increment(ref _lastWindowNumber);
        return new PresentationDocumentWindowPlan(
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

    /// <summary>
    /// The title suffix shown while the current presentation came from a file that cannot be
    /// written back to, matching FreeW's <c>FreeWDocumentWindowPlanner.ReadOnlySuffix</c> and
    /// FreeX's <c>WorkbookTitleFormatter</c> marker so the sister apps read the same. Composed
    /// after the window suffix, in the title policy's group-suffix slot.
    /// </summary>
    public const string ReadOnlySuffix = " [Read-Only]";

    public static string FormatReadOnlySuffix(bool isReadOnly) =>
        isReadOnly ? ReadOnlySuffix : string.Empty;
}
