using System.IO;
using DocSharp.Binary.DocFileFormat;
using DocSharp.Binary.StructuredStorage.Reader;
using FreeW.Core.Model;
using DocSharpWordprocessingDocument = DocSharp.Binary.OpenXmlLib.WordprocessingML.WordprocessingDocument;

namespace FreeW.Core.IO;

/// <summary>
/// Read-only import of legacy Word 97-2003 binary documents (<c>.doc</c>/<c>.dot</c>) — design §5.5. The
/// binary OLE2/CFB format is transcoded to an in-memory <c>.docx</c> with DocSharp.Binary.Doc (an MIT fork of
/// b2xtranslator) and then read by the existing <see cref="DocxReader"/>, so all of FreeW's WordprocessingML
/// mapping is reused for free. Open-only (<see cref="FileFormatDescriptor.CanSave"/> is false); users
/// round-trip out via <em>Save As .docx</em>. Pre-97 Word 6.0/95 binaries are a different format and fail
/// with a clear message. Mirrors the sibling app's read-only legacy-binary adapter.
/// </summary>
public sealed class LegacyDocFileAdapter : IDocumentFileAdapter
{
    public string Extension => ".doc";
    public string FormatName => "Word 97-2003 Document";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new(".doc", "Word 97-2003 Document", CanOpen: true, CanSave: false),
        new(".dot", "Word 97-2003 Template", CanOpen: true, CanSave: false, OpensAsTemplate: true),
    ];

    public TextDocument Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // The OLE2/CFB reader needs random access; materialize without taking ownership of the caller's stream.
        byte[] input;
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            input = buffer.ToArray();
        }

        // Hop 1 — transcode the legacy binary .doc into an in-memory .docx (DocSharp / b2xtranslator fork).
        byte[] docxBytes;
        try
        {
            using var inputStream = new MemoryStream(input, writable: false);
            using var reader = new StructuredStorageReader(inputStream);
            var doc = new WordDocument(reader);
            using var docxStream = new MemoryStream();
            using (var docx = DocSharpWordprocessingDocument.Create(
                docxStream, DocSharp.Binary.OpenXmlLib.WordprocessingDocumentType.Document))
            {
                DocSharp.Binary.WordprocessingMLMapping.Converter.Convert(doc, docx);
            }
            docxBytes = docxStream.ToArray();
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                "Could not read this as a Word 97-2003 (.doc) document. Older Word 6.0/95 files and " +
                "non-Word OLE documents are not supported.", ex);
        }

        // Hop 2 — read the transcoded .docx with the existing engine. Distinct message so the two hops are
        // distinguishable on failure.
        try
        {
            using var docxStream = new MemoryStream(docxBytes, writable: false);
            return DocxReader.Read(docxStream);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                "The legacy .doc was converted, but the resulting document could not be read.", ex);
        }
    }

    public void Save(TextDocument document, Stream stream) =>
        throw new NotSupportedException("Legacy .doc is read-only — use Save As .docx.");
}
