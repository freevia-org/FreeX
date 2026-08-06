using System.Diagnostics.CodeAnalysis;
using System.Text;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// Converts platform-retrieved RTF clipboard text into the portable document model. Clipboard access
/// remains renderer-owned; this parser only handles the shared payload boundary.
/// </summary>
public static class RtfClipboardDocumentParser
{
    public static bool TryParse(
        string? rtf,
        [NotNullWhen(true)] out TextDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(rtf))
            return false;

        try
        {
            // RTF control syntax is ASCII. Latin-1 preserves every supplied code unit so RtfReader can
            // apply the payload's own code-page declarations to escaped source bytes.
            using var stream = new MemoryStream(Encoding.Latin1.GetBytes(rtf));
            var parsed = RtfReader.Read(stream);
            if (parsed.Blocks.Count == 0)
                return false;

            document = parsed;
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException
            or ArgumentException
            or FormatException
            or OverflowException
            or DecoderFallbackException)
        {
            return false;
        }
    }
}
