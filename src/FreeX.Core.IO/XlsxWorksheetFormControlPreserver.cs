using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Preserves legacy Excel form controls across an edited save (the full-rebuild path). ClosedXML
/// regenerates each worksheet without the <c>controls</c> block or its <c>legacyDrawing</c> marker,
/// which orphans the (otherwise copied) VML/ctrlProps parts so Excel shows nothing. This preserver
/// copies the source worksheet's controls block and form-control <c>legacyDrawing</c> back into the
/// generated worksheet, then re-binds the relationship ids via the shared OLE-control normalizer so
/// the controls re-attach to their preserved ctrlProps and VML drawing.
/// </summary>
internal static class XlsxWorksheetFormControlPreserver
{
    private static readonly XNamespace McNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string VmlDrawingRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";

    public static void Preserve(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext? context)
    {
        if (context is null)
            return;

        var anyChange = false;
        foreach (var (sheetName, sourceWorksheetPath) in context.SourceSheets)
        {
            if (!context.TargetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceArchive, sourceWorksheetPath);
            var sourceRoot = sourceWorksheetXml?.Root;
            if (sourceRoot is null)
                continue;

            var sourceControls = FindControlsContainer(sourceRoot, context.WorkbookNs);
            if (sourceControls is null)
                continue;

            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (targetWorksheetEntry is null)
                continue;

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null)
                continue;

            // If a controls block already survived (clean byte-copy path), leave it alone.
            if (FindControlsContainer(targetRoot, context.WorkbookNs) is not null)
                continue;

            InjectFormControlLegacyDrawing(
                sourceArchive,
                targetArchive,
                context,
                sourceRoot,
                sourceWorksheetPath,
                targetRoot,
                targetWorksheetPath);

            targetRoot.SetAttributeValue(XNamespace.Xmlns + "r", context.RelNs.NamespaceName);
            targetRoot.SetAttributeValue(XNamespace.Xmlns + "mc", McNs.NamespaceName);
            InsertControlsInWorksheetOrder(targetRoot, context.WorkbookNs, CloneControlsBlock(sourceRoot, context.WorkbookNs));
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
            anyChange = true;
        }

        if (anyChange)
        {
            // Re-bind the freshly injected <control> r:id values to the copied ctrlProps parts.
            XlsxWorksheetOleControlNormalizer.NormalizePackage(targetArchive);
        }
    }

    /// <summary>
    /// Copy the source form-control <c>legacyDrawing</c> marker (the VML shape geometry) into the
    /// target worksheet + relationships. Returns true when a marker was injected.
    /// </summary>
    private static bool InjectFormControlLegacyDrawing(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext context,
        XElement sourceRoot,
        string sourceWorksheetPath,
        XElement targetRoot,
        string targetWorksheetPath)
    {
        // A control's VML lives behind the worksheet legacyDrawing marker. Only inject it if the
        // target does not already have one (comments also use legacyDrawing — that path is handled
        // by XlsxWorksheetVmlReferencePreserver).
        if (targetRoot.Element(context.WorkbookNs + "legacyDrawing") is not null)
            return false;

        var sourceMarker = sourceRoot.Element(context.WorkbookNs + "legacyDrawing");
        var sourceRelId = sourceMarker?.Attribute(context.RelNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(sourceRelId))
            return false;

        var sourceRelsPath = XlsxPackagePath.GetRelationshipPartPath(sourceWorksheetPath);
        var sourceRels = XlsxRelationshipReader.LoadTargets(
            sourceArchive,
            sourceRelsPath,
            sourceWorksheetPath,
            context.PackageRelNs);
        if (!sourceRels.TryGetValue(sourceRelId, out var vmlPath) ||
            targetArchive.GetEntry(vmlPath) is null)
        {
            return false;
        }

        var targetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
        var targetRelsXml = targetArchive.GetEntry(targetRelsPath) is { } targetRelsEntry
            ? XlsxPackageXmlEditor.LoadXml(targetRelsEntry)
            : new XDocument(new XElement(context.PackageRelNs + "Relationships"));
        var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
            targetRelsXml,
            context.PackageRelNs,
            targetWorksheetPath,
            vmlPath,
            VmlDrawingRelationshipType);
        XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetRelsPath, targetRelsXml);

        var marker = new XElement(context.WorkbookNs + "legacyDrawing",
            new XAttribute(context.RelNs + "id", targetRelId));
        InsertLegacyDrawingInWorksheetOrder(targetRoot, context.WorkbookNs, marker);
        return true;
    }

    private static XElement? FindControlsContainer(XElement worksheetRoot, XNamespace worksheetNs)
    {
        // Excel stores controls either directly as <controls> or wrapped in an mc:AlternateContent.
        var direct = worksheetRoot.Element(worksheetNs + "controls");
        if (direct is not null)
            return direct;

        foreach (var alternateContent in worksheetRoot.Elements(McNs + "AlternateContent"))
        {
            var preferred = alternateContent.Element(McNs + "Choice") ?? alternateContent.Element(McNs + "Fallback");
            if (preferred?.Element(worksheetNs + "controls") is not null)
                return alternateContent;
        }

        return null;
    }

    /// <summary>Clone the source controls container (direct or AlternateContent-wrapped) verbatim.</summary>
    private static XElement CloneControlsBlock(XElement sourceRoot, XNamespace worksheetNs)
    {
        var container = FindControlsContainer(sourceRoot, worksheetNs)!;
        return new XElement(container);
    }

    private static void InsertControlsInWorksheetOrder(XElement worksheetRoot, XNamespace worksheetNs, XElement controlsBlock)
    {
        string[] laterElements = ["webPublishItems", "tableParts", "extLst"];
        var insertionPoint = FindFirstWorksheetElement(worksheetRoot, worksheetNs, laterElements);
        if (insertionPoint is null)
            worksheetRoot.Add(controlsBlock);
        else
            insertionPoint.AddBeforeSelf(controlsBlock);
    }

    private static void InsertLegacyDrawingInWorksheetOrder(XElement worksheetRoot, XNamespace worksheetNs, XElement marker)
    {
        string[] laterElements = ["legacyDrawingHF", "picture", "oleObjects", "controls", "webPublishItems", "tableParts", "extLst"];
        var insertionPoint = FindFirstWorksheetElement(worksheetRoot, worksheetNs, laterElements);
        if (insertionPoint is null)
            worksheetRoot.Add(marker);
        else
            insertionPoint.AddBeforeSelf(marker);
    }

    private static XElement? FindFirstWorksheetElement(XElement worksheetRoot, XNamespace worksheetNs, string[] laterElements)
    {
        foreach (var element in worksheetRoot.Elements())
        {
            if (element.Name.Namespace == worksheetNs &&
                laterElements.Contains(element.Name.LocalName, StringComparer.Ordinal))
            {
                return element;
            }

            // Controls wrapped in AlternateContent should also be treated as a "later" boundary.
            if (element.Name == McNs + "AlternateContent")
                return element;
        }

        return null;
    }
}
