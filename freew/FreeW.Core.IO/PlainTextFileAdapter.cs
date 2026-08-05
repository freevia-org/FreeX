using System.IO;
using System.Text;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// Reads and writes plain text (<c>.txt</c>/<c>.text</c>/<c>.log</c>). Each line becomes a
/// <see cref="Paragraph"/> on read; each paragraph's text is written back joined by the configured
/// line-ending on save. Writing is intentionally lossy — only characters and paragraph breaks survive, as
/// with Word's own plain-text export. Reading honors UTF-8/UTF-16/UTF-32 byte-order marks, accepts
/// valid bomless UTF-8, and falls back to Windows-1252 for invalid bomless UTF-8.
/// </summary>
public sealed class PlainTextFileAdapter(TextSaveOptions? options = null) : IDocumentFileAdapter
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Encoding Windows1252 = CreateWindows1252();
    private readonly TextSaveOptions _options = options ?? TextSaveOptions.Default;

    public string Extension => ".txt";
    public string FormatName => "Plain text";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new(".txt", "Plain text"),
        new(".text", "Plain text"),
        new(".log", "Log file"),
    ];

    public TextDocument Load(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        var bytes = copy.ToArray();
        var text = Decode(bytes);

        var document = new TextDocument();
        document.Blocks.Clear();
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
            document.Blocks.Add(new Paragraph(line));
        return document;
    }

    private static string Decode(byte[] bytes)
    {
        if (HasUnicodeBom(bytes))
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(false, false),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 1024,
                leaveOpen: false);
            return reader.ReadToEnd();
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Windows1252.GetString(bytes);
        }
    }

    private static bool HasUnicodeBom(ReadOnlySpan<byte> bytes) =>
        (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        || (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        || (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        || (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        || (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF);

    private static Encoding CreateWindows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(
            1252,
            EncoderFallback.ReplacementFallback,
            DecoderFallback.ReplacementFallback);
    }

    public void Save(TextDocument document, Stream stream)
    {
        var newline = _options.Eol switch
        {
            EolStyle.Lf => "\n",
            EolStyle.Cr => "\r",
            _ => "\r\n",
        };
        var encoding = _options.EmitBom ? new UTF8Encoding(true) : _options.Encoding;

        using var writer = new StreamWriter(stream, encoding, bufferSize: 1024, leaveOpen: true) { NewLine = newline };
        var first = true;
        foreach (var block in document.Blocks)
        {
            if (block is not Paragraph paragraph)
                continue; // tables and other non-paragraph blocks are dropped (plain text has no place for them)
            if (!first)
                writer.Write(newline);
            writer.Write(paragraph.PlainText);
            first = false;
        }
    }
}
