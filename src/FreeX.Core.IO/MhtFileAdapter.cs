using System.Buffers;
using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// MHTML (.mht/.mhtml) "Single File Web Page" adapter — READ + WRITE.
///
/// <para><b>Format overview</b>: MHTML is an RFC 2557 MIME multipart/related message. Excel uses
/// this when the user saves as "Single File Web Page". The file is plain text: MIME headers
/// followed by one or more MIME parts separated by a boundary string. FreeX writes exactly one
/// part containing the HTML document produced by <see cref="HtmlTableWriter"/>.</para>
///
/// <para><b>Transfer encoding</b>: Base64 is chosen over quoted-printable for two reasons:
/// (1) it is simpler to implement correctly — a hand-rolled QP encoder must handle soft-line
/// wraps, escape of '=' and non-ASCII, and CRLF handling, whereas Base64 is a single
/// <see cref="Convert.ToBase64String"/> call; (2) base64 is universally accepted by
/// Excel/browsers for MHTML parts.</para>
///
/// <para><b>Determinism</b>: the boundary string is a compile-time constant so that saving the
/// same workbook twice always produces identical bytes.</para>
/// </summary>
public sealed class MhtFileAdapter : IFileAdapter
{
    // Constant boundary — must not appear inside the HTML body we produce.
    // We prefix with "----=_Part_" to match the style Excel uses and avoid
    // collisions with normal HTML content.
    private const string Boundary = "----=_Part_FreeX_MHT_Boundary_0000";

