using System.IO;
using DocSharp.Binary.DocFileFormat;
using DocSharp.Binary.StructuredStorage.Reader;
using FreeW.Core.Model;
using DocSharpWordprocessingDocument = DocSharp.Binary.OpenXmlLib.WordprocessingML.WordprocessingDocument;

namespace FreeW.Core.IO;

/// <summary>
/// Legacy Word 97-2003 binary document adapter (<c>.doc</c>/<c>.dot</c>) — design §5.5. Load uses
/// DocSharp.Binary.Doc to transcode the binary OLE2/CFB format to an in-memory <c>.docx</c> which is
/// then read by the existing <see cref="DocxReader"/>, so all of FreeW's WordprocessingML mapping is
/// reused for free. Save uses <see cref="LegacyDocWriter"/> to produce a minimal but valid Word 97-2003
/// binary <c>.doc</c> (OLE2/CFB container + FIB + Unicode text stream + CLX piece table) that
/// round-trips through DocSharp's binary-Word reader. Pre-97 Word 6.0/95 binaries and non-Word OLE
/// documents fail with a clear message on load. Mirrors the sibling app's legacy-binary adapter.
/// </summary>
public sealed class LegacyDocFileAdapter : IDocumentFileAdapter
{
    public string Extension => ".doc";
    public string FormatName => "Word 97-2003 Document";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new(".doc", "Word 97-2003 Document", CanOpen: true, CanSave: true),
        new(".dot", "Word 97-2003 Template", CanOpen: true, CanSave: true, OpensAsTemplate: true),
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

    public void Save(TextDocument document, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        LegacyDocWriter.Write(document, stream);
    }
}
