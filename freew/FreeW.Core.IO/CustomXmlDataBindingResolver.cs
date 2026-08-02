using System.Collections;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Free.Shared.Opc;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// Resolves Word content-control bindings against preserved <c>customXml</c> data-store items.
/// </summary>
public static class CustomXmlDataBindingResolver
{
    private static readonly XNamespace DataStoreNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/customXml";

    /// <summary>
    /// Refreshes successfully resolved plain-text content controls and leaves every unresolved control unchanged.
    /// Returns the number of distinct controls whose display text was updated.
    /// </summary>
    public static int RefreshBoundPlainTextControls(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var stores = BuildDataStores(document.Preserved.Parts);
        if (stores.Count == 0)
            return 0;

        var refreshed = 0;
        foreach (var paragraph in EnumerateStoryParagraphs(document))
            refreshed += RefreshInlineControls(paragraph, stores);

        refreshed += RefreshBodyBlockControls(document.Blocks, stores);
        return refreshed;
    }

    /// <summary>Attempts to evaluate one binding against the document's preserved custom XML data store.</summary>
    public static bool TryResolve(
        TextDocument document,
        ContentControlDataBinding binding,
        out string value)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(binding);
        return TryResolve(BuildDataStores(document.Preserved.Parts), binding, out value);
    }

    private static int RefreshInlineControls(
        Paragraph paragraph,
        IReadOnlyDictionary<string, XDocument> stores)
    {
        var refreshed = 0;
        for (var start = 0; start < paragraph.Runs.Count;)
        {
            var control = paragraph.Runs[start].Control;
            var end = start + 1;
            while (end < paragraph.Runs.Count && ReferenceEquals(paragraph.Runs[end].Control, control))
                end++;

            if (control is { Kind: ContentControlKind.PlainText, WordMetadata.DataBinding: { } binding }
                && TryResolve(stores, binding, out var value))
            {
                paragraph.Runs[start].Text = value;
                for (var index = start + 1; index < end; index++)
                    paragraph.Runs[index].Text = string.Empty;
                refreshed++;
            }

            start = end;
        }

        return refreshed;
    }

    private static int RefreshBodyBlockControls(
        IReadOnlyList<Block> blocks,
        IReadOnlyDictionary<string, XDocument> stores)
    {
        var refreshed = 0;
        for (var start = 0; start < blocks.Count;)
        {
            var control = blocks[start].BlockContentControl;
            var end = start + 1;
            while (end < blocks.Count && ReferenceEquals(blocks[end].BlockContentControl, control))
                end++;

            if (control is { Kind: BlockContentControlKind.PlainText, WordMetadata.DataBinding: { } binding }
                && TryResolve(stores, binding, out var value))
            {
                var runs = blocks
                    .Skip(start)
                    .Take(end - start)
                    .SelectMany(EnumerateParagraphs)
                    .SelectMany(paragraph => paragraph.Runs)
                    .ToList();
                if (runs.Count > 0)
                {
                    runs[0].Text = value;
                    foreach (var run in runs.Skip(1))
                        run.Text = string.Empty;
                    refreshed++;
                }
            }

            start = end;
        }

        return refreshed;
    }

    private static bool TryResolve(
        IReadOnlyDictionary<string, XDocument> stores,
        ContentControlDataBinding binding,
        out string value)
    {
        value = string.Empty;
        if (NormalizeStoreItemId(binding.StoreItemId) is not { } storeItemId
            || binding.XPath is not { Length: > 0 } xpath
            || !stores.TryGetValue(storeItemId, out var item))
        {
            return false;
        }

        try
        {
            var namespaces = BuildNamespaceManager(binding.PrefixMappings);
            var result = item.XPathEvaluate(xpath, namespaces);
            return TryGetValue(result, out value);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or XmlException or XPathException)
        {
            return false;
        }
    }

    private static Dictionary<string, XDocument> BuildDataStores(IReadOnlyList<PreservedPart> parts)
    {
        var partsByName = parts
            .GroupBy(part => part.PartName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);

        foreach (var itemPart in parts.Where(part =>
                     string.Equals(part.RelationshipType, Ooxml.CustomXmlRelType, StringComparison.Ordinal)))
        {
            var relationshipsName = OpcPathHelper.GetRelationshipPartName(itemPart.PartName);
            if (!partsByName.TryGetValue(relationshipsName, out var relationshipsPart)
                || OpcXml.TryLoadXml(relationshipsPart.Bytes) is not { } relationships)
            {
                continue;
            }

            var propertiesTarget = OpcRelationships.Load(relationships)
                .FirstOrDefault(relationship =>
                    !relationship.IsExternal
                    && string.Equals(relationship.Type, Ooxml.CustomXmlPropsRelType, StringComparison.Ordinal))
                .Target;
            if (string.IsNullOrWhiteSpace(propertiesTarget))
                continue;

            var propertiesName = OpcPathHelper.ResolveAbsolutePartName(
                OpcPathHelper.GetPartDirectoryName(itemPart.PartName),
                propertiesTarget);
            if (propertiesName is null
                || !partsByName.TryGetValue(propertiesName, out var propertiesPart)
                || OpcXml.TryLoadXml(propertiesPart.Bytes)?.Root is not { } propertiesRoot
                || NormalizeStoreItemId(propertiesRoot.Attribute(DataStoreNamespace + "itemID")?.Value) is not { } itemId
                || OpcXml.TryLoadXml(itemPart.Bytes) is not { Root: not null } item)
            {
                continue;
            }

            result.TryAdd(itemId, item);
        }

        return result;
    }

    private static XmlNamespaceManager BuildNamespaceManager(string? prefixMappings)
    {
        var manager = new XmlNamespaceManager(new NameTable());
        if (string.IsNullOrWhiteSpace(prefixMappings))
            return manager;

        var wrapper = OpcXml.TryLoadXml(Encoding.UTF8.GetBytes($"<bindings {prefixMappings}/>"))
            ?? throw new XmlException("Invalid content-control prefix mappings.");
        foreach (var declaration in wrapper.Root!.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
        {
            var prefix = declaration.Name.LocalName == "xmlns" ? string.Empty : declaration.Name.LocalName;
            manager.AddNamespace(prefix, declaration.Value);
        }

        return manager;
    }

    private static bool TryGetValue(object? result, out string value)
    {
        if (result is IEnumerable sequence and not string)
        {
            foreach (var item in sequence)
                return TryGetValue(item, out value);

            value = string.Empty;
            return false;
        }

        value = result switch
        {
            XElement element => element.Value,
            XAttribute attribute => attribute.Value,
            XText text => text.Value,
            XPathNavigator navigator => navigator.Value,
            string text => text,
            bool boolean => XmlConvert.ToString(boolean),
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            _ => string.Empty
        };
        return result is XElement or XAttribute or XText or XPathNavigator or string or bool or double;
    }

    private static string? NormalizeStoreItemId(string? value) =>
        Guid.TryParse(value, out var id) ? id.ToString("D") : null;

    private static IEnumerable<Paragraph> EnumerateStoryParagraphs(TextDocument document)
    {
        var seen = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);

        foreach (var paragraph in document.Blocks.SelectMany(EnumerateParagraphs))
            if (seen.Add(paragraph))
                yield return paragraph;

        foreach (var section in document.Sections)
        {
            foreach (var content in new[]
                     {
                         section.HeadersFooters.Header, section.HeadersFooters.Footer,
                         section.HeadersFooters.EvenHeader, section.HeadersFooters.EvenFooter,
                         section.HeadersFooters.FirstHeader, section.HeadersFooters.FirstFooter
                     })
            {
                if (content is null)
                    continue;
                foreach (var paragraph in content.Paragraphs)
                    if (seen.Add(paragraph))
                        yield return paragraph;
            }
        }

        foreach (var paragraph in document.Footnotes.Values.SelectMany(note => note.Content)
                     .Concat(document.Endnotes.Values.SelectMany(note => note.Content))
                     .Concat(EnumerateComments(document)))
        {
            if (seen.Add(paragraph))
                yield return paragraph;
        }
    }

    private static IEnumerable<Paragraph> EnumerateParagraphs(Block block)
    {
        if (block is Paragraph paragraph)
        {
            yield return paragraph;
            yield break;
        }

        if (block is not Table table)
            yield break;
        foreach (var row in table.Rows)
            foreach (var cell in row.Cells)
                foreach (var cellParagraph in cell.Paragraphs)
                    yield return cellParagraph;
    }

    private static IEnumerable<Paragraph> EnumerateComments(TextDocument document)
    {
        var seen = new HashSet<int>();
        var pending = new Queue<Comment>(document.Comments.Values);
        while (pending.Count > 0)
        {
            var comment = pending.Dequeue();
            if (!seen.Add(comment.Id))
                continue;
            foreach (var paragraph in comment.Content)
                yield return paragraph;
            foreach (var reply in comment.Replies)
                pending.Enqueue(reply);
        }
    }
}
