using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Reads legacy Excel form-control state (the <c>formControlPr</c> element stored in
/// <c>xl/ctrlProps/ctrlPropN.xml</c>) into a <see cref="FormControlModel"/> so that form
/// controls are no longer silently dropped on load. The underlying VML/ctrlProps package
/// parts are round-tripped verbatim by the preservation layer, so this mapper only needs to
/// surface the modeled state (type, checked/value/min/max, linked cell, list fill range).
/// </summary>
internal static class XlsxFormControlMapper
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace McNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    /// <summary>
    /// Read every legacy form control declared by a worksheet's <c>controls</c> block (handling the
    /// <c>mc:AlternateContent</c> wrappers Excel uses) into models. Resolves each control's
    /// <c>r:id</c> to its <c>ctrlProp</c> part to recover the control state.
    /// </summary>
    public static IReadOnlyList<FormControlModel> ReadWorksheet(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return [];

        var controlElements = EnumerateDescendantsThroughAlternateContent(root, WorksheetNs + "control").ToList();
        if (controlElements.Count == 0)
            return [];

        var worksheetRels = XlsxRelationshipReader.LoadTargets(
            archive,
            XlsxPackagePath.GetRelationshipPartPath(worksheetPath),
            worksheetPath,
            XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships"));

        var controls = new List<FormControlModel>(controlElements.Count);
        foreach (var controlElement in controlElements)
        {
            try
            {
                var model = ReadControl(archive, worksheetPath, controlElement, worksheetRels);
                if (model is not null)
                    controls.Add(model);
            }
            catch
            {
                // A single malformed control must never abort loading the rest of the worksheet
                // metadata. The underlying VML/ctrlProps parts are still preserved on round-trip.
            }
        }

        return controls;
    }

    private static FormControlModel? ReadControl(
        ZipArchive archive,
        string worksheetPath,
        XElement controlElement,
        IReadOnlyDictionary<string, string> worksheetRels)
    {
        var relId = controlElement.Attribute(RelNs + "id")?.Value;
        var ctrlPropXml = ReadCtrlPropXml(archive, worksheetPath, relId, worksheetRels);

        var model = ReadControlProperties(ctrlPropXml) ?? new FormControlModel();
        model.Name = NullIfWhiteSpace(controlElement.Attribute("name")?.Value);
        if (uint.TryParse(controlElement.Attribute("shapeId")?.Value, out var shapeId))
            model.ShapeId = shapeId;

        var controlPr = controlElement.Element(WorksheetNs + "controlPr");
        var anchor = controlPr?.Element(WorksheetNs + "anchor");
        if (anchor is not null)
            model.Anchor = ReadAnchor(anchor);

        // controlPr can also carry the linked cell / list fill range when no ctrlProp part exists.
        model.LinkedCell ??= NullIfWhiteSpace(controlPr?.Attribute("fmlaLink")?.Value);
        model.ListFillRange ??= NullIfWhiteSpace(controlPr?.Attribute("fmlaRange")?.Value);

        return model;
    }

    private static XElement? ReadCtrlPropXml(
        ZipArchive archive,
        string worksheetPath,
        string? relId,
        IReadOnlyDictionary<string, string> worksheetRels)
    {
        if (string.IsNullOrWhiteSpace(relId) ||
            !worksheetRels.TryGetValue(relId, out var ctrlPropPath) ||
            string.IsNullOrWhiteSpace(ctrlPropPath))
        {
            return null;
        }

        var entry = archive.GetEntry(ctrlPropPath);
        if (entry is null)
            return null;

        try
        {
            return XlsxPackageXmlEditor.LoadXml(entry).Root;
        }
        catch
        {
            return null;
        }
    }

    private static GridRange? ReadAnchor(XElement anchor)
    {
        var from = anchor.Element(WorksheetNs + "from");
        var to = anchor.Element(WorksheetNs + "to");
        if (from is null || to is null)
            return null;

        if (!TryReadAnchorCell(from, out var fromRow, out var fromCol) ||
            !TryReadAnchorCell(to, out var toRow, out var toCol))
        {
            return null;
        }

        // Anchor cell indices in the drawing namespace are 0-based; the model is 1-based.
        // A placeholder SheetId is used for both endpoints (GridRange requires a shared sheet);
        // the load applier rebinds the anchor to the owning sheet's id.
        var placeholderSheetId = SheetId.New();
        var start = new CellAddress(placeholderSheetId, fromRow + 1, fromCol + 1);
        var end = new CellAddress(placeholderSheetId, toRow + 1, toCol + 1);
        return new GridRange(start, end);
    }

    private static bool TryReadAnchorCell(XElement anchorCell, out uint row, out uint col)
    {
        row = 0;
        col = 0;
        var colValue = anchorCell.Element(DrawingNs + "col")?.Value;
        var rowValue = anchorCell.Element(DrawingNs + "row")?.Value;
        return uint.TryParse(colValue, out col) & uint.TryParse(rowValue, out row);
    }

    /// <summary>
    /// Enumerate descendants matching <paramref name="name"/>, transparently descending through
    /// <c>mc:AlternateContent</c> / <c>mc:Choice</c> / <c>mc:Fallback</c> wrappers (form controls are
    /// nested inside these in modern Excel files). Only the first <c>mc:Choice</c> of each
    /// AlternateContent is followed to avoid double-counting the equivalent Fallback markup.
    /// </summary>
    private static IEnumerable<XElement> EnumerateDescendantsThroughAlternateContent(XElement element, XName name)
    {
        foreach (var child in element.Elements())
        {
            if (child.Name == name)
            {
                yield return child;
                continue;
            }

            if (child.Name == McNs + "AlternateContent")
            {
                var preferred = child.Element(McNs + "Choice") ?? child.Element(McNs + "Fallback");
                if (preferred is not null)
                {
                    foreach (var match in EnumerateDescendantsThroughAlternateContent(preferred, name))
                        yield return match;
                }

                continue;
            }

            // Recurse into ordinary container elements (e.g. <controls>).
            foreach (var match in EnumerateDescendantsThroughAlternateContent(child, name))
                yield return match;
        }
    }

    /// <summary>
    /// Parse a <c>formControlPr</c> element (from a ctrlProp part) into a model. Returns
    /// <see langword="null"/> only when the element is null.
    /// </summary>
    public static FormControlModel? ReadControlProperties(XElement? formControlPr)
    {
        if (formControlPr is null)
            return null;

        var model = new FormControlModel
        {
            Kind = MapObjectType(formControlPr.Attribute("objectType")?.Value),
            IsChecked = string.Equals(
                formControlPr.Attribute("checked")?.Value,
                "Checked",
                StringComparison.OrdinalIgnoreCase),
            LinkedCell = NullIfWhiteSpace(formControlPr.Attribute("fmlaLink")?.Value),
            ListFillRange = NullIfWhiteSpace(formControlPr.Attribute("fmlaRange")?.Value),
            Value = ReadInt(formControlPr, "val"),
            Min = ReadInt(formControlPr, "min"),
            Max = ReadInt(formControlPr, "max"),
            Increment = ReadInt(formControlPr, "inc"),
            PageChange = ReadInt(formControlPr, "page"),
            SelectedIndex = ReadInt(formControlPr, "sel"),
        };

        return model;
    }

    private static FormControlKind MapObjectType(string? objectType) => objectType switch
    {
        null => FormControlKind.Unknown,
        _ when objectType.Equals("Button", StringComparison.OrdinalIgnoreCase) => FormControlKind.Button,
        _ when objectType.Equals("CheckBox", StringComparison.OrdinalIgnoreCase) => FormControlKind.CheckBox,
        _ when objectType.Equals("Radio", StringComparison.OrdinalIgnoreCase) => FormControlKind.OptionButton,
        _ when objectType.Equals("Option", StringComparison.OrdinalIgnoreCase) => FormControlKind.OptionButton,
        _ when objectType.Equals("Drop", StringComparison.OrdinalIgnoreCase) => FormControlKind.DropDown,
        _ when objectType.Equals("List", StringComparison.OrdinalIgnoreCase) => FormControlKind.ListBox,
        _ when objectType.Equals("GBox", StringComparison.OrdinalIgnoreCase) => FormControlKind.GroupBox,
        _ when objectType.Equals("Label", StringComparison.OrdinalIgnoreCase) => FormControlKind.Label,
        _ when objectType.Equals("Scroll", StringComparison.OrdinalIgnoreCase) => FormControlKind.ScrollBar,
        _ when objectType.Equals("Spin", StringComparison.OrdinalIgnoreCase) => FormControlKind.Spinner,
        _ => FormControlKind.Unknown,
    };

    private static int? ReadInt(XElement element, string attributeName)
    {
        var value = element.Attribute(attributeName)?.Value;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
