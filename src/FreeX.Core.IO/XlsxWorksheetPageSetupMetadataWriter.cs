using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPageSetupMetadataWriter
{
    public static bool HasModeledPrinterAttributes(Sheet sheet) =>
        sheet.UsePrinterDefaults is not null ||
        sheet.PrintCopies is > 0 ||
        sheet.PrintQualityVerticalDpi is > 0 ||
        sheet.PageSetupMetadata is not null ||
        sheet.FitToPage is not null ||
        sheet.AutoPageBreaks is not null ||
        sheet.OutlineSummaryBelow is not null ||
        sheet.OutlineSummaryRight is not null ||
        sheet.ShowOutlineSymbols is not null ||
        sheet.ApplyOutlineStyles is not null;

    public static void Save(
        Stream packageStream,
        Workbook workbook,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var session = new XlsxWorksheetXmlEditSession(packageStream, worksheetPathMap);
        Save(session, workbook);
    }

    internal static void Save(XlsxWorksheetXmlEditSession session, Workbook workbook)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var sheet in workbook.Sheets)
        {
            if (!session.TryGetWorksheet(sheet, out var edit))
                continue;

            var root = edit.Root;

            var changed = false;
            changed |= ApplyPageSetupAttributes(root, workbookNs, sheet);
            changed |= ApplyPageSetupProperties(root, workbookNs, sheet);
            changed |= ApplyOutlineProperties(root, workbookNs, sheet);

            if (changed)
                session.MarkDirty(edit);
        }
    }

    private static bool ApplyPageSetupAttributes(XElement root, XNamespace workbookNs, Sheet sheet)
    {
        var pageSetup = root.Element(workbookNs + "pageSetup");
        if (pageSetup is null)
        {
            if (sheet.UsePrinterDefaults is null &&
                sheet.PrintCopies is not > 0 &&
                sheet.PrintQualityVerticalDpi is not > 0 &&
                sheet.PageSetupMetadata is null)
            {
                return false;
            }

            pageSetup = new XElement(workbookNs + "pageSetup");
            InsertPageSetupInOrder(root, workbookNs, pageSetup);
        }

        var changed = false;
        changed |= SetOptionalBoolAttribute(pageSetup, "usePrinterDefaults", sheet.UsePrinterDefaults);
        changed |= SetOptionalIntAttribute(pageSetup, "copies", sheet.PrintCopies);
        changed |= SetOptionalIntAttribute(pageSetup, "verticalDpi", sheet.PrintQualityVerticalDpi);
        changed |= ApplyNativePageSetupMetadata(pageSetup, sheet.PageSetupMetadata);
        changed |= XlsxWorksheetPageLayoutNormalizer.NormalizePageSetup(pageSetup);
        return changed;
    }

    private static bool ApplyNativePageSetupMetadata(XElement pageSetup, NativeXmlPreserveBag? metadata)
    {
        if (metadata is null)
            return false;

        return XmlNativeBagSerializer.ApplyToElement(pageSetup, metadata.Get("pageSetup"), ModeledPageSetupAttributes);
    }

    private static readonly IReadOnlyCollection<string> ModeledPageSetupAttributes =
    [
        "paperSize", "scale", "firstPageNumber", "fitToWidth", "fitToHeight",
        "pageOrder", "orientation", "usePrinterDefaults", "blackAndWhite", "draft",
        "cellComments", "useFirstPageNumber", "errors", "horizontalDpi", "verticalDpi", "copies"
    ];

    private static bool ApplyPageSetupProperties(XElement root, XNamespace workbookNs, Sheet sheet)
    {
        // sheet.FitToPage is an independent, load-time flag that the Page Setup dialog's
        // fit-to-page/scale-% toggle (SetPageSetupCommand) never updates -- it only ever writes
        // sheet.ScaleToFit. Deriving the effective flag from ScaleToFit (mirroring exactly how
        // XlsxFileAdapter.Save picks between xlSheet.PageSetup.Scale and .FitToPages(...) at
        // save time -- see XlsxFileAdapter.Save.cs) keeps this post-processing pass from
        // clobbering a correct ClosedXML-written pageSetUpPr/@fitToPage with a stale value, and
        // from injecting a fitToPage="1" element for a sheet that is really in scale-% mode.
        var effectiveFitToPage = DetermineEffectiveFitToPage(sheet);

        var sheetProperties = root.Element(workbookNs + "sheetPr");
        var pageSetupProperties = sheetProperties?.Element(workbookNs + "pageSetUpPr");
        if (pageSetupProperties is null)
        {
            // Only force-create the element for an explicit fit-to-page mode (true) or an
            // explicit auto-page-breaks flag. A derived "false" (scale mode) needs no element:
            // omission already means fitToPage=false per the OOXML schema default, matching how
            // ClosedXML (and Excel itself) omit pageSetUpPr for scale-only page setups.
            if (effectiveFitToPage is not true && sheet.AutoPageBreaks is null)
                return false;

            sheetProperties ??= new XElement(workbookNs + "sheetPr");
            if (sheetProperties.Parent is null)
                root.AddFirst(sheetProperties);

            pageSetupProperties = new XElement(workbookNs + "pageSetUpPr");
            sheetProperties.Add(pageSetupProperties);
        }

        var changed = false;
        changed |= SetOptionalBoolAttribute(pageSetupProperties, "fitToPage", effectiveFitToPage);
        changed |= SetOptionalBoolAttribute(pageSetupProperties, "autoPageBreaks", sheet.AutoPageBreaks);
        changed |= XlsxWorksheetPageLayoutNormalizer.NormalizeSheetPropertiesPageLayout(sheetProperties!);
        return changed;
    }

    /// <summary>
    /// Resolves the effective <c>fitToPage</c> flag from the sheet's actual print-scaling mode
    /// instead of trusting the possibly-stale <see cref="Sheet.FitToPage"/> field. An explicit
    /// scale percentage always means scale mode (fitToPage must be false so Excel honors
    /// <c>&lt;pageSetup scale="..."/&gt;</c> instead of stale fitToWidth/fitToHeight attributes);
    /// an explicit fit-to-page axis (wide and/or tall) always means fit-to-page mode. Only falls
    /// back to the raw <see cref="Sheet.FitToPage"/> value when neither signal is present, which
    /// in practice cannot happen because <see cref="Sheet.ScaleToFit"/> always resolves to one of
    /// the two modes on load (see XlsxFileAdapter.cs) or defaults to 100% scale.
    /// </summary>
    private static bool? DetermineEffectiveFitToPage(Sheet sheet)
    {
        var scaleToFit = sheet.ScaleToFit;
        if (scaleToFit.ScalePercent is not null)
            return false;
        if (scaleToFit.FitToPagesWide is not null || scaleToFit.FitToPagesTall is not null)
            return true;
        return sheet.FitToPage;
    }

    private static bool ApplyOutlineProperties(XElement root, XNamespace workbookNs, Sheet sheet)
    {
        var sheetProperties = root.Element(workbookNs + "sheetPr");
        var outlineProperties = sheetProperties?.Element(workbookNs + "outlinePr");
        if (outlineProperties is null)
        {
            if (sheet.OutlineSummaryBelow is null &&
                sheet.OutlineSummaryRight is null &&
                sheet.ShowOutlineSymbols is null &&
                sheet.ApplyOutlineStyles is null)
            {
                return false;
            }

            sheetProperties ??= new XElement(workbookNs + "sheetPr");
            if (sheetProperties.Parent is null)
                root.AddFirst(sheetProperties);

            outlineProperties = new XElement(workbookNs + "outlinePr");
            sheetProperties.Add(outlineProperties);
        }

        var changed = false;
        changed |= SetOptionalBoolAttribute(outlineProperties, "summaryBelow", sheet.OutlineSummaryBelow);
        changed |= SetOptionalBoolAttribute(outlineProperties, "summaryRight", sheet.OutlineSummaryRight);
        changed |= SetOptionalBoolAttribute(outlineProperties, "showOutlineSymbols", sheet.ShowOutlineSymbols);
        changed |= SetOptionalBoolAttribute(outlineProperties, "applyStyles", sheet.ApplyOutlineStyles);
        changed |= XlsxWorksheetPageLayoutNormalizer.NormalizeSheetPropertiesPageLayout(sheetProperties!);
        return changed;
    }

    private static bool SetOptionalBoolAttribute(XElement element, XName name, bool? value) =>
        SetOptionalAttribute(element, name, value is { } flag ? flag ? "1" : "0" : null);

    private static bool SetOptionalIntAttribute(XElement element, XName name, int? value) =>
        SetOptionalAttribute(
            element,
            name,
            value is > 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : null);

    private static bool SetOptionalAttribute(XElement element, XName name, string? value) =>
        XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(element, name, value);

    private static void InsertPageSetupInOrder(
        XElement worksheetRoot,
        XNamespace workbookNs,
        XElement pageSetup)
    {
        var headerFooter = worksheetRoot.Element(workbookNs + "headerFooter");
        if (headerFooter is not null)
        {
            headerFooter.AddBeforeSelf(pageSetup);
            return;
        }

        var pageMargins = worksheetRoot.Element(workbookNs + "pageMargins");
        if (pageMargins is not null)
        {
            pageMargins.AddAfterSelf(pageSetup);
            return;
        }

        worksheetRoot.Add(pageSetup);
    }
}
