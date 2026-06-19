namespace FreeW.Core.IO;

/// <summary>
/// Per-save knobs for <see cref="DocxWriter"/> that differ across the WordprocessingML package variants
/// (<c>.docx</c>/<c>.docm</c>/<c>.dotx</c>/<c>.dotm</c>): the main document part's content type and whether
/// macro parts are re-emitted. The document body is identical across all four — only the package framing
/// changes — so the variants are pure data over the one engine.
/// </summary>
public sealed class DocxWriteOptions
{
    /// <summary>The <c>[Content_Types].xml</c> Override content type for <c>/word/document.xml</c>.</summary>
    public required string MainDocumentContentType { get; init; }

    /// <summary>
    /// Whether preserved macro parts (<c>word/vbaProject.bin</c> and friends) are re-emitted. Only the
    /// macro-enabled targets (<c>.docm</c>/<c>.dotm</c>) keep them; a <c>.docx</c>/<c>.dotx</c> must not.
    /// </summary>
    public bool IncludeMacroParts { get; init; }

    public const string DocxMainContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
    public const string DocmMainContentType =
        "application/vnd.ms-word.document.macroEnabled.main+xml";
    public const string DotxMainContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml";
    public const string DotmMainContentType =
        "application/vnd.ms-word.template.macroEnabled.main+xml";

    /// <summary>Word Document — the default; macro parts dropped.</summary>
    public static DocxWriteOptions Docx { get; } =
        new() { MainDocumentContentType = DocxMainContentType, IncludeMacroParts = false };

    /// <summary>Word Macro-Enabled Document — macro parts re-emitted.</summary>
    public static DocxWriteOptions Docm { get; } =
        new() { MainDocumentContentType = DocmMainContentType, IncludeMacroParts = true };

    /// <summary>Word Template — macro parts dropped.</summary>
    public static DocxWriteOptions Dotx { get; } =
        new() { MainDocumentContentType = DotxMainContentType, IncludeMacroParts = false };

    /// <summary>Word Macro-Enabled Template — macro parts re-emitted.</summary>
    public static DocxWriteOptions Dotm { get; } =
        new() { MainDocumentContentType = DotmMainContentType, IncludeMacroParts = true };

    /// <summary>The macro parts that only macro-enabled variants may carry.</summary>
    internal static bool IsMacroPart(string partName) =>
        partName.Equals("/word/vbaProject.bin", StringComparison.OrdinalIgnoreCase)
        || partName.Equals("/word/vbaData.xml", StringComparison.OrdinalIgnoreCase)
        || partName.Equals("/word/_rels/vbaProject.bin.rels", StringComparison.OrdinalIgnoreCase);
}
