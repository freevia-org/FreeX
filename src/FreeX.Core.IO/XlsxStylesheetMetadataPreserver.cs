using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxStylesheetMetadataPreserver
{
    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var sourceStylesEntry = sourceArchive.GetEntry("xl/styles.xml");
        var targetStylesEntry = targetArchive.GetEntry("xl/styles.xml");
        if (sourceStylesEntry is null || targetStylesEntry is null)
            return;
        if (!HasPreservableStylesheetMetadata(sourceStylesEntry))
            return;

        var sourceStylesXml = XlsxPackageXmlEditor.LoadXml(sourceStylesEntry);
        var targetStylesXml = XlsxPackageXmlEditor.LoadXml(targetStylesEntry);
        var targetRoot = targetStylesXml.Root;
        if (targetRoot is null)
            return;

        var changed = false;
        if (MergeStylesheetColors(sourceStylesXml.Root?.Element(workbookNs + "colors"), targetRoot, workbookNs))
            changed = true;
        if (MergeStylesheetDifferentialStyles(sourceStylesXml.Root?.Element(workbookNs + "dxfs"), targetRoot, workbookNs))
            changed = true;
        if (MergeStylesheetTableStyles(sourceStylesXml.Root?.Element(workbookNs + "tableStyles"), targetRoot, workbookNs))
            changed = true;
        if (XlsxNativeXmlMerger.MergeExtensionList(sourceStylesXml.Root?.Element(workbookNs + "extLst"), targetRoot, workbookNs))
            changed = true;

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, "xl/styles.xml", targetStylesXml);
    }

    private static bool HasPreservableStylesheetMetadata(ZipArchiveEntry sourceStylesEntry)
    {
        try
        {
            using var stream = sourceStylesEntry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true
            });

            var stylesheetDepth = -1;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                if (stylesheetDepth < 0)
                {
                    if (reader.LocalName == "styleSheet" &&
                        reader.NamespaceURI == "http://schemas.openxmlformats.org/spreadsheetml/2006/main")
                    {
                        stylesheetDepth = reader.Depth;
                    }

                    continue;
                }

                if (reader.Depth != stylesheetDepth + 1 ||
                    reader.NamespaceURI != "http://schemas.openxmlformats.org/spreadsheetml/2006/main")
                {
                    continue;
                }

                switch (reader.LocalName)
                {
                    case "colors":
                    case "extLst":
                        return true;
                    case "dxfs":
                        if (HasPreservableDifferentialStyles(reader))
                            return true;
                        break;
                    case "tableStyles":
                        if (HasPreservableTableStyles(reader))
                            return true;
                        break;
                }
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool HasPreservableDifferentialStyles(XmlReader reader)
    {
        if (HasNativeOnlyAttributes(reader, "count"))
            return true;
        if (reader.IsEmptyElement)
            return false;

        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType == XmlNodeType.Element && subtree.Depth > 0)
                return true;
        }

        return false;
    }

    private static bool HasPreservableTableStyles(XmlReader reader)
    {
        if (reader.HasAttributes)
        {
            for (var i = 0; i < reader.AttributeCount; i++)
            {
                reader.MoveToAttribute(i);
                if (IsNamespaceDeclaration(reader))
                    continue;

                if (reader.LocalName == "count")
                {
                    if (!string.Equals(reader.Value, "0", StringComparison.Ordinal))
                    {
                        reader.MoveToElement();
                        return true;
                    }

                    continue;
                }

                if (reader.LocalName == "defaultTableStyle")
                {
                    if (!string.Equals(reader.Value, "TableStyleMedium2", StringComparison.Ordinal))
                    {
                        reader.MoveToElement();
                        return true;
                    }

                    continue;
                }

                if (reader.LocalName == "defaultPivotStyle")
                {
                    if (!string.Equals(reader.Value, "PivotStyleLight16", StringComparison.Ordinal))
                    {
                        reader.MoveToElement();
                        return true;
                    }

                    continue;
                }

                reader.MoveToElement();
                return true;
            }

            reader.MoveToElement();
        }

        if (reader.IsEmptyElement)
            return false;

        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType == XmlNodeType.Element && subtree.Depth > 0)
                return true;
        }

        return false;
    }

    private static bool HasNativeOnlyAttributes(XmlReader reader, params string[] modeledLocalNames)
    {
        if (!reader.HasAttributes)
            return false;

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            if (!IsNamespaceDeclaration(reader) &&
                !modeledLocalNames.Contains(reader.LocalName, StringComparer.Ordinal))
            {
                reader.MoveToElement();
                return true;
            }
        }

        reader.MoveToElement();
        return false;
    }

    private static bool IsNamespaceDeclaration(XmlReader reader) =>
        reader.Prefix == "xmlns" ||
        (reader.Prefix.Length == 0 && reader.LocalName == "xmlns");

    private static bool MergeStylesheetColors(XElement? sourceColors, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceColors is null)
            return false;

        var targetColors = targetRoot.Element(workbookNs + "colors");
        if (targetColors is null)
        {
            targetRoot.Add(new XElement(sourceColors));
            return true;
        }

        return XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceColors, targetColors);
    }

    private static bool MergeStylesheetDifferentialStyles(XElement? sourceDifferentialStyles, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceDifferentialStyles is null)
            return false;

        var targetDifferentialStyles = targetRoot.Element(workbookNs + "dxfs");
        if (targetDifferentialStyles is null)
        {
            targetRoot.Add(new XElement(sourceDifferentialStyles));
            return true;
        }

        var changed = XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceDifferentialStyles, targetDifferentialStyles);
        var targetStyles = targetDifferentialStyles.Elements(workbookNs + "dxf").ToList();
        foreach (var (sourceStyle, index) in sourceDifferentialStyles.Elements(workbookNs + "dxf").Select((style, index) => (style, index)))
        {
            if (index >= targetStyles.Count)
            {
                targetDifferentialStyles.Add(new XElement(sourceStyle));
                targetStyles.Add(targetDifferentialStyles.Elements(workbookNs + "dxf").Last());
                changed = true;
                continue;
            }

            if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceStyle, targetStyles[index]))
                changed = true;
        }

        targetDifferentialStyles.SetAttributeValue(
            "count",
            targetDifferentialStyles.Elements(workbookNs + "dxf").Count().ToString(CultureInfo.InvariantCulture));
        return changed;
    }

    private static bool MergeStylesheetTableStyles(XElement? sourceTableStyles, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceTableStyles is null)
            return false;

        var targetTableStyles = targetRoot.Element(workbookNs + "tableStyles");
        if (targetTableStyles is null)
        {
            targetRoot.Add(new XElement(sourceTableStyles));
            return true;
        }

        var changed = false;
        foreach (var attribute in sourceTableStyles.Attributes())
        {
            if (targetTableStyles.Attribute(attribute.Name)?.Value == attribute.Value)
                continue;

            targetTableStyles.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var targetStylesByName = targetTableStyles
            .Elements(workbookNs + "tableStyle")
            .Select(element => (Name: element.Attribute("name")?.Value, Element: element))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Name))
            .ToDictionary(pair => pair.Name!, pair => pair.Element, StringComparer.OrdinalIgnoreCase);
        foreach (var sourceStyle in sourceTableStyles.Elements(workbookNs + "tableStyle"))
        {
            var name = sourceStyle.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name) || !targetStylesByName.TryGetValue(name, out var targetStyle))
            {
                targetTableStyles.Add(new XElement(sourceStyle));
                if (!string.IsNullOrWhiteSpace(name))
                    targetStylesByName[name] = targetTableStyles.Elements(workbookNs + "tableStyle").Last();
                changed = true;
                continue;
            }

            if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceStyle, targetStyle))
                changed = true;
        }

        if (MergeTableStylesNativeChildren(sourceTableStyles, targetTableStyles, workbookNs))
            changed = true;

        targetTableStyles.SetAttributeValue(
            "count",
            targetTableStyles.Elements(workbookNs + "tableStyle").Count().ToString(CultureInfo.InvariantCulture));
        return changed;
    }

    private static bool MergeTableStylesNativeChildren(
        XElement sourceTableStyles,
        XElement targetTableStyles,
        XNamespace workbookNs)
    {
        var targetChildrenByKey = targetTableStyles
            .Elements()
            .Where(child => child.Name != workbookNs + "tableStyle")
            .GroupBy(NativeChildKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var changed = false;
        foreach (var sourceChild in sourceTableStyles.Elements().Where(child => child.Name != workbookNs + "tableStyle"))
        {
            var key = NativeChildKey(sourceChild);
            if (targetChildrenByKey.TryGetValue(key, out var targetChild))
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                    changed = true;
                continue;
            }

            targetTableStyles.Add(new XElement(sourceChild));
            targetChildrenByKey[key] = targetTableStyles.Elements().Last();
            changed = true;
        }

        return changed;
    }

    private static string NativeChildKey(XElement element)
    {
        var identity = element.Attribute("name")?.Value
            ?? element.Attribute("id")?.Value
            ?? element.Attribute("uid")?.Value
            ?? element.Attribute("uri")?.Value
            ?? string.Empty;
        return $"{element.Name}\u001f{identity}";
    }
}
