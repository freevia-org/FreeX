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

    /// <summary>
    /// Propagates every data-bound plain-text content control's CURRENT displayed text back into its bound
    /// customXml data-store item whenever the two have diverged (i.e. the run text was edited since the
    /// binding was last resolved). Word re-reads w:dataBinding when the package is reopened, so without this
    /// the edited display text is silently discarded and the stale store value reappears. Also clears a
    /// stale w:showingPlcHdr on the edited control (mutating the document's runs in place, mirroring
    /// <see cref="RefreshBoundTextControls"/>'s load-time mutation) so genuine user content stops round-tripping
    /// tagged as placeholder text. Returns <paramref name="preservedParts"/> with the affected customXml
    /// item part(s) replaced by their updated bytes; unaffected parts (including every part when nothing
    /// diverged) are returned unchanged. List/combo/checkbox/date-bound controls and controls whose binding
    /// does not resolve are left untouched — this only writes back plain-text bindings, the shape a typed
    /// edit actually takes.
    /// </summary>
    public static IReadOnlyList<PreservedPart> WriteBoundTextEdits(
        TextDocument document,
        IReadOnlyList<PreservedPart> preservedParts)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(preservedParts);

        var stores = BuildDataStores(preservedParts);
        if (stores.Count == 0)
            return preservedParts;

        var paragraphs = EnumerateStoryParagraphs(document).ToList();

        // Pass 1: snapshot the pre-edit stored value of every distinct binding (store item + XPath +
        // prefix mappings) referenced by an inline control, BEFORE any write-back below mutates the
        // shared per-store-item XDocument. Two or more controls can legitimately bind to the same
        // XPath -- "linked" controls, e.g. the same field repeated in a header and in the body -- and
        // resolving each one against this snapshot (rather than the live document, which the walk in
        // pass 2 progressively mutates) is what lets every such control be compared against the value
        // it actually started the edit session with, instead of a sibling control's already-written
        // value.
        var snapshot = new Dictionary<BindingKey, string>();
        foreach (var paragraph in paragraphs)
        {
            foreach (var (_, _, _, binding) in EnumerateInlineBoundGroups(paragraph))
            {
                if (BindingKey.From(binding) is not { } key || snapshot.ContainsKey(key))
                    continue;
                if (TryResolve(stores, binding, out var storedValue))
                    snapshot[key] = storedValue;
            }
        }

        // Pass 2: apply edits using the snapshot above. A control is only treated as edited when its
        // displayed text differs from the value the snapshot recorded for its binding key -- a linked
        // control that was never touched still shows that same snapshot value, so it is correctly left
        // alone even after a sibling control bound to the same key has already written a new value to
        // the store.
        //
        // Conflict rule: if two or more linked controls were BOTH edited to DIFFERENT values before this
        // save, the first one encountered in document order (body, then headers/footers, footnotes/
        // endnotes, then comments -- see EnumerateStoryParagraphs) wins and its value is written to the
        // shared store item once; every later control bound to the same key is discarded from the store
        // (its own displayed text is left as-is in memory for this session, but the next time the
        // document is opened, RefreshBoundTextControls will resolve it back to the single value that won
        // and got persisted). This is a "first writer wins" policy, chosen because document order is the
        // only deterministic signal available here -- there is no edit-timestamp to arbitrate by -- and a
        // single deterministic winner keeps the store in one consistent state rather than depending on
        // enumeration order to decide which write clobbers which mid-walk.
        var dirtyPartNames = new HashSet<string>(StringComparer.Ordinal);
        var resolvedKeys = new HashSet<BindingKey>();
        foreach (var paragraph in paragraphs)
            WriteInlineBoundEdits(paragraph, stores, snapshot, resolvedKeys, dirtyPartNames);

        if (dirtyPartNames.Count == 0)
            return preservedParts;

        var updatedByPartName = stores.Values
            .Where(entry => dirtyPartNames.Contains(entry.Part.PartName))
            .ToDictionary(
                entry => entry.Part.PartName,
                entry => entry.Part with { Bytes = SerializeXml(entry.Document) },
                StringComparer.Ordinal);

        return preservedParts
            .Select(part => updatedByPartName.TryGetValue(part.PartName, out var updated) ? updated : part)
            .ToList();
    }

    private static void WriteInlineBoundEdits(
        Paragraph paragraph,
        IReadOnlyDictionary<string, (XDocument Document, PreservedPart Part)> stores,
        IReadOnlyDictionary<BindingKey, string> snapshot,
        HashSet<BindingKey> resolvedKeys,
        HashSet<string> dirtyPartNames)
    {
        foreach (var (start, end, control, binding) in EnumerateInlineBoundGroups(paragraph))
        {
            if (BindingKey.From(binding) is not { } key
                || !snapshot.TryGetValue(key, out var storedValue)
                || !stores.TryGetValue(key.StoreItemId, out var entry))
            {
                continue;
            }

            var displayedText = string.Concat(
                paragraph.Runs.Skip(start).Take(end - start).Select(run => run.Text));
            if (string.Equals(storedValue, displayedText, StringComparison.Ordinal))
                continue; // unchanged from the value this control's edit session started with

            if (!resolvedKeys.Contains(key) && TryWriteBack(entry.Document, binding, displayedText))
            {
                // First (in document order) control bound to this key whose displayed text actually
                // diverged: it wins the write. See the "first writer wins" note in WriteBoundTextEdits.
                resolvedKeys.Add(key);
                dirtyPartNames.Add(entry.Part.PartName);
            }

            if (control.WordMetadata!.ShowingPlaceholder)
            {
                var cleared = control with
                {
                    WordMetadata = control.WordMetadata with { ShowingPlaceholder = false }
                };
                for (var index = start; index < end; index++)
                    paragraph.Runs[index].Control = cleared;
            }
        }
    }

    private static IEnumerable<(int Start, int End, ContentControl Control, ContentControlDataBinding Binding)>
        EnumerateInlineBoundGroups(Paragraph paragraph)
    {
        for (var start = 0; start < paragraph.Runs.Count;)
        {
            var control = paragraph.Runs[start].Control;
            var end = start + 1;
            while (end < paragraph.Runs.Count && ReferenceEquals(paragraph.Runs[end].Control, control))
                end++;

            if (control is { Kind: ContentControlKind.PlainText, WordMetadata.DataBinding: { } binding })
                yield return (start, end, control, binding);

            start = end;
        }
    }

    /// <summary>
    /// Identifies one bindable location inside a customXml data store: the store item plus the XPath
    /// (and namespace prefix mappings the XPath relies on) used to reach into it. Two content controls
    /// that share a <see cref="BindingKey"/> are "linked" -- Word shows and edits them as one field.
    /// </summary>
    private readonly record struct BindingKey(string StoreItemId, string XPath, string? PrefixMappings)
    {
        public static BindingKey? From(ContentControlDataBinding binding)
        {
            if (NormalizeStoreItemId(binding.StoreItemId) is not { } storeItemId
                || binding.XPath is not { Length: > 0 } xpath)
            {
                return null;
            }

            return new BindingKey(storeItemId, xpath, binding.PrefixMappings);
        }
    }

    private static bool TryWriteBack(XDocument itemDocument, ContentControlDataBinding binding, string newValue)
    {
        if (binding.XPath is not { Length: > 0 } xpath)
            return false;

        try
        {
            var namespaces = BuildNamespaceManager(binding.PrefixMappings);
            var result = itemDocument.XPathEvaluate(xpath, namespaces);
            return TrySetValue(result, newValue);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or XmlException or XPathException)
        {
            return false;
        }
    }

    private static bool TrySetValue(object? result, string newValue)
    {
        if (result is IEnumerable sequence and not string)
        {
            foreach (var item in sequence)
                return TrySetValue(item, newValue);

            return false;
        }

        switch (result)
        {
            case XElement element:
                element.Value = newValue;
                return true;
            case XAttribute attribute:
                attribute.Value = newValue;
                return true;
            case XText text:
                text.Value = newValue;
                return true;
            default:
                return false;
        }
    }

    private static byte[] SerializeXml(XDocument document) =>
        Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));

    private static int RefreshInlineControls(
        Paragraph paragraph,
        IReadOnlyDictionary<string, (XDocument Document, PreservedPart Part)> stores)
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
        if (string.IsNullOrEmpty(storage)
            || string.Equals(storage, "text", StringComparison.OrdinalIgnoreCase))
        {
            displayText = value;
            return true;
        }

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
        IReadOnlyDictionary<string, (XDocument Document, PreservedPart Part)> stores)
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
                    .SelectMany(EnumerateBlockParagraphs)
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
        IReadOnlyDictionary<string, (XDocument Document, PreservedPart Part)> stores,
        ContentControlDataBinding binding,
        out string value)
    {
        value = string.Empty;
        if (NormalizeStoreItemId(binding.StoreItemId) is not { } storeItemId
            || binding.XPath is not { Length: > 0 } xpath
            || !stores.TryGetValue(storeItemId, out var entry))
        {
            return false;
        }

        try
        {
            var namespaces = BuildNamespaceManager(binding.PrefixMappings);
            var result = entry.Document.XPathEvaluate(xpath, namespaces);
            return TryGetValue(result, out value);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or XmlException or XPathException)
        {
            return false;
        }
    }

    private static Dictionary<string, (XDocument Document, PreservedPart Part)> BuildDataStores(
        IReadOnlyList<PreservedPart> parts)
    {
        var partsByName = parts
            .GroupBy(part => part.PartName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, (XDocument Document, PreservedPart Part)>(StringComparer.OrdinalIgnoreCase);

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

            result.TryAdd(itemId, (item, itemPart));
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

    private static IEnumerable<Paragraph> EnumerateStoryParagraphs(TextDocument document) =>
        TextDocumentStoryTraversal.EnumerateParagraphs(document, EnumerateComments(document));

    private static IEnumerable<Paragraph> EnumerateBlockParagraphs(Block block)
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
