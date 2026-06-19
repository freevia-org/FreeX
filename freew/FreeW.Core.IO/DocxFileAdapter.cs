using System.IO;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// The native WordprocessingML adapter, covering the OOXML package family: <c>.docx</c>, the macro-enabled
/// <c>.docm</c>, and the templates <c>.dotx</c>/<c>.dotm</c>. All four share one document body — only the
/// package framing (the <c>document.xml</c> content type and whether macro parts are kept) differs — so they
/// are pure data over the existing static <see cref="DocxReader"/>/<see cref="DocxWriter"/> engine. Reading is
/// variant-agnostic (it keys on <c>word/document.xml</c>); writing selects the right
/// <see cref="DocxWriteOptions"/> per variant. One instance is registered per extension.
/// </summary>
public sealed class DocxFileAdapter : IDocumentFileAdapter
{
    private readonly DocxWriteOptions _writeOptions;
    private readonly bool _opensAsTemplate;

    public string Extension { get; }
    public string FormatName { get; }

    private DocxFileAdapter(string extension, string formatName, DocxWriteOptions writeOptions, bool opensAsTemplate)
    {
        Extension = extension;
        FormatName = formatName;
        _writeOptions = writeOptions;
        _opensAsTemplate = opensAsTemplate;
    }

    /// <summary>The plain <c>.docx</c> Word Document (default).</summary>
    public DocxFileAdapter() : this(".docx", "Word Document", DocxWriteOptions.Docx, opensAsTemplate: false) { }

    public static DocxFileAdapter Docx() => new(".docx", "Word Document", DocxWriteOptions.Docx, opensAsTemplate: false);
    public static DocxFileAdapter Docm() => new(".docm", "Word Macro-Enabled Document", DocxWriteOptions.Docm, opensAsTemplate: false);
    public static DocxFileAdapter Dotx() => new(".dotx", "Word Template", DocxWriteOptions.Dotx, opensAsTemplate: true);
    public static DocxFileAdapter Dotm() => new(".dotm", "Word Macro-Enabled Template", DocxWriteOptions.Dotm, opensAsTemplate: true);

    public IReadOnlyList<FileFormatDescriptor> Formats =>
        [new FileFormatDescriptor(Extension, FormatName, CanOpen: true, CanSave: true, OpensAsTemplate: _opensAsTemplate)];

    public TextDocument Load(Stream stream) => DocxReader.Read(stream);

    public void Save(TextDocument document, Stream stream) => DocxWriter.Write(document, stream, _writeOptions);
}
