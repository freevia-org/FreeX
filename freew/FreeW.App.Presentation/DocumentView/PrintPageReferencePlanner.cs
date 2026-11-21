using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Display-only recalculation of <c>PAGEREF</c> results for the paged/print render path. The model keeps
/// the cached result Word last wrote (so editing and DOCX round-trip are untouched), but Word itself
/// re-evaluates every PAGEREF -- most visibly the page numbers of a Table of Contents -- against the
/// pagination it just laid out when it prints or exports. FreeW's print pagination knows each top-level
/// block's physical page, so the printed number is derived from it with the same section restarts,
/// formats and chapter prefixes the other generated page references use.
/// </summary>
public static class PrintPageReferencePlanner
{
    /// <summary>Prefix of the automation id a print-clone run carries to mark itself as a refreshable PAGEREF.</summary>
    public const string RunMarkerPrefix = "freew-pageref:";

    /// <summary>
    /// Builds the resolver for one laid-out document. <paramref name="zeroBasedPageOfBlock"/> has one entry
    /// per top-level model block (the page index the paginator put its start on; negative = unknown).
    /// The returned function maps a PAGEREF instruction and its cached result to the text to display.
    /// </summary>
    public static Func<string, string, string> Build(
        TextDocument document,
        IReadOnlyList<int> zeroBasedPageOfBlock,
        int pageCount)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(zeroBasedPageOfBlock);

        var resolution = DocumentReferenceBlockPageResolverPlanner.Build(
            document,
            blockIndex => blockIndex >= 0 && blockIndex < zeroBasedPageOfBlock.Count && zeroBasedPageOfBlock[blockIndex] >= 0
                ? zeroBasedPageOfBlock[blockIndex] + 1
                : null,
            pageCount,
            allowUnobservedFirstPageFallback: false);
        var pageOf = resolution.PageNumberAtBlock;
        var pageTextOf = pageOf is null ? null : PageNumberFormatDialogPlanner.BuildBlockPageReferenceResolver(document, pageOf);
        return (instruction, cached) =>
            ComplexFieldEngine.ResolvePageRefDisplay(document, instruction, cached, pageOf, pageTextOf);
    }

    /// <summary>The marker a rendered PAGEREF run carries, or null when the field must keep its cached text.</summary>
    public static string? MarkerFor(ComplexField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Keyword == "PAGEREF" && !field.IsLocked && !field.ShowCode
            ? RunMarkerPrefix + field.Instruction
            : null;
    }

    /// <summary>The instruction a <see cref="MarkerFor"/> marker carries, or null when it is not one.</summary>
    public static string? InstructionOf(string? marker) =>
        marker is not null && marker.StartsWith(RunMarkerPrefix, StringComparison.Ordinal)
            ? marker[RunMarkerPrefix.Length..]
            : null;
}
