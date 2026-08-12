using System.Text;
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
    public static bool TryReadRtf(string? rtf, out TextDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(rtf))
            return false;

        try
        {
            using var stream = new MemoryStream(Encoding.Latin1.GetBytes(rtf));
            var parsed = RtfReader.Read(stream);
            if (parsed.Blocks.Count == 0)
                return false;

            document = parsed;
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
