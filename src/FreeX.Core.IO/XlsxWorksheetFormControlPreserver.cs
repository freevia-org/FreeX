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
        XlsxSourcePackagePreservationContext? context,
        Workbook? workbook = null)
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

            // Write the live FormControlModel state (IsChecked/Value/SelectedIndex, mutated by user
            // interaction since load) back into the source archive's ctrlProp parts BEFORE cloning
            // the controls block or copying ctrlProps forward, so the round-tripped file reflects the
            // control's current state rather than silently reverting to its file-load state.
            var sheet = workbook?.GetSheet(sheetName);
            if (sheet is not null)
                WriteControlStateToCtrlProps(sourceArchive, targetArchive, context, sourceWorksheetPath, sheet);

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
    /// Rewrites each control's <c>checked</c>/<c>val</c>/<c>sel</c> <c>formControlPr</c> attributes
    /// (in the TARGET archive's already-copied ctrlProp part — see
    /// <see cref="XlsxPackageMetadataMerger.CopyUnknownPackageParts"/>) from the corresponding
    /// <see cref="FormControlModel"/>'s live state, matched primarily by <c>shapeId</c> (unique per
    /// control) against <paramref name="sheet"/>.<see cref="Sheet.FormControls"/>, falling back to
    /// document order when a shapeId is unavailable on either side. Only the attributes we actually
    /// model are touched; every other attribute is left untouched so unmodeled state still
    /// round-trips byte-for-byte.
    /// </summary>
    private static void WriteControlStateToCtrlProps(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext context,
        string sourceWorksheetPath,
        Sheet sheet)
    {
        if (sheet.FormControls.Count == 0)
            return;

        var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceArchive, sourceWorksheetPath);
        var sourceRoot = sourceWorksheetXml?.Root;
        if (sourceRoot is null)
            return;

        var controlElements = EnumerateControlElements(sourceRoot, context.WorkbookNs + "control").ToList();
        if (controlElements.Count == 0)
            return;

        var worksheetRels = XlsxRelationshipReader.LoadTargets(
            sourceArchive,
            XlsxPackagePath.GetRelationshipPartPath(sourceWorksheetPath),
            sourceWorksheetPath,
            context.PackageRelNs);

        // Controls are read into FormControlModel in the same document order they're enumerated
        // here (XlsxFormControlMapper.ReadWorksheet uses the identical traversal), so pairing by
        // index recovers each model's originating <control> element PROVIDED every control parsed
        // successfully on load. XlsxFormControlMapper.ReadWorksheet swallows a single malformed
        // control's parse failure rather than aborting the whole sheet, which can shift a purely
        // positional pairing out of sync — so prefer matching by ShapeId (unique per control,
        // present on both the XML element and the loaded model) and only fall back to positional
        // pairing when ShapeId is unavailable on either side.
        var controlsByShapeId = sheet.FormControls
            .Where(c => c.ShapeId is not null)
            .ToDictionary(c => c.ShapeId!.Value, c => c);

        for (var i = 0; i < controlElements.Count; i++)
        {
            var element = controlElements[i];
            FormControlModel? control = null;
            if (uint.TryParse(element.Attribute("shapeId")?.Value, out var shapeId))
                controlsByShapeId.TryGetValue(shapeId, out control);

            control ??= i < sheet.FormControls.Count ? sheet.FormControls[i] : null;
            if (control is null)
                continue;

            var relId = element.Attribute(context.RelNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(relId) ||
                !worksheetRels.TryGetValue(relId, out var ctrlPropPath) ||
                string.IsNullOrWhiteSpace(ctrlPropPath))
            {
                continue;
            }

            var targetEntry = targetArchive.GetEntry(ctrlPropPath);
            if (targetEntry is null)
                continue;

            var ctrlPropXml = XlsxPackageXmlEditor.LoadXml(targetEntry);
            var formControlPr = ctrlPropXml.Root;
            if (formControlPr is null)
                continue;

            ApplyControlStateToFormControlPr(formControlPr, control);
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, ctrlPropPath, ctrlPropXml);
        }
    }

    /// <summary>Writes IsChecked/Value/SelectedIndex onto a <c>formControlPr</c> element's attributes.</summary>
    private static void ApplyControlStateToFormControlPr(XElement formControlPr, FormControlModel control)
    {
        switch (control.Kind)
        {
            case FormControlKind.CheckBox:
            case FormControlKind.OptionButton:
                // Excel's tri-state checkbox value ("Mixed") is not modeled — we only ever write
                // the two states FormControlModel.IsChecked can represent.
                formControlPr.SetAttributeValue("checked", control.IsChecked ? "Checked" : "Unchecked");
                break;

            case FormControlKind.Spinner:
            case FormControlKind.ScrollBar:
                if (control.Value is { } value)
                    formControlPr.SetAttributeValue("val", value);
                break;

            case FormControlKind.ListBox:
            case FormControlKind.DropDown:
                if (control.SelectedIndex is { } selectedIndex)
                    formControlPr.SetAttributeValue("sel", selectedIndex);
                break;
        }
    }

    /// <summary>
    /// Enumerates a worksheet's <c>&lt;control&gt;</c> elements in document order, descending
    /// through <c>mc:AlternateContent</c>/<c>mc:Choice</c>/<c>mc:Fallback</c> wrappers exactly like
    /// <see cref="XlsxFormControlMapper"/>'s loader does (same namespace-qualified name match and
    /// first-Choice-else-Fallback traversal), so the two traversals stay paired.
    /// </summary>
    private static IEnumerable<XElement> EnumerateControlElements(XElement element, XName controlName)
    {
        foreach (var child in element.Elements())
        {
            if (child.Name == controlName)
            {
                yield return child;
                continue;
            }

            if (child.Name == McNs + "AlternateContent")
            {
                var preferred = child.Element(McNs + "Choice") ?? child.Element(McNs + "Fallback");
                if (preferred is not null)
                {
                    foreach (var match in EnumerateControlElements(preferred, controlName))
                        yield return match;
                }

                continue;
            }

            foreach (var match in EnumerateControlElements(child, controlName))
                yield return match;
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
