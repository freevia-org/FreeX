using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxStylesheetSchemaNormalizer
{
    public static void Normalize(Stream xlsxStream)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        Normalize(archive);
    }

    public static void Normalize(ZipArchive archive)
    {
        var stylesEntry = archive.GetEntry("xl/styles.xml");
        if (stylesEntry is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var stylesXml = XlsxPackageXmlEditor.LoadXml(stylesEntry);
        if (!NormalizeStylesheet(stylesXml, workbookNs))
            return;

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/styles.xml", stylesXml);
    }

    internal static bool NormalizeStylesheet(XDocument stylesXml, XNamespace workbookNs)
    {
        var root = stylesXml.Root;
        if (root is null)
            return false;

        var changed = false;
        if (NormalizeStylesheetChildOrder(root, workbookNs))
            changed = true;

        if (root.Element(workbookNs + "colors") is { } colors &&
            NormalizeColorsChildOrder(colors, workbookNs))
        {
            changed = true;
        }

        foreach (var font in root.Element(workbookNs + "fonts")?.Elements(workbookNs + "font") ?? [])
        {
            if (NormalizeRegularFont(font, workbookNs))
                changed = true;
        }

        foreach (var dxf in root.Element(workbookNs + "dxfs")?.Elements(workbookNs + "dxf") ?? [])
        {
            if (XlsxAdvancedConditionalFormatWriter.NormalizeDifferentialStyleOrder(dxf, workbookNs))
                changed = true;
        }

        return changed;
    }

    private static bool NormalizeStylesheetChildOrder(XElement root, XNamespace workbookNs)
    {
        var orderedChildren = root.Elements()
            .Select((element, index) => new { Element = element, Index = index })
            .OrderBy(item => StylesheetChildOrder(item.Element, workbookNs))
            .ThenBy(item => item.Index)
            .Select(item => item.Element)
            .ToList();
        if (orderedChildren.Count == 0 || root.Elements().SequenceEqual(orderedChildren))
            return false;

        root.ReplaceNodes(orderedChildren);
        return true;
    }

    private static int StylesheetChildOrder(XElement element, XNamespace workbookNs) =>
        element.Name == workbookNs + "numFmts" ? 0 :
        element.Name == workbookNs + "fonts" ? 1 :
        element.Name == workbookNs + "fills" ? 2 :
        element.Name == workbookNs + "borders" ? 3 :
        element.Name == workbookNs + "cellStyleXfs" ? 4 :
        element.Name == workbookNs + "cellXfs" ? 5 :
        element.Name == workbookNs + "cellStyles" ? 6 :
        element.Name == workbookNs + "dxfs" ? 7 :
        element.Name == workbookNs + "tableStyles" ? 8 :
        element.Name == workbookNs + "colors" ? 9 :
        element.Name == workbookNs + "extLst" ? 100 :
        90;

    private static bool NormalizeColorsChildOrder(XElement colors, XNamespace workbookNs)
    {
        var orderedChildren = colors.Elements()
            .Select((element, index) => new { Element = element, Index = index })
            .OrderBy(item => ColorsChildOrder(item.Element, workbookNs))
            .ThenBy(item => item.Index)
            .Select(item => item.Element)
            .ToList();
        if (orderedChildren.Count == 0 || colors.Elements().SequenceEqual(orderedChildren))
            return false;

        colors.ReplaceNodes(orderedChildren);
        return true;
    }

    private static int ColorsChildOrder(XElement element, XNamespace workbookNs) =>
        element.Name == workbookNs + "indexedColors" ? 0 :
        element.Name == workbookNs + "mruColors" ? 1 :
        90;

    private static bool NormalizeRegularFont(XElement font, XNamespace workbookNs)
    {
        var changed = XlsxFontNameSanitizer.SanitizeValAttribute(font.Element(workbookNs + "name"));
        var orderedChildren = font.Elements()
            .OrderBy(element => RegularFontChildOrder(element, workbookNs))
            .ToList();
        if (orderedChildren.Count == 0)
            return changed;

        if (font.Elements().Select(element => element.Name).SequenceEqual(orderedChildren.Select(element => element.Name)))
            return changed;

        font.ReplaceNodes(orderedChildren);
        return true;
    }

    private static int RegularFontChildOrder(XElement element, XNamespace workbookNs) =>
        element.Name == workbookNs + "b" ? 0 :
        element.Name == workbookNs + "i" ? 1 :
        element.Name == workbookNs + "strike" ? 2 :
        element.Name == workbookNs + "condense" ? 3 :
        element.Name == workbookNs + "extend" ? 4 :
        element.Name == workbookNs + "outline" ? 5 :
        element.Name == workbookNs + "shadow" ? 6 :
        element.Name == workbookNs + "u" ? 7 :
        element.Name == workbookNs + "vertAlign" ? 8 :
        element.Name == workbookNs + "sz" ? 9 :
        element.Name == workbookNs + "color" ? 10 :
        element.Name == workbookNs + "name" ? 11 :
        element.Name == workbookNs + "charset" ? 12 :
        element.Name == workbookNs + "family" ? 13 :
        element.Name == workbookNs + "scheme" ? 14 :
        90;
}
