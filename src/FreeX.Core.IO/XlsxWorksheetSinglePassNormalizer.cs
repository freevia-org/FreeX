using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Single-pass worksheet normalizer.
///
/// <para>Replaces the previous pattern of ~28 sequential <c>NormalizeWorksheets(ZipArchive)</c>
/// calls, each of which enumerated all worksheet archive entries and loaded each worksheet XDocument
/// independently. This class enumerates worksheet entries once and, for each worksheet, loads the
/// XDocument once, runs all normalizer steps against the shared root element, then writes it back
/// once if any step reported a change.</para>
///
/// <para>Normalizer steps are invoked in the same order as the original pipeline.</para>
/// </summary>
internal static class XlsxWorksheetSinglePassNormalizer
{
    /// <summary>
    /// Runs all worksheet-scoped normalizer steps in a single pass.
    /// Items 1–28 of the original pipeline are fused here; items 24/29–36 that are package-scoped
    /// remain as separate calls in <c>XlsxFileAdapter.SourcePackage.cs</c>.
    /// </summary>
    public static void NormalizeWorksheets(ZipArchive archive)
        => NormalizeWorksheets(archive, WorksheetNormalizationProfile.SourcePreservation);

    /// <summary>
    /// Runs the worksheet-scoped portion of <see cref="XlsxWorkbookSchemaNormalizer"/> in one
    /// archive traversal. The selected steps and their order exactly match the former schema
    /// pipeline; package-only normalizers still run in their original positions around this call.
    /// </summary>
    internal static void NormalizeSchemaWorksheets(
        ZipArchive archive,
        Action<string>? onWorksheetVisited = null)
        => NormalizeWorksheets(archive, WorksheetNormalizationProfile.Schema, onWorksheetVisited);

