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
        {
            model.Anchor = ReadAnchor(anchor);
            // Preserve the per-cell EMU sub-cell offsets so the render reflects the true sub-cell
            // position+size rather than snapping to a whole-cell span.
            model.AnchorOffsets = ReadAnchorOffsets(anchor);
        }

        // Resolve the control's VML shape once for both the anchor fallback and the caption text.
        var vmlShape = ResolveVmlShape(archive, worksheetPath, model.ShapeId, worksheetRels);
        if (vmlShape is not null)
        {
            // Fall back to the VML <x:ClientData><x:Anchor> (pixel offsets) when the worksheet controlPr
            // carries no offset-bearing anchor (e.g. legacy files that anchor purely via VML).
            model.AnchorOffsets ??= ParseVmlAnchor(
                vmlShape.Element(ExcelVmlNs + "ClientData")?.Element(ExcelVmlNs + "Anchor")?.Value);
            // The visible caption/label lives in the VML shape's <v:textbox> (empty when the control
            // has no authored display text — Excel shows nothing, and so do we; we never show Name).
            model.Caption = ReadVmlCaption(vmlShape);
        }

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

    private const long EmusPerPixel = 9525;

    /// <summary>
    /// Reads a worksheet <c>controlPr/anchor</c> (from/to cell + EMU <c>colOff</c>/<c>rowOff</c>) into a
    /// 0-based <see cref="DrawingAnchorRange"/> preserving the sub-cell offsets in EMU. Returns
    /// <see langword="null"/> when the from/to cell markers are missing.
    /// </summary>
    public static DrawingAnchorRange? ReadAnchorOffsets(XElement anchor)
    {
        var from = anchor.Element(WorksheetNs + "from");
        var to = anchor.Element(WorksheetNs + "to");
        if (from is null || to is null)
            return null;

        if (!TryReadAnchorPoint(from, out var fromPoint) || !TryReadAnchorPoint(to, out var toPoint))
            return null;

        return new DrawingAnchorRange(fromPoint, toPoint);
    }

    private static bool TryReadAnchorPoint(XElement marker, out DrawingAnchorPoint point)
    {
        point = default!;
        if (!uint.TryParse(marker.Element(DrawingNs + "col")?.Value, out var col) ||
            !uint.TryParse(marker.Element(DrawingNs + "row")?.Value, out var row))
        {
            return false;
        }

        var colOff = ReadEmu(marker.Element(DrawingNs + "colOff")?.Value);
        var rowOff = ReadEmu(marker.Element(DrawingNs + "rowOff")?.Value);
        point = new DrawingAnchorPoint(col, colOff, row, rowOff);
        return true;
    }

    private static long ReadEmu(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var emus) ? emus : 0;

    /// <summary>
    /// Parses a legacy VML <c>x:Anchor</c> value (comma-separated
    /// <c>leftCol,leftColOff,topRow,topRowOff,rightCol,rightColOff,bottomRow,bottomRowOff</c>; cells
    /// 0-based, offsets in PIXELS) into a 0-based <see cref="DrawingAnchorRange"/> with offsets
    /// converted to EMU. Returns <see langword="null"/> for malformed input.
    /// </summary>
    public static DrawingAnchorRange? ParseVmlAnchor(string? anchorText)
    {
        if (string.IsNullOrWhiteSpace(anchorText))
            return null;

        var parts = anchorText.Split(',');
        if (parts.Length != 8)
            return null;

        Span<long> values = stackalloc long[8];
        for (var i = 0; i < 8; i++)
        {
            if (!long.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]) ||
                values[i] < 0)
            {
                return null;
            }
        }

        var from = new DrawingAnchorPoint((uint)values[0], values[1] * EmusPerPixel, (uint)values[2], values[3] * EmusPerPixel);
        var to = new DrawingAnchorPoint((uint)values[4], values[5] * EmusPerPixel, (uint)values[6], values[7] * EmusPerPixel);
        return new DrawingAnchorRange(from, to);
    }

    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace ExcelVmlNs = "urn:schemas-microsoft-com:office:excel";
    private const string VmlDrawingRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";

    /// <summary>
    /// Resolves the worksheet's legacy VML drawing and returns the <c>v:shape</c> for the control's
    /// shape (matched by its <c>shapeId</c>, encoded in the VML shape id as <c>_x0000_s{shapeId}</c>),
    /// from which both the <c>x:Anchor</c> (offset fallback) and the <c>v:textbox</c> caption are read.
    /// </summary>
    private static XElement? ResolveVmlShape(
        ZipArchive archive,
        string worksheetPath,
        uint? shapeId,
        IReadOnlyDictionary<string, string> worksheetRels)
    {
        if (shapeId is null)
            return null;

        var vmlPath = ResolveVmlDrawingPath(archive, worksheetPath, worksheetRels);
        if (vmlPath is null)
            return null;

        var vmlEntry = archive.GetEntry(vmlPath);
        if (vmlEntry is null)
            return null;

        XDocument vmlDoc;
        try
        {
            vmlDoc = XlsxPackageXmlEditor.LoadXml(vmlEntry);
        }
        catch
        {
            return null;
        }

        var root = vmlDoc.Root;
        if (root is null)
            return null;

        var targetSuffix = "s" + shapeId.Value.ToString(CultureInfo.InvariantCulture);
        foreach (var shape in root.Descendants(VmlNs + "shape"))
        {
            var id = shape.Attribute("id")?.Value;
            if (string.IsNullOrEmpty(id) || !id.EndsWith(targetSuffix, StringComparison.Ordinal))
                continue;

            return shape;
        }

        return null;
    }

    /// <summary>
    /// Reads the control's authored caption from its VML shape's <c>v:textbox</c> (the visible label).
    /// Returns <see langword="null"/> when the textbox is absent or empty — the common case for
    /// checkboxes whose caption was cleared, where Excel shows no label.
    /// </summary>
    internal static string? ReadVmlCaption(XElement vmlShape)
    {
        var textbox = vmlShape.Element(VmlNs + "textbox");
        if (textbox is null)
            return null;

        // The caption text is the concatenated text of the textbox content (one or more <div> lines,
        // possibly wrapping <font> runs). XElement.Value already flattens descendant text.
        return NullIfWhiteSpace(textbox.Value);
    }

    private static string? ResolveVmlDrawingPath(
        ZipArchive archive,
        string worksheetPath,
        IReadOnlyDictionary<string, string> worksheetRels)
    {
        // worksheetRels (loaded with the package relationship namespace) maps relId -> resolved target.
        // The control block points at the VML via the worksheet <legacyDrawing r:id>, which resolves to
        // a vmlDrawing part; we identify it by the ".vml" target since the rel-type isn't surfaced here.
        var relsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relsEntry = archive.GetEntry(relsPath);
        if (relsEntry is null)
            return null;

        XDocument relsXml;
        try
        {
            relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
        }
        catch
        {
            return null;
        }

        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        foreach (var rel in relsXml.Root?.Elements(packageRelNs + "Relationship") ?? [])
        {
            if (!string.Equals(rel.Attribute("Type")?.Value, VmlDrawingRelationshipType, StringComparison.OrdinalIgnoreCase))
                continue;

            var target = rel.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target))
                continue;

            var resolved = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
        }

        return null;
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

        var checkedAttribute = formControlPr.Attribute("checked")?.Value;
        var model = new FormControlModel
        {
            Kind = MapObjectType(formControlPr.Attribute("objectType")?.Value),
            IsChecked = string.Equals(checkedAttribute, "Checked", StringComparison.OrdinalIgnoreCase),
            LinkedCell = NullIfWhiteSpace(formControlPr.Attribute("fmlaLink")?.Value),
            ListFillRange = NullIfWhiteSpace(formControlPr.Attribute("fmlaRange")?.Value),
            Value = ReadInt(formControlPr, "val"),
            Min = ReadInt(formControlPr, "min"),
            Max = ReadInt(formControlPr, "max"),
            Increment = ReadInt(formControlPr, "inc"),
            PageChange = ReadInt(formControlPr, "page"),
            SelectedIndex = ReadInt(formControlPr, "sel"),
        };

        // Checkboxes/option buttons never carry a "val" attribute (that's exclusive to
        // spinner/scrollbar controls), so Value is otherwise always null for these two kinds.
        // Reuse it to carry Excel's tri-state ST_Checked encoding (0=Unchecked/1=Checked/
        // 2=Mixed) so an indeterminate ("Mixed") checkbox/option-button is distinguishable from
        // a plain Unchecked one instead of being silently collapsed by the plain IsChecked bool
        // (R38-io-vml-form-controls-2-1). IsChecked itself stays false for "Mixed" (it cannot
        // represent a third state), but Value now preserves the distinction for any consumer
        // that inspects it -- including the native .fxl JSON round-trip, which already
        // serializes Value unconditionally for every FormControlModel.
        if (model.Value is null &&
            (model.Kind == FormControlKind.CheckBox || model.Kind == FormControlKind.OptionButton))
        {
            model.Value = checkedAttribute?.Trim().ToLowerInvariant() switch
            {
                "unchecked" => 0,
                "checked" => 1,
                "mixed" => 2,
                _ => null,
            };
        }

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
