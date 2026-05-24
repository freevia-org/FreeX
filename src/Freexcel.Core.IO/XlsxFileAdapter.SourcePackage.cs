using System.IO.Compression;

using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class XlsxFileAdapter
{
    // Source package snapshot and native package-part preservation for loaded workbook saves.
    private static void PreserveSourcePackageParts(Workbook workbook, Stream generatedPackage)
    {
        if (!SourcePackages.TryGetValue(workbook, out var sourcePackage))
            return;

        using var sourceStream = sourcePackage.OpenRead();
        using var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false);
        using var generatedArchive = new ZipArchive(generatedPackage, ZipArchiveMode.Update, leaveOpen: true);
        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, generatedArchive);

        XlsxPackageMetadataMerger.MergeContentTypes(sourceArchive, generatedArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, generatedArchive, generatedEntriesBeforeMerge);
        XlsxDocumentPropertiesPreserver.Preserve(sourceArchive, generatedArchive);
        XlsxWorkbookMetadataPreserver.Preserve(sourceArchive, generatedArchive, workbook);
        XlsxStylesheetMetadataPreserver.Preserve(sourceArchive, generatedArchive);
        XlsxPivotXmlReferencePreserver.Preserve(sourceArchive, generatedArchive);
        XlsxStructuredTableReferencePreserver.Preserve(sourceArchive, generatedArchive);
        XlsxExternalLinkReferencePreserver.Preserve(sourceArchive, generatedArchive);
        XlsxUnsupportedSheetReferencePreserver.Preserve(sourceArchive, generatedArchive);
        XlsxWorksheetDrawingPartMerger.Merge(sourceArchive, generatedArchive);
        XlsxWorksheetDrawingReferencePreserver.Preserve(sourceArchive, generatedArchive);
        if (HasSourcePackagePart(sourceArchive, "xl/printerSettings/"))
            XlsxWorksheetPrinterSettingsReferencePreserver.Preserve(sourceArchive, generatedArchive);
        XlsxWorksheetMetadataPreserver.Preserve(sourceArchive, generatedArchive, workbook);
        XlsxLegacyCommentPreserver.Preserve(sourceArchive, generatedArchive, workbook);
        XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics(sourceArchive, generatedArchive);
        XlsxUnsupportedConditionalFormattingPreserver.Preserve(sourceArchive, generatedArchive);
    }

    private static bool HasSourcePackagePart(ZipArchive archive, string prefix) =>
        archive.Entries.Any(entry => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));


    private sealed record XlsxSourcePackage(byte[] Buffer, int Offset, int Count)
    {
        public static XlsxSourcePackage Capture(MemoryStream stream)
        {
            if (stream.TryGetBuffer(out var buffer))
                return new XlsxSourcePackage(buffer.Array!, buffer.Offset, buffer.Count);

            var bytes = new byte[stream.Length];
            var previousPosition = stream.Position;
            stream.Position = 0;
            stream.ReadExactly(bytes);
            stream.Position = previousPosition;
            return new XlsxSourcePackage(bytes, 0, bytes.Length);
        }

        public MemoryStream OpenRead() => new(Buffer, Offset, Count, writable: false);
    }
}