    public string Extension => ".mht";
    public string FormatName => "Single File Web Page (MHT)";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(".mht", "Single File Web Page (MHT)", CanOpen: true, CanSave: true),
        new FileFormatDescriptor(".mhtml", "Single File Web Page (MHTML)", CanOpen: true, CanSave: true),
    ];

    // ---- Save ------------------------------------------------------------------------------------

    public void Save(Workbook workbook, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(stream);
        SaveStreamPreparer.TruncateFromCurrentPosition(stream);

        // 1. Render HTML into memory via the existing HTML writer.
        using var htmlStream = new MemoryStream();
        HtmlTableWriter.Write(workbook, htmlStream);
        byte[] htmlBytes = htmlStream.ToArray();

        // 2. Base64-encode the HTML body.
        string b64 = Convert.ToBase64String(htmlBytes, Base64FormattingOptions.InsertLineBreaks);

        // 3. Write the MHTML envelope.  We use ASCII for headers and structure bytes; the HTML
        //    payload is already base64 (pure ASCII), so the whole file is safe ASCII.
        using var writer = new StreamWriter(stream, new ASCIIEncoding(), leaveOpen: true)
        {
            NewLine = "\r\n",
        };

        // Outer MIME headers.
        writer.WriteLine("MIME-Version: 1.0");
        writer.WriteLine($"Content-Type: multipart/related; boundary=\"{Boundary}\"; type=\"text/html\"");
        writer.WriteLine("X-MimeOLE: Produced By FreeX");
        writer.WriteLine(); // blank line separating outer headers from body

        // First (and only) part: the HTML document.
        writer.WriteLine($"--{Boundary}");
        writer.WriteLine("Content-Type: text/html; charset=\"utf-8\"");
        writer.WriteLine("Content-Transfer-Encoding: base64");
        writer.WriteLine("Content-Location: file:///C:/FreeX/index.html");
        writer.WriteLine(); // blank line separating part headers from part body
        writer.WriteLine(b64);
        writer.WriteLine(); // blank line after encoded data (per RFC 2046)

        // Closing boundary.
        writer.WriteLine($"--{Boundary}--");

        writer.Flush();
    }

    // ---- Load ------------------------------------------------------------------------------------

    public Workbook Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        string mhtml = HtmlText.ReadAll(stream);

        // Attempt to decode as MHTML; fall back to treating the whole content as HTML so
        // that a plain HTML file saved with a .mht extension still opens.
        string html = ExtractHtmlPart(mhtml) ?? mhtml;

        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");
        HtmlTableReader.Populate(html, workbook, sheet);
        return workbook;
    }

    // ---- MHTML parsing ---------------------------------------------------------------------------

    /// <summary>
    /// Locate the <c>text/html</c> part in an MHTML document, decode its transfer encoding,
    /// and return the HTML string.  Returns <see langword="null"/> when the content does not
    /// look like a MIME multipart document (allowing the caller to treat it as raw HTML).
    /// </summary>
    private static string? ExtractHtmlPart(string mhtml)
    {
        // Quick sanity: must have MIME-Version header.
        if (!ContainsCaseInsensitive(mhtml, "MIME-Version"))
            return null;

        // Find the boundary string from the outer Content-Type header.
        string? boundary = FindBoundaryParam(mhtml);
        if (boundary is null)
            return null;

        string delimiter = "--" + boundary;

        // Walk through MIME parts.
        int pos = 0;
        while (true)
        {
            // Find the next part delimiter.
            int delimIdx = IndexOfOrdinalIgnoreCase(mhtml, delimiter, pos);
            if (delimIdx < 0)
                break;

            int afterDelim = delimIdx + delimiter.Length;

            // A closing boundary is "--<boundary>--"; skip it.
            if (afterDelim < mhtml.Length && mhtml[afterDelim] == '-')
                break;

            // Skip to end of delimiter line.
            int lineEnd = mhtml.IndexOf('\n', afterDelim);
            if (lineEnd < 0)
                break;
            int headersStart = lineEnd + 1;

            // Find the blank line that separates part headers from part body.
            int bodyStart = FindBlankLine(mhtml, headersStart);
            if (bodyStart < 0)
                break;

            string partHeaders = mhtml.Substring(headersStart, bodyStart - headersStart);

            // Find the end of this part: the next boundary.
            int nextDelim = IndexOfOrdinalIgnoreCase(mhtml, delimiter, bodyStart);
            int bodyEnd = nextDelim < 0 ? mhtml.Length : nextDelim;

            // Trim the trailing CRLF/LF before the boundary.
            while (bodyEnd > bodyStart && (mhtml[bodyEnd - 1] == '\r' || mhtml[bodyEnd - 1] == '\n'))
                bodyEnd--;

            string partBody = mhtml.Substring(bodyStart, bodyEnd - bodyStart);

            // Only process text/html parts.
            if (ContainsCaseInsensitive(partHeaders, "text/html"))
            {
                string encoding = GetHeaderValue(partHeaders, "Content-Transfer-Encoding");
                if (string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
                {
                    // Remove whitespace (line breaks) from base64 payload before decoding.
                    string b64 = RemoveWhitespace(partBody);
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(b64);
                        return Encoding.UTF8.GetString(bytes);
                    }
                    catch (FormatException)
                    {
                        // Corrupt base64 — fall through to return raw body.
                        return partBody;
                    }
                }

                if (string.Equals(encoding, "quoted-printable", StringComparison.OrdinalIgnoreCase))
                    return DecodeQuotedPrintable(partBody);

                // No or unknown transfer encoding — treat body as plain text/HTML.
                return partBody;
            }

            pos = afterDelim;
        }

        return null;
    }

    // ---- MHTML parsing helpers -------------------------------------------------------------------

    /// <summary>Find the value of the <c>boundary=</c> parameter in the outer Content-Type header.</summary>
    private static string? FindBoundaryParam(string text)
    {
        // Look for "boundary=" (case-insensitive) within the first 2048 characters.
        int searchLen = Math.Min(text.Length, 2048);
        int idx = IndexOfOrdinalIgnoreCase(text.AsSpan(0, searchLen).ToString(), "boundary=", 0);
        if (idx < 0)
            return null;

        int valueStart = idx + "boundary=".Length;
        if (valueStart >= text.Length)
            return null;

        char first = text[valueStart];
        if (first is '"' or '\'')
        {
            int end = text.IndexOf(first, valueStart + 1);
            if (end < 0)
                return null;
            return text.Substring(valueStart + 1, end - valueStart - 1);
        }

        // Unquoted boundary — ends at whitespace, semicolon, CR, or LF.
        int e = valueStart;
        while (e < text.Length && text[e] != ' ' && text[e] != '\t' &&
               text[e] != '\r' && text[e] != '\n' && text[e] != ';')
            e++;
        return e > valueStart ? text.Substring(valueStart, e - valueStart) : null;
    }

    /// <summary>
    /// Find the start of the first blank line (CRLF CRLF or LF LF or mixed) after
    /// <paramref name="from"/> and return the index of the character after it.
    /// Returns -1 when no blank line is found.
    /// </summary>
    private static int FindBlankLine(string text, int from)
    {
        int i = from;
        while (i < text.Length)
        {
            // Find next newline.
            int nl = text.IndexOf('\n', i);
            if (nl < 0)
                return -1;
            int afterNl = nl + 1;

            // The blank-line pattern: the next line is empty (starts immediately with \r\n or \n).
            if (afterNl < text.Length && text[afterNl] == '\n')
                return afterNl + 1;
            if (afterNl + 1 < text.Length && text[afterNl] == '\r' && text[afterNl + 1] == '\n')
                return afterNl + 2;

            i = afterNl;
        }
        return -1;
    }

    /// <summary>
    /// Extract the trimmed value of a header line such as
    /// <c>Content-Transfer-Encoding: base64</c>.  Returns empty string when not found.
    /// </summary>
    private static string GetHeaderValue(string headers, string headerName)
    {
        int idx = IndexOfOrdinalIgnoreCase(headers, headerName, 0);
        if (idx < 0)
            return "";

        int colon = headers.IndexOf(':', idx + headerName.Length);
        if (colon < 0)
            return "";

        int lineEnd = headers.IndexOf('\n', colon + 1);
        string value = lineEnd < 0
            ? headers.Substring(colon + 1)
            : headers.Substring(colon + 1, lineEnd - colon - 1);
        return value.Trim().TrimEnd('\r');
    }

    private static bool ContainsCaseInsensitive(string haystack, string needle) =>
        haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static int IndexOfOrdinalIgnoreCase(string haystack, string needle, int startIndex) =>
        haystack.IndexOf(needle, startIndex, StringComparison.OrdinalIgnoreCase);

    private static string RemoveWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            if (c != ' ' && c != '\t' && c != '\r' && c != '\n')
                sb.Append(c);
        return sb.ToString();
    }

    /// <summary>
    /// Minimal quoted-printable decoder: handles soft line breaks (<c>=\r\n</c> / <c>=\n</c>)
    /// and hex escapes (<c>=XX</c>).
    /// </summary>
    private static string DecodeQuotedPrintable(string body)
    {
        // Work on raw bytes for correctness, then interpret as UTF-8. Literal text is encoded in
        // contiguous spans so tolerant raw non-ASCII input does not allocate a char[] + byte[] for
        // every UTF-16 code unit (and surrogate pairs remain intact).
        var bytes = new ArrayBufferWriter<byte>(Math.Max(1, body.Length));
        int i = 0;
        while (i < body.Length)
        {
            if (body[i] != '=')
            {
                var literalStart = i;
                do
                {
                    i++;
                }
                while (i < body.Length && body[i] != '=');

                AppendUtf8(bytes, body.AsSpan(literalStart, i - literalStart));
                continue;
            }

            if (i + 1 < body.Length && (body[i + 1] == '\r' || body[i + 1] == '\n'))
            {
                // Soft line break — skip the '=' and the newline(s).
                i++;
                if (i < body.Length && body[i] == '\r') i++;
                if (i < body.Length && body[i] == '\n') i++;
                continue;
            }

            if (i + 2 < body.Length &&
                byte.TryParse(
                    body.AsSpan(i + 1, 2),
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
            {
                AppendByte(bytes, value);
                i += 3;
                continue;
            }

            // Malformed '=' — emit literally.
            AppendByte(bytes, (byte)'=');
            i++;
        }

        return Encoding.UTF8.GetString(bytes.WrittenSpan);
    }

    private static void AppendUtf8(ArrayBufferWriter<byte> destination, ReadOnlySpan<char> text)
    {
        var target = destination.GetSpan(Encoding.UTF8.GetMaxByteCount(text.Length));
        destination.Advance(Encoding.UTF8.GetBytes(text, target));
    }

    private static void AppendByte(ArrayBufferWriter<byte> destination, byte value)
    {
        destination.GetSpan(1)[0] = value;
        destination.Advance(1);
    }
}
