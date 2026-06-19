using System.IO;
using System.Text;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// Reads and writes plain text (<c>.txt</c>/<c>.text</c>/<c>.log</c>). Each line becomes a
/// <see cref="Paragraph"/> on read; each paragraph's text is written back joined by the configured
/// line-ending on save. Writing is intentionally lossy — only characters and paragraph breaks survive, as
/// with Word's own plain-text export. Reading detects UTF-8/UTF-16/UTF-32 from a byte-order mark and
/// otherwise decodes as UTF-8 (an ANSI/legacy-codepage chooser is a planned follow-up).
/// </summary>
public sealed class PlainTextFileAdapter(TextSaveOptions? options = null) : IDocumentFileAdapter
{
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
        // BOM sniff handles UTF-8/16/32; absent a BOM, decode as UTF-8 with replacement rather than throwing
        // on invalid bytes (a binary file opened by mistake degrades to replacement chars, not a crash).
        var fallback = new UTF8Encoding(false, false);
        using var reader = new StreamReader(stream, fallback, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        var text = reader.ReadToEnd();

        var document = new TextDocument();
        document.Blocks.Clear();
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
            document.Blocks.Add(new Paragraph(line));
        return document;
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
