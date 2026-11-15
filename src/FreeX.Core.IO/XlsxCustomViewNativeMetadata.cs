using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>Preserves unmodeled attributes on surviving custom views after re-authoring.</summary>
internal sealed class XlsxCustomViewNativeMetadata
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly HashSet<string> WorkbookAttributes =
    [
        "autoUpdate", "mergeInterval", "changesSavedWin", "onlySync", "personalView",
        "maximized", "minimized", "showHorizontalScroll", "showVerticalScroll", "showSheetTabs",
        "xWindow", "yWindow", "windowWidth", "windowHeight", "tabRatio",
        "showFormulaBar", "showStatusbar", "showComments", "showObjects"
    ];
    private static readonly HashSet<string> SheetBooleanAttributes =
        ["outlineSymbols", "showPageBreaks", "zeroValues"];

    private readonly Dictionary<string, Dictionary<string, string>> _workbook = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(SheetId Sheet, string Id), Dictionary<string, string>> _sheets = [];
    private readonly Dictionary<string, XElement> _workbookExtensions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(SheetId Sheet, string Id), XElement> _sheetExtensions = [];

    public static XlsxCustomViewNativeMetadata Capture(Stream source, IReadOnlyList<SheetId> sourceSheetIds)
    {
        var result = new XlsxCustomViewNativeMetadata();
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml");
        if (entry is null) return result;
        var xml = XlsxPackageXmlEditor.LoadXml(entry);
        foreach (var sourceView in xml.Root?.Element(Ns + "customWorkbookViews")?.Elements(Ns + "customWorkbookView") ?? [])
        {
            var view = new XElement(sourceView);
            XlsxWorkbookCustomViewNormalizer.NormalizeCustomWorkbookViewElement(view);
            var id = XlsxCustomViewMapper.NormalizeId(view.Attribute("guid")?.Value);
            if (id is null) continue;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var attribute in view.Attributes())
            {
                if (attribute.Name.Namespace != XNamespace.None || !WorkbookAttributes.Contains(attribute.Name.LocalName)) continue;
                if (attribute.Name.LocalName is "windowWidth" or "windowHeight" &&
                    (!uint.TryParse(attribute.Value, out var size) || size == 0)) continue;
                values[attribute.Name.LocalName] = attribute.Value;
            }
            result._workbook[id] = values;
            if (CaptureCompatibleExtensions(view) is { } extensions)
                result._workbookExtensions[id] = extensions;
        }

        var relationships = XlsxRelationshipReader.LoadTargets(archive, "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml", PackageRelNs);
        var sheets = xml.Root?.Element(Ns + "sheets")?.Elements(Ns + "sheet").ToList() ?? [];
        for (var index = 0; index < sheets.Count && index < sourceSheetIds.Count; index++)
        {
            var relationship = sheets[index].Attribute(RelNs + "id")?.Value;
            if (relationship is null || !relationships.TryGetValue(relationship, out var path)) continue;
            var sheetEntry = archive.GetEntry(path);
            if (sheetEntry is null) continue;
            var sheetXml = XlsxPackageXmlEditor.LoadXml(sheetEntry);
            foreach (var view in sheetXml.Root?.Element(Ns + "customSheetViews")?.Elements(Ns + "customSheetView") ?? [])
            {
                var id = XlsxCustomViewMapper.NormalizeId(view.Attribute("guid")?.Value);
                if (id is null) continue;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var attribute in view.Attributes())
                {
                    if (attribute.Name.Namespace != XNamespace.None) continue;
                    var name = attribute.Name.LocalName;
                    if (SheetBooleanAttributes.Contains(name))
                    {
                        var normalized = XlsxXmlNormalizationHelpers.NormalizeBoolean(attribute.Value);
                        if (normalized is not null) values[name] = normalized;
                    }
                    else if (name == "colorId" && uint.TryParse(attribute.Value, out var color))
                        values[name] = color.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                result._sheets[(sourceSheetIds[index], id)] = values;
                if (CaptureCompatibleExtensions(view) is { } extensions)
                    result._sheetExtensions[(sourceSheetIds[index], id)] = extensions;
            }
        }
        return result;
    }

    public void Apply(Stream target, Workbook workbook)
    {
        using var archive = new ZipArchive(target, ZipArchiveMode.Update, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml");
        if (entry is null) return;
        var xml = XlsxPackageXmlEditor.LoadXml(entry);
        var changed = false;
        foreach (var view in xml.Root?.Element(Ns + "customWorkbookViews")?.Elements(Ns + "customWorkbookView") ?? [])
        {
            var id = XlsxCustomViewMapper.NormalizeId(view.Attribute("guid")?.Value);
            if (id is null || !_workbook.TryGetValue(id, out var values)) continue;
            foreach (var (name, value) in values) { view.SetAttributeValue(name, value); changed = true; }
            if (_workbookExtensions.TryGetValue(id, out var extensions))
                changed |= MergeExtensions(view, extensions);
        }
        if (changed) XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", xml);

        var relationships = XlsxRelationshipReader.LoadTargets(archive, "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml", PackageRelNs);
        var paths = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(xml, relationships, Ns, RelNs)
            .ToDictionary(item => item.SheetName, item => item.WorksheetPath, StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in workbook.Sheets)
        {
            if (!paths.TryGetValue(sheet.Name, out var path)) continue;
            var sheetEntry = archive.GetEntry(path);
            if (sheetEntry is null) continue;
            var sheetXml = XlsxPackageXmlEditor.LoadXml(sheetEntry);
            var sheetChanged = false;
            foreach (var view in sheetXml.Root?.Element(Ns + "customSheetViews")?.Elements(Ns + "customSheetView") ?? [])
            {
                var id = XlsxCustomViewMapper.NormalizeId(view.Attribute("guid")?.Value);
                if (id is null || !_sheets.TryGetValue((sheet.Id, id), out var values)) continue;
                foreach (var (name, value) in values) { view.SetAttributeValue(name, value); sheetChanged = true; }
                if (_sheetExtensions.TryGetValue((sheet.Id, id), out var extensions))
                    sheetChanged |= MergeExtensions(view, extensions);
            }
            if (sheetChanged) XlsxPackageXmlEditor.ReplaceXml(archive, path, sheetXml);
        }
    }

    private static XElement? CaptureCompatibleExtensions(XElement view)
    {
        var source = view.Element(Ns + "extLst");
        if (source is null) return null;
        var copy = new XElement(source);
        XlsxExtensionListNormalizer.NormalizeElement(copy, Ns);
        foreach (var extension in copy.Elements(Ns + "ext").ToList())
        {
            // An r:id refers to a package relationship which may have been removed or
            // renumbered during the rebuild. MC directives can name namespace prefixes
            // inherited from ancestors and cannot safely be transplanted in isolation.
            if (extension.DescendantsAndSelf().Attributes().Any(attribute =>
                    attribute.Name.NamespaceName.Contains("/relationships", StringComparison.Ordinal) ||
                    attribute.Name.NamespaceName == "http://schemas.openxmlformats.org/markup-compatibility/2006"))
                extension.Remove();
        }
        return XlsxExtensionListNormalizer.ShouldRemove(copy, Ns) ? null : copy;
    }

    private static bool MergeExtensions(XElement view, XElement captured)
    {
        var target = view.Element(Ns + "extLst");
        if (target is null)
        {
            view.Add(new XElement(captured));
            return true;
        }
        var existingUris = target.Elements(Ns + "ext")
            .Select(element => element.Attribute("uri")?.Value)
            .Where(uri => uri is not null)
            .ToHashSet(StringComparer.Ordinal);
        var changed = false;
        foreach (var extension in captured.Elements(Ns + "ext"))
        {
            var uri = extension.Attribute("uri")?.Value;
            if (uri is null || !existingUris.Add(uri)) continue;
            target.Add(new XElement(extension));
            changed = true;
        }
        return changed;
    }
}
