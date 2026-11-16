using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Free.Shared.Pdf.Skia;

/// <summary>
/// Adds catalog entries and outlines in an incremental update to Skia's classic cross-reference
/// format. The original page streams, fonts, links, and named destinations stay intact.
/// </summary>
internal static partial class SkiaPdfCatalog
{
    private const int CrossReferenceEntryLength = 20;
    private const int GeneratedTrailerReadLimit = 1024;
    private const int GeneratedCatalogReadLimit = 4096;
    private const int GeneratedPageTreeReadLimit = 1024;

    public static void AppendUpdate(Stream stream, PdfDocumentProperties properties, int pageCount)
    {
        var outlines = properties.Outlines.Where(outline =>
            (uint)outline.PageIndex < (uint)pageCount && !string.IsNullOrWhiteSpace(outline.Title)).ToArray();
        var hasOutlines = outlines.Length > 0;
        var viewerEntries = PdfCatalogViewerEntries.Build(properties.ViewerSettings, hasOutlines);
        if (string.IsNullOrWhiteSpace(properties.Language) && string.IsNullOrEmpty(viewerEntries) && !hasOutlines)
            return;

        var trailerStart = Math.Max(0, stream.Length - GeneratedTrailerReadLimit);
        var trailer = TrailerPattern().Match(ReadAscii(stream, trailerStart, GeneratedTrailerReadLimit));
        var trailerDictionary = trailer.Groups["dictionary"].Value;
        var size = DictionarySizePattern().Match(trailerDictionary);
        var root = CatalogReferencePattern().Match(trailerDictionary);
        if (!TryParseOffset(trailer.Groups["xref"].ValueSpan, out var previousXref) ||
            !TryParseOffset(root.Groups["number"].ValueSpan, out var catalogNumber) ||
            !TryParseOffset(size.Groups["count"].ValueSpan, out var objectCount) ||
            catalogNumber <= 0 || catalogNumber >= objectCount ||
            previousXref <= 0 || previousXref >= stream.Length || trailerDictionary.Contains("/Prev", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Skia PDF export did not produce the expected trailer.");
        }

        var xrefHeader = FormattableString.Invariant($"xref\n0 {objectCount}\n");
        if (ReadAscii(stream, previousXref, xrefHeader.Length) != xrefHeader ||
            objectCount > (stream.Length - previousXref - xrefHeader.Length) / CrossReferenceEntryLength)
        {
            throw new InvalidDataException("Skia PDF export did not produce the expected cross-reference table.");
        }

        var xref = new OriginalCrossReference(previousXref, previousXref + xrefHeader.Length, objectCount);
        var dictionary = ReadDictionary(ReadObjectPrefix(stream, xref, catalogNumber, GeneratedCatalogReadLimit));
        if (!CatalogDictionaryPattern().IsMatch(dictionary))
            throw new InvalidDataException("Skia PDF export did not produce the expected catalog dictionary.");

        if (properties.Language is { } language && !string.IsNullOrWhiteSpace(language))
            dictionary = ApplyLanguage(dictionary, language);

        if (!string.IsNullOrEmpty(viewerEntries))
        {
            var updatedFields = ViewerCatalogEntryPattern().Matches(viewerEntries)
                .Select(entry => entry.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
            dictionary = ViewerCatalogEntryPattern().Replace(dictionary,
                entry => updatedFields.Contains(entry.Groups["name"].Value) ? string.Empty : entry.Value);
            dictionary = dictionary.Insert(dictionary.Length - 2, viewerEntries);
        }

        long[] pageReferences = [];
        if (hasOutlines)
        {
            var pagesRoot = PagesReferencePattern().Match(dictionary);
            if (!TryParseOffset(pagesRoot.Groups["number"].ValueSpan, out var pagesNumber))
                throw new InvalidDataException("Skia PDF export did not produce the expected page tree reference.");

            pageReferences = ReadPages(stream, xref, pagesNumber, pageCount);
            dictionary = OutlinesReferencePattern().Replace(dictionary, string.Empty);
            dictionary = dictionary.Insert(dictionary.Length - 2, FormattableString.Invariant($"\n/Outlines {objectCount} 0 R"));
        }

        stream.Position = stream.Length;
        var catalogRevisionOffset = WriteObject(stream, catalogNumber, dictionary);
        var outlineOffsets = hasOutlines ? WriteOutlines(stream, objectCount, outlines, pageReferences) : [];
        var xrefRevisionOffset = stream.Position;
        WriteAscii(stream, FormattableString.Invariant($"xref\n{catalogNumber} 1\n"));
        WriteCrossReferenceEntry(stream, catalogRevisionOffset);
        if (outlineOffsets.Length > 0)
        {
            WriteAscii(stream, FormattableString.Invariant($"{objectCount} {outlineOffsets.Length}\n"));
            foreach (var offset in outlineOffsets)
                WriteCrossReferenceEntry(stream, offset);
            trailerDictionary = trailerDictionary.Remove(size.Index, size.Length)
                .Insert(size.Index, FormattableString.Invariant($"/Size {objectCount + outlineOffsets.Length}"));
        }

        WriteAscii(stream, FormattableString.Invariant(
            $"trailer\n<<{trailerDictionary}\n/Prev {previousXref}>>\nstartxref\n{xrefRevisionOffset}\n%%EOF\n"));
    }

    private static string ReadObjectPrefix(Stream stream, OriginalCrossReference xref, long number, int readLimit)
    {
        if (number <= 0 || number >= xref.ObjectCount)
            throw new InvalidDataException("Skia PDF export produced an invalid object reference.");

        var entryOffset = xref.EntryTableOffset + (number * CrossReferenceEntryLength);
        var entry = ReadAscii(stream, entryOffset, CrossReferenceEntryLength);
        if (entry.Length != CrossReferenceEntryLength || !entry.EndsWith(" 00000 n \n", StringComparison.Ordinal) ||
            !TryParseOffset(entry.AsSpan(0, 10), out var objectOffset) || objectOffset <= 0 || objectOffset >= xref.Offset)
        {
            throw new InvalidDataException("Skia PDF export did not produce a valid object cross-reference.");
        }

        var objectHeader = FormattableString.Invariant($"{number} 0 obj\n");
        var data = ReadAscii(stream, objectOffset, readLimit);
        if (!data.StartsWith(objectHeader, StringComparison.Ordinal))
            throw new InvalidDataException("Skia PDF export did not produce the expected object header.");

        return data[objectHeader.Length..];
    }

    private static string ReadDictionary(string objectPrefix)
    {
        var end = objectPrefix.IndexOf("\nendobj\n", StringComparison.Ordinal);
        if (end < 0 || !objectPrefix.AsSpan(0, end).EndsWith(">>", StringComparison.Ordinal))
            throw new InvalidDataException("Skia PDF export did not produce the expected object dictionary.");

        return objectPrefix[..end];
    }

    private static string ApplyLanguage(string dictionary, string language)
    {
        var languageEntry = $"/Lang {EncodeText(language)}";
        var existingLanguage = CatalogLanguageKeyPattern().Matches(dictionary);
        if (existingLanguage.Count == 0)
            return dictionary.Insert(dictionary.Length - 2, $"\n{languageEntry}");

        var value = CatalogLanguageEntryPattern().Match(dictionary);
        if (existingLanguage.Count != 1 || !value.Success)
            throw new InvalidDataException("Skia PDF export produced an unexpected catalog language entry.");

        return dictionary.Remove(value.Index, value.Length).Insert(value.Index, languageEntry);
    }

    private static long[] ReadPages(Stream stream, OriginalCrossReference xref, long root, int expectedPageCount)
    {
        // Skia groups pages into small /Pages nodes. Walk /Kids order, as the object numbers
        // themselves need not express page order or even be contiguous.
        var pages = new List<long>(expectedPageCount);
        var seen = new HashSet<long>();
        var pending = new Stack<long>();
        pending.Push(root);

        while (pending.TryPop(out var number))
        {
            if (!seen.Add(number))
                throw new InvalidDataException("Skia PDF export produced a duplicate page tree node.");

            var data = ReadObjectPrefix(stream, xref, number, GeneratedPageTreeReadLimit);
            var type = PageTypePattern().Match(data).Groups["type"].Value;
            if (type == "Page" && number != root)
            {
                pages.Add(number);
                continue;
            }

            var children = type == "Pages" ? PageKidsPattern().Match(ReadDictionary(data)) : Match.Empty;
            if (!children.Success)
                throw new InvalidDataException("Skia PDF export did not produce the expected page tree node.");

            var references = PageReferencePattern().Matches(children.Groups["kids"].Value);
            for (var index = references.Count - 1; index >= 0; index--)
            {
                if (!TryParseOffset(references[index].Groups["number"].ValueSpan, out var child))
                    throw new InvalidDataException("Skia PDF export produced an invalid page tree reference.");

                pending.Push(child);
            }
        }

        if (pages.Count != expectedPageCount)
            throw new InvalidDataException("Skia PDF export did not produce the expected number of pages.");

        return [.. pages];
    }

    private static long[] WriteOutlines(
        Stream stream, long root, IReadOnlyList<PdfOutlineEntry> outlines, IReadOnlyList<long> pages)
    {
        var offsets = new long[outlines.Count + 1];
        offsets[0] = WriteObject(stream, root, FormattableString.Invariant(
            $"<< /Type /Outlines /First {root + 1} 0 R /Last {root + outlines.Count} 0 R /Count {outlines.Count} >>"));
        for (var index = 0; index < outlines.Count; index++)
        {
            var item = outlines[index];
            var number = root + 1 + index;
            var previous = index > 0 ? FormattableString.Invariant($" /Prev {number - 1} 0 R") : string.Empty;
            var next = index + 1 < outlines.Count ? FormattableString.Invariant($" /Next {number + 1} 0 R") : string.Empty;
            offsets[index + 1] = WriteObject(stream, number, FormattableString.Invariant(
                $"<< /Title {EncodeText(item.Title.Trim())} /Parent {root} 0 R{previous}{next} /Dest [{pages[item.PageIndex]} 0 R /XYZ null null null] >>"));
        }

        return offsets;
    }

    private static long WriteObject(Stream stream, long number, string dictionary)
    {
        var offset = stream.Position;
        WriteAscii(stream, FormattableString.Invariant($"{number} 0 obj\n{dictionary}\nendobj\n"));
        return offset;
    }

    private static void WriteCrossReferenceEntry(Stream stream, long offset) =>
        WriteAscii(stream, FormattableString.Invariant($"{offset:D10} 00000 n \n"));

    private static string EncodeText(string text) =>
        $"<FEFF{Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text))}>";

    private static bool TryParseOffset(ReadOnlySpan<char> text, out long value) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static string ReadAscii(Stream stream, long offset, int limit)
    {
        stream.Position = offset;
        Span<byte> bytes = stackalloc byte[(int)Math.Min(stream.Length - offset, limit)];
        stream.ReadExactly(bytes);
        return Encoding.ASCII.GetString(bytes);
    }

    private static void WriteAscii(Stream stream, string text) =>
        stream.Write(Encoding.ASCII.GetBytes(text));

    [GeneratedRegex(@"\ntrailer\n<<(?<dictionary>.*?)>>\nstartxref\n(?<xref>[0-9]+)\n%%EOF[ \t\r\n\f]*\z", RegexOptions.Singleline)]
    private static partial Regex TrailerPattern();

    [GeneratedRegex(@"(?<!\S)/Size\s+(?<count>[0-9]+)(?=\s|$)")]
    private static partial Regex DictionarySizePattern();

    [GeneratedRegex(@"(?<!\S)/Root\s+(?<number>[0-9]+)\s+0\s+R(?=\s|$)")]
    private static partial Regex CatalogReferencePattern();

    [GeneratedRegex(@"\A<<\s*/Type\s+/Catalog(?=\s|/|>>)")]
    private static partial Regex CatalogDictionaryPattern();

    [GeneratedRegex(@"(?<!\S)/Lang(?=\s|\(|<)")]
    private static partial Regex CatalogLanguageKeyPattern();

    [GeneratedRegex(@"(?<!\S)/Lang\s*(?:\((?:\\.|[^\\()])*\)|<[0-9A-Fa-f\s]*>)")]
    private static partial Regex CatalogLanguageEntryPattern();

    [GeneratedRegex(@"\s*/(?<name>Version|PageLayout|PageMode|NonFullScreenPageMode)\s+/[A-Za-z0-9.]+|\s*/(?<name>ViewerPreferences)\s*<<[^<>]*>>")]
    private static partial Regex ViewerCatalogEntryPattern();

    [GeneratedRegex(@"\s*/Outlines\s+[0-9]+\s+0\s+R\b")]
    private static partial Regex OutlinesReferencePattern();

    [GeneratedRegex(@"(?<!\S)/Pages\s+(?<number>[0-9]+)\s+0\s+R\b")]
    private static partial Regex PagesReferencePattern();

    [GeneratedRegex(@"\A<<\s*/Type\s+/(?<type>Pages?)(?=\s|/|>>)")]
    private static partial Regex PageTypePattern();

    [GeneratedRegex(@"(?<!\S)/Kids\s*\[(?<kids>(?:\s*[0-9]+\s+0\s+R)+)\s*\]")]
    private static partial Regex PageKidsPattern();

    [GeneratedRegex(@"(?<number>[0-9]+)\s+0\s+R")]
    private static partial Regex PageReferencePattern();

    private readonly record struct OriginalCrossReference(long Offset, long EntryTableOffset, long ObjectCount);
}
