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
    /// Refreshes successfully resolved text, list, checkbox, and supported Gregorian date controls while
    /// leaving every unresolved control unchanged. Returns the number of distinct controls that were updated.
    /// </summary>
    public static int RefreshBoundTextControls(TextDocument document)
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

    /// <summary>
    /// Backward-compatible entry point for callers that used the original plain-text-only refresh API.
    /// List, combo, checkbox, and supported Gregorian date controls now use the same Word XML-mapping pass.
    /// </summary>
    public static int RefreshBoundPlainTextControls(TextDocument document) =>
        RefreshBoundTextControls(document);

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

            if (control is { WordMetadata.DataBinding: { } binding }
                && TryResolve(stores, binding, out var value)
                && TryApplyInlineBinding(paragraph.Runs, start, end, control, value))
            {
                refreshed++;
            }

            start = end;
        }

        return refreshed;
    }

    private static bool TryApplyInlineBinding(
        IReadOnlyList<Run> runs,
        int start,
        int end,
        ContentControl control,
        string value)
    {
        if (IsTextualBindingKind(control.Kind))
        {
            runs[start].Text = ResolveDisplayText(control, value);
            for (var index = start + 1; index < end; index++)
                runs[index].Text = string.Empty;
            return true;
        }

        if (control.Kind == ContentControlKind.DatePicker
            && TryResolveDateBinding(control, value, out var updatedDateControl, out var displayText))
        {
            runs[start].Text = displayText;
            for (var index = start; index < end; index++)
            {
                runs[index].Control = updatedDateControl;
                if (index > start)
                    runs[index].Text = string.Empty;
            }
            return true;
        }

        if (control.Kind != ContentControlKind.CheckBox || !TryParseXmlBoolean(value, out var isChecked))
            return false;

        var updated = control with { Checked = isChecked };
        runs[start].Text = ResolveCheckBoxGlyph(updated);
        for (var index = start; index < end; index++)
        {
            runs[index].Control = updated;
            if (index > start)
                runs[index].Text = string.Empty;
        }
        return true;
    }

    private static bool IsTextualBindingKind(ContentControlKind kind) =>
        kind is ContentControlKind.PlainText
            or ContentControlKind.DropDownList
            or ContentControlKind.ComboBox;

    private static string ResolveDisplayText(ContentControl control, string value)
    {
        if (control.Kind is not (ContentControlKind.DropDownList or ContentControlKind.ComboBox))
            return value;

        return control.Items.FirstOrDefault(item => string.Equals(item.Value, value, StringComparison.Ordinal))
            ?.DisplayText ?? value;
    }

    private static bool TryResolveDateBinding(
        ContentControl control,
        string value,
        out ContentControl updated,
        out string displayText)
    {
        updated = control;
        displayText = string.Empty;
        var metadata = control.DateMetadata;
        if (metadata?.Calendar is { Length: > 0 } calendar
            && !string.Equals(calendar, "gregorian", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var storage = metadata?.StoreMappedDataAs;
        DateTimeOffset date;
        string fullDate;
        if (string.Equals(storage, "date", StringComparison.OrdinalIgnoreCase))
        {
            if (!DateOnly.TryParseExact(
                    value.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var dateOnly))
            {
                return false;
            }

            date = new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            fullDate = $"{dateOnly:yyyy-MM-dd}T00:00:00Z";
        }
        else if (string.Equals(storage, "dateTime", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                date = XmlConvert.ToDateTimeOffset(value.Trim());
                fullDate = XmlConvert.ToString(date);
            }
            catch (FormatException)
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        CultureInfo culture;
        try
        {
            culture = metadata?.LanguageId is { Length: > 0 } languageId
                ? CultureInfo.GetCultureInfo(languageId)
                : CultureInfo.InvariantCulture;
            displayText = date.ToString(control.DateFormat ?? ContentControl.DefaultDateFormat, culture);
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }

        updated = control with
        {
            DateMetadata = (metadata ?? new ContentControlDateMetadata()) with { FullDate = fullDate }
        };
        return true;
    }

    private static bool TryParseXmlBoolean(string value, out bool result)
    {
        switch (value.Trim())
        {
            case "true":
            case "1":
                result = true;
                return true;
            case "false":
            case "0":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }

    private static string ResolveCheckBoxGlyph(ContentControl control)
    {
        var state = control.Checked
            ? control.CheckBoxMetadata?.CheckedState
            : control.CheckBoxMetadata?.UncheckedState;
        if (state?.GlyphCodePoint is { Length: > 0 } code
            && int.TryParse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint)
            && Rune.IsValid(codePoint))
        {
            return char.ConvertFromUtf32(codePoint);
        }

        return control.Checked ? ContentControl.CheckedGlyph : ContentControl.UncheckedGlyph;
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
