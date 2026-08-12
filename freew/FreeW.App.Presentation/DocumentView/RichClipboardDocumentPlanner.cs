using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Converts platform clipboard RTF text into the portable document model. Clipboard adapters own
/// data-object access; this planner owns the shared byte-preserving parse and rejection policy.
/// </summary>
public static class RichClipboardDocumentPlanner
{
    /// <summary>
    /// Parses RTF clipboard text. RTF control syntax is ASCII while source text can be code-page
    /// encoded, so Latin-1 preserves every supplied code unit for <see cref="RtfReader"/>.
    /// </summary>
    public static bool TryReadRtf(string? rtf, out TextDocument? document) =>
        RtfClipboardDocumentParser.TryParse(rtf, out document);
}
