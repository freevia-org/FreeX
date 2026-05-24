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

        using var sourceStream = new MemoryStream(sourcePackage.Bytes, writable: false);
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
        XlsxWorksheetPrinterSettingsReferencePreserver.Preserve(sourceArchive, generatedArchive);
        XlsxWorksheetMetadataPreserver.Preserve(sourceArchive, generatedArchive, workbook);
        XlsxLegacyCommentPreserver.Preserve(sourceArchive, generatedArchive, workbook);
        XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics(sourceArchive, generatedArchive);
        XlsxUnsupportedConditionalFormattingPreserver.Preserve(sourceArchive, generatedArchive);
    }


    private sealed record XlsxSourcePackage(byte[] Bytes);
}