    private static void NormalizeWorksheets(
        ZipArchive archive,
        WorksheetNormalizationProfile profile,
        Action<string>? onWorksheetVisited = null)
    {
        var (cellMetadataCount, valueMetadataCount) = profile == WorksheetNormalizationProfile.SourcePreservation
            ? XlsxWorksheetGridXmlNormalizer.ReadMetadataCountsForSinglePass(archive)
            : (0u, 0u);

        // Collect worksheet entries upfront (the entry list must not change during normalization).
        var entries = archive.Entries
            .Where(XlsxPackagePath.IsWorksheetXmlEntry)
            .ToList();

        // Track which worksheets have webPublishItems (for the package-metadata step below).
        var worksheetPathsWithWebPublishItems = new List<string>();

        foreach (var entry in entries)
        {
            onWorksheetVisited?.Invoke(entry.FullName);
            var xml = XlsxPackageXmlEditor.LoadXml(entry);
            var root = xml.Root;
            if (root is null)
                continue;

            var changed = false;

            if (profile == WorksheetNormalizationProfile.SourcePreservation)
            {
                // Source-preservation steps 1-2 are not part of the schema normalizer's former
                // worksheet pipeline.
                changed |= XlsxWorksheetGridXmlNormalizer.NormalizeWorksheetRoot(root, cellMetadataCount, valueMetadataCount);
                changed |= XlsxWorksheetMergeCellsNormalizer.NormalizeWorksheetRoot(root);
            }
            // Step  3 – XlsxWorksheetDimensionNormalizer
            changed |= XlsxWorksheetDimensionNormalizer.NormalizeWorksheetRoot(root);
            // Step  4 – XlsxWorksheetCalculationPropertyNormalizer
            changed |= XlsxWorksheetCalculationPropertyNormalizer.NormalizeWorksheetRoot(root);
            // Step  5 – XlsxWorksheetSheetFormatNormalizer
            changed |= XlsxWorksheetSheetFormatNormalizer.NormalizeWorksheetRoot(root);
            // Step  6 – XlsxWorksheetSheetPropertiesNormalizer
            changed |= XlsxWorksheetSheetPropertiesNormalizer.NormalizeWorksheetRoot(root);
            // Step  7 – XlsxWorksheetSheetViewNormalizer
            changed |= XlsxWorksheetSheetViewNormalizer.NormalizeWorksheetRoot(root);
            // Step  8 – XlsxWorksheetProtectionNormalizer
            changed |= XlsxWorksheetProtectionNormalizer.NormalizeWorksheetRoot(root);
            // Step  9 – XlsxWorksheetProtectedRangeNormalizer
            changed |= XlsxWorksheetProtectedRangeNormalizer.NormalizeWorksheetRoot(root);
            // Step 10 – XlsxWorksheetScenarioNormalizer
            changed |= XlsxWorksheetScenarioNormalizer.NormalizeWorksheetRoot(root);
            // Step 11 – XlsxWorksheetSmartTagNormalizer
            changed |= XlsxWorksheetSmartTagNormalizer.NormalizeWorksheetRoot(root);
            // Step 12 – XlsxWorksheetCustomSheetViewExtensionListNormalizer
            changed |= XlsxWorksheetCustomSheetViewExtensionListNormalizer.NormalizeWorksheetRoot(root);
            // Step 13 – XlsxWorksheetPhoneticPropertyNormalizer
            changed |= XlsxWorksheetPhoneticPropertyNormalizer.NormalizeWorksheetRoot(root);
            // Step 14 – XlsxWorksheetCellWatchesNormalizer
            changed |= XlsxWorksheetCellWatchesNormalizer.NormalizeWorksheetRoot(root);
            // Step 15 – XlsxWorksheetCustomPropertiesNormalizer
            changed |= XlsxWorksheetCustomPropertiesNormalizer.NormalizeWorksheetRoot(root);
            // Step 16 – XlsxWorksheetIgnoredErrorsNormalizer
            changed |= XlsxWorksheetIgnoredErrorsNormalizer.NormalizeWorksheetRoot(root);
            // Step 17 – XlsxWorksheetHyperlinkNormalizer
            changed |= XlsxWorksheetHyperlinkNormalizer.NormalizeWorksheetRoot(root);
            // Step 18 – XlsxWorksheetConditionalFormatNormalizer
            changed |= XlsxWorksheetConditionalFormatNormalizer.NormalizeWorksheetRoot(root);
            // Step 19 – XlsxWorksheetAutoFilterNormalizer
            changed |= XlsxWorksheetAutoFilterNormalizer.NormalizeWorksheetRoot(root);
            // Step 20 – XlsxWorksheetSortStateNormalizer
            changed |= XlsxWorksheetSortStateNormalizer.NormalizeWorksheetRoot(root);
            // Step 21 – XlsxWorksheetDataConsolidationNormalizer
            changed |= XlsxWorksheetDataConsolidationNormalizer.NormalizeWorksheetRoot(root);
            // Step 22 – XlsxWorksheetDataValidationNormalizer
            changed |= XlsxWorksheetDataValidationNormalizer.NormalizeWorksheetRoot(root);
            // Step 23 – XlsxWorksheetExtensionListNormalizer
            changed |= XlsxWorksheetExtensionListNormalizer.NormalizeWorksheetRoot(root);
            // Step 24 – XlsxWorksheetWebPublishItemsNormalizer (worksheet-XML portion only)
            changed |= XlsxWorksheetWebPublishItemsNormalizer.NormalizeWorksheetRoot(root);
            if (root.Elements(XlsxWorksheetWebPublishItemsNs + "webPublishItems").Any())
                worksheetPathsWithWebPublishItems.Add(entry.FullName);
            // Step 25 – XlsxWorksheetOleControlNormalizer
            changed |= XlsxWorksheetOleControlNormalizer.NormalizeWorksheetRoot(root);
            if (profile == WorksheetNormalizationProfile.Schema)
                changed |= XlsxWorksheetOleControlNormalizer.NormalizePackageRelationships(archive, entry.FullName, xml);
            // Step 26 – XlsxWorksheetRelationshipMarkerNormalizer
            changed |= XlsxWorksheetRelationshipMarkerNormalizer.NormalizeWorksheetRoot(root);
            // Step 27 – XlsxWorksheetPageLayoutNormalizer
            // (also calls XlsxWorksheetSheetPropertiesNormalizer internally — idempotent)
            changed |= XlsxWorksheetPageLayoutNormalizer.NormalizeWorksheetRoot(root);
            // Step 28 – XlsxWorksheetPageBreakNormalizer
            changed |= XlsxWorksheetPageBreakNormalizer.NormalizeWorksheetRoot(root);

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, entry.FullName, xml);
        }

        // Step 24 residual — standalone webPublishItems.xml parts + package-level metadata.
        // This must run after all worksheet XML has been written back.
        XlsxWorksheetWebPublishItemsNormalizer.NormalizePackageResidual(archive, worksheetPathsWithWebPublishItems);
    }

    // Namespace used to locate webPublishItems children after normalization.
    private static readonly XNamespace XlsxWorksheetWebPublishItemsNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private enum WorksheetNormalizationProfile
    {
        SourcePreservation,
        Schema
    }
}
