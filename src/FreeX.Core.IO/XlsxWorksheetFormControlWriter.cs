using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Writes the package graph for modeled legacy worksheet form controls. ClosedXML does not expose
/// these controls, so a newly created control needs its worksheet <c>controls</c> entry, ctrlProp
/// part, VML shape, relationships, and content-type metadata authored after ClosedXML saves.
/// ActiveX controls are deliberately out of scope: this writes only the legacy controls represented
/// by <see cref="FormControlModel"/>.
/// </summary>
internal static class XlsxWorksheetFormControlWriter
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace FormControlNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace ExcelVmlNs = "urn:schemas-microsoft-com:office:excel";

    private const string VmlDrawingRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";
    private const string ControlPropertiesRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp";
    private const string ControlPropertiesContentType = "application/vnd.ms-excel.controlproperties+xml";
    private const string VmlContentType = "application/vnd.openxmlformats-officedocument.vmlDrawing";
    private const long EmusPerPixel = 9525;

    public static bool HasPersistableControls(Workbook workbook) =>
        workbook.Sheets.Any(sheet => HasPersistableControls(sheet.FormControls));

    public static bool HasPersistableControls(IEnumerable<FormControlModel> controls) =>
        controls.Any(IsPersistable);

    public static void Save(Stream packageStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (!HasPersistableControls(workbook))
            return;

        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        Save(archive, workbook, worksheetPathMap);
    }

    public static void Save(ZipArchive archive, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (!HasPersistableControls(workbook))
            return;

        worksheetPathMap ??= XlsxWorkbookWorksheetPathMap.TryCreate(archive);
        if (worksheetPathMap is null)
            return;

        foreach (var sheet in workbook.Sheets)
        {
            if (!sheet.FormControls.Any(IsPersistable) ||
                !worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
            {
                continue;
            }

            WriteSheet(archive, worksheetPath, sheet);
        }
    }

    private static void WriteSheet(ZipArchive archive, string worksheetPath, Sheet sheet)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
        var worksheetRoot = worksheetXml.Root;
        if (worksheetRoot is null)
            return;

        var relationshipsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relationshipsEntry = archive.GetEntry(relationshipsPath);
        var relationshipsXml = relationshipsEntry is null
            ? new XDocument(new XElement(PackageRelNs + "Relationships"))
            : XlsxPackageXmlEditor.LoadXml(relationshipsEntry);

        var controls = FindControlsContainer(worksheetRoot) ?? new XElement(WorksheetNs + "controls");
        var controlsWerePresent = controls.Parent is not null;
        var existingShapeIds = EnumerateControlElements(controls)
            .Select(element => uint.TryParse(element.Attribute("shapeId")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? (uint?)id
                : null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();

        var vml = GetOrCreateVmlDocument(archive, worksheetPath, worksheetRoot, relationshipsXml, out var vmlPath, out var vmlRelationshipId);
        if (vml is null || vmlPath is null || vmlRelationshipId is null)
            return;

        var vmlRoot = vml.Root!;
        EnsureVmlNamespaces(vmlRoot);
        var changed = false;
        foreach (var control in sheet.FormControls)
        {
            if (!IsPersistable(control))
                continue;

            if (control.ShapeId is { } existingShapeId && existingShapeIds.Contains(existingShapeId))
                continue;

            var shapeId = control.ShapeId is { } requested && !existingShapeIds.Contains(requested)
                ? requested
                : AllocateShapeId(existingShapeIds);
            control.ShapeId = shapeId;
            existingShapeIds.Add(shapeId);
            control.Name ??= BuildDefaultName(control.Kind, shapeId);

            var ctrlPropPath = AllocateCtrlPropPath(archive);
            var ctrlRelationshipId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                relationshipsXml,
                PackageRelNs,
                worksheetPath,
                ctrlPropPath,
                ControlPropertiesRelationshipType);
            WriteCtrlPropPart(archive, ctrlPropPath, control);
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, ctrlPropPath, ControlPropertiesContentType);

            controls.Add(BuildControlElement(control, shapeId, ctrlRelationshipId));
            vmlRoot.Add(BuildVmlShape(control, shapeId));
            changed = true;
        }

        if (!changed)
            return;

        if (!controlsWerePresent)
            InsertControlsInWorksheetOrder(worksheetRoot, controls);

        var legacyDrawing = worksheetRoot.Element(WorksheetNs + "legacyDrawing");
        if (legacyDrawing is null)
        {
            legacyDrawing = new XElement(WorksheetNs + "legacyDrawing");
            InsertLegacyDrawingInWorksheetOrder(worksheetRoot, legacyDrawing);
        }
        legacyDrawing.SetAttributeValue(RelNs + "id", vmlRelationshipId);
        worksheetRoot.SetAttributeValue(XNamespace.Xmlns + "r", RelNs.NamespaceName);

        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        XlsxPackageXmlEditor.ReplaceXml(archive, relationshipsPath, relationshipsXml);
        XlsxPackageXmlEditor.ReplaceXml(archive, vmlPath, vml);
        XlsxPackageXmlEditor.EnsureDefaultContentType(archive, "vml", VmlContentType);
    }

    private static XDocument? GetOrCreateVmlDocument(
        ZipArchive archive,
        string worksheetPath,
        XElement worksheetRoot,
        XDocument relationshipsXml,
        out string? vmlPath,
        out string? vmlRelationshipId)
    {
        vmlPath = null;
        vmlRelationshipId = null;
        var legacyDrawingRelationshipId = worksheetRoot.Element(WorksheetNs + "legacyDrawing")?.Attribute(RelNs + "id")?.Value;
        var existingRelationship = relationshipsXml.Root?
            .Elements(PackageRelNs + "Relationship")
            .FirstOrDefault(relationship =>
                string.Equals(relationship.Attribute("Id")?.Value, legacyDrawingRelationshipId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(relationship.Attribute("Type")?.Value, VmlDrawingRelationshipType, StringComparison.OrdinalIgnoreCase));
        existingRelationship ??= relationshipsXml.Root?
            .Elements(PackageRelNs + "Relationship")
            .FirstOrDefault(relationship => string.Equals(
                relationship.Attribute("Type")?.Value,
                VmlDrawingRelationshipType,
                StringComparison.OrdinalIgnoreCase));

        if (existingRelationship is not null)
        {
            var target = existingRelationship.Attribute("Target")?.Value;
            var resolved = string.IsNullOrWhiteSpace(target) ? null : XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
            var entry = resolved is null ? null : archive.GetEntry(resolved);
            if (entry is not null)
            {
                try
                {
                    vmlPath = resolved;
                    vmlRelationshipId = existingRelationship.Attribute("Id")?.Value;
                    return XlsxPackageXmlEditor.LoadXml(entry);
                }
                catch
                {
                    // A malformed pre-existing VML part must not be overwritten by a newly authored
                    // legacy control; leave the sheet unchanged rather than risk comments or drawings.
                    return null;
                }
            }
        }

        vmlPath = AllocateVmlPath(archive);
        vmlRelationshipId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
            relationshipsXml,
            PackageRelNs,
            worksheetPath,
            vmlPath,
            VmlDrawingRelationshipType);
        return new XDocument(new XElement("xml",
            new XAttribute(XNamespace.Xmlns + "v", VmlNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "x", ExcelVmlNs.NamespaceName)));
    }

    private static void WriteCtrlPropPart(ZipArchive archive, string ctrlPropPath, FormControlModel control)
    {
        var definition = GetDefinition(control.Kind)!;
        var properties = new XElement(FormControlNs + "formControlPr",
            new XAttribute("objectType", definition.CtrlPropObjectType));
        if (!string.IsNullOrWhiteSpace(control.LinkedCell))
            properties.SetAttributeValue("fmlaLink", control.LinkedCell);
        if (!string.IsNullOrWhiteSpace(control.ListFillRange))
            properties.SetAttributeValue("fmlaRange", control.ListFillRange);

        switch (control.Kind)
        {
            case FormControlKind.CheckBox:
            case FormControlKind.OptionButton:
                properties.SetAttributeValue("checked", control.Value == 2 ? "Mixed" : control.IsChecked ? "Checked" : "Unchecked");
                break;
            case FormControlKind.Spinner:
            case FormControlKind.ScrollBar:
                SetOptionalInt(properties, "val", control.Value);
                SetOptionalInt(properties, "min", control.Min);
                SetOptionalInt(properties, "max", control.Max);
                SetOptionalInt(properties, "inc", control.Increment);
                SetOptionalInt(properties, "page", control.PageChange);
                break;
            case FormControlKind.ListBox:
            case FormControlKind.DropDown:
                SetOptionalInt(properties, "sel", control.SelectedIndex);
                break;
        }

        XlsxPackageXmlEditor.ReplaceXml(archive, ctrlPropPath, new XDocument(properties));
    }

    private static XElement BuildControlElement(FormControlModel control, uint shapeId, string relationshipId)
    {
        var anchor = control.Anchor!.Value;
        var offsets = control.AnchorOffsets;
        return new XElement(WorksheetNs + "control",
            new XAttribute("shapeId", shapeId.ToString(CultureInfo.InvariantCulture)),
            new XAttribute(RelNs + "id", relationshipId),
            new XAttribute("name", control.Name!),
            new XElement(WorksheetNs + "controlPr",
                new XAttribute("defaultSize", "0"),
                new XAttribute("autoFill", "0"),
                new XAttribute("autoLine", "0"),
                new XAttribute("autoPict", "0"),
                new XElement(WorksheetNs + "anchor",
                    BuildAnchorMarker("from", anchor.Start.Col - 1, anchor.Start.Row - 1, offsets?.From),
                    BuildAnchorMarker("to", anchor.End.Col - 1, anchor.End.Row - 1, offsets?.To))));
    }

    private static XElement BuildAnchorMarker(string name, uint col, uint row, DrawingAnchorPoint? offset) =>
        new(WorksheetNs + name,
            new XElement(DrawingNs + "col", col.ToString(CultureInfo.InvariantCulture)),
            new XElement(DrawingNs + "colOff", (offset?.ColumnOffsetEmu ?? 0).ToString(CultureInfo.InvariantCulture)),
            new XElement(DrawingNs + "row", row.ToString(CultureInfo.InvariantCulture)),
            new XElement(DrawingNs + "rowOff", (offset?.RowOffsetEmu ?? 0).ToString(CultureInfo.InvariantCulture)));

    private static XElement BuildVmlShape(FormControlModel control, uint shapeId)
    {
        var definition = GetDefinition(control.Kind)!;
        var shape = new XElement(VmlNs + "shape",
            new XAttribute("id", "_x0000_s" + shapeId.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("type", "#_x0000_t201"),
            new XAttribute("style", "position:absolute;margin-left:0;margin-top:0;width:96pt;height:15pt;z-index:" + shapeId.ToString(CultureInfo.InvariantCulture)));
        if (!string.IsNullOrWhiteSpace(control.Caption))
        {
            shape.Add(new XElement(VmlNs + "textbox",
                new XElement("div", control.Caption)));
        }

        var clientData = new XElement(ExcelVmlNs + "ClientData",
            new XAttribute("ObjectType", definition.VmlObjectType),
            new XElement(ExcelVmlNs + "Anchor", BuildVmlAnchor(control.Anchor!.Value, control.AnchorOffsets)));
        if (!string.IsNullOrWhiteSpace(control.LinkedCell))
            clientData.Add(new XElement(ExcelVmlNs + "FmlaLink", control.LinkedCell));
        if (!string.IsNullOrWhiteSpace(control.ListFillRange))
            clientData.Add(new XElement(ExcelVmlNs + "FmlaRange", control.ListFillRange));

        switch (control.Kind)
        {
            case FormControlKind.CheckBox:
            case FormControlKind.OptionButton:
                clientData.Add(new XElement(ExcelVmlNs + "Checked", control.Value == 2 ? "2" : control.IsChecked ? "1" : "0"));
                break;
            case FormControlKind.Spinner:
            case FormControlKind.ScrollBar:
                AddOptionalVmlInt(clientData, "Val", control.Value);
                AddOptionalVmlInt(clientData, "Min", control.Min);
                AddOptionalVmlInt(clientData, "Max", control.Max);
                AddOptionalVmlInt(clientData, "Inc", control.Increment);
                AddOptionalVmlInt(clientData, "Page", control.PageChange);
                break;
            case FormControlKind.ListBox:
            case FormControlKind.DropDown:
                AddOptionalVmlInt(clientData, "Sel", control.SelectedIndex);
                break;
        }

        shape.Add(clientData);
        return shape;
    }

    private static string BuildVmlAnchor(GridRange anchor, DrawingAnchorRange? offsets) => string.Join(",",
        (anchor.Start.Col - 1).ToString(CultureInfo.InvariantCulture),
        EmuToPixels(offsets?.From.ColumnOffsetEmu ?? 0).ToString(CultureInfo.InvariantCulture),
        (anchor.Start.Row - 1).ToString(CultureInfo.InvariantCulture),
        EmuToPixels(offsets?.From.RowOffsetEmu ?? 0).ToString(CultureInfo.InvariantCulture),
        (anchor.End.Col - 1).ToString(CultureInfo.InvariantCulture),
        EmuToPixels(offsets?.To.ColumnOffsetEmu ?? 0).ToString(CultureInfo.InvariantCulture),
        (anchor.End.Row - 1).ToString(CultureInfo.InvariantCulture),
        EmuToPixels(offsets?.To.RowOffsetEmu ?? 0).ToString(CultureInfo.InvariantCulture));

    private static long EmuToPixels(long emu) =>
        Math.Max(0, (long)Math.Round(emu / (double)EmusPerPixel, MidpointRounding.AwayFromZero));

    private static void SetOptionalInt(XElement element, string name, int? value)
    {
        if (value is { } number)
            element.SetAttributeValue(name, number.ToString(CultureInfo.InvariantCulture));
    }

    private static void AddOptionalVmlInt(XElement element, string name, int? value)
    {
        if (value is { } number)
            element.Add(new XElement(ExcelVmlNs + name, number.ToString(CultureInfo.InvariantCulture)));
    }

    private static bool IsPersistable(FormControlModel control) =>
        control.Anchor is not null && GetDefinition(control.Kind) is not null;

    private static ControlDefinition? GetDefinition(FormControlKind kind) => kind switch
    {
        FormControlKind.Button => new("Button", "Button"),
        FormControlKind.CheckBox => new("CheckBox", "Checkbox"),
        FormControlKind.OptionButton => new("Radio", "Radio"),
        FormControlKind.DropDown => new("Drop", "Drop"),
        FormControlKind.ListBox => new("List", "List"),
        FormControlKind.GroupBox => new("GBox", "GBox"),
        FormControlKind.Label => new("Label", "Label"),
        FormControlKind.ScrollBar => new("Scroll", "Scroll"),
        FormControlKind.Spinner => new("Spin", "Spin"),
        _ => null
    };

    private static string BuildDefaultName(FormControlKind kind, uint shapeId) => kind switch
    {
        FormControlKind.CheckBox => "Check Box " + shapeId.ToString(CultureInfo.InvariantCulture),
        FormControlKind.OptionButton => "Option Button " + shapeId.ToString(CultureInfo.InvariantCulture),
        FormControlKind.DropDown => "Drop Down " + shapeId.ToString(CultureInfo.InvariantCulture),
        FormControlKind.ListBox => "List Box " + shapeId.ToString(CultureInfo.InvariantCulture),
        FormControlKind.GroupBox => "Group Box " + shapeId.ToString(CultureInfo.InvariantCulture),
        FormControlKind.ScrollBar => "Scroll Bar " + shapeId.ToString(CultureInfo.InvariantCulture),
        FormControlKind.Spinner => "Spin Button " + shapeId.ToString(CultureInfo.InvariantCulture),
        _ => kind + " " + shapeId.ToString(CultureInfo.InvariantCulture)
    };

    private static uint AllocateShapeId(IReadOnlySet<uint> usedShapeIds)
    {
        for (var candidate = 1025u; candidate < uint.MaxValue; candidate++)
        {
            if (!usedShapeIds.Contains(candidate))
                return candidate;
        }

        throw new InvalidOperationException("No legacy form-control shape id is available.");
    }

    private static string AllocateCtrlPropPath(ZipArchive archive)
    {
        for (var index = 1; ; index++)
        {
            var path = $"xl/ctrlProps/ctrlProp{index}.xml";
            if (archive.GetEntry(path) is null)
                return path;
        }
    }

    private static string AllocateVmlPath(ZipArchive archive)
    {
        for (var index = 1; ; index++)
        {
            var path = $"xl/drawings/vmlDrawing{index}.vml";
            if (archive.GetEntry(path) is null)
                return path;
        }
    }

    private static XElement? FindControlsContainer(XElement worksheetRoot) =>
        worksheetRoot.Descendants(WorksheetNs + "controls").FirstOrDefault();

    private static IEnumerable<XElement> EnumerateControlElements(XElement element)
    {
        foreach (var child in element.DescendantsAndSelf(WorksheetNs + "control"))
            yield return child;
    }

    private static void EnsureVmlNamespaces(XElement root)
    {
        if (root.GetNamespaceOfPrefix("v") is null)
            root.SetAttributeValue(XNamespace.Xmlns + "v", VmlNs.NamespaceName);
        if (root.GetNamespaceOfPrefix("x") is null)
            root.SetAttributeValue(XNamespace.Xmlns + "x", ExcelVmlNs.NamespaceName);
    }

    private static void InsertControlsInWorksheetOrder(XElement root, XElement controls)
    {
        var later = root.Elements().FirstOrDefault(element =>
            element.Name == WorksheetNs + "webPublishItems" ||
            element.Name == WorksheetNs + "tableParts" ||
            element.Name == WorksheetNs + "extLst");
        if (later is null)
            root.Add(controls);
        else
            later.AddBeforeSelf(controls);
    }

    private static void InsertLegacyDrawingInWorksheetOrder(XElement root, XElement marker)
    {
        var later = root.Elements().FirstOrDefault(element =>
            element.Name == WorksheetNs + "legacyDrawingHF" ||
            element.Name == WorksheetNs + "picture" ||
            element.Name == WorksheetNs + "oleObjects" ||
            element.Name == WorksheetNs + "controls" ||
            element.Name == WorksheetNs + "webPublishItems" ||
            element.Name == WorksheetNs + "tableParts" ||
            element.Name == WorksheetNs + "extLst");
        if (later is null)
            root.Add(marker);
        else
            later.AddBeforeSelf(marker);
    }

    private sealed record ControlDefinition(string CtrlPropObjectType, string VmlObjectType);
}
