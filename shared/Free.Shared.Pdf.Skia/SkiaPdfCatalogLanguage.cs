using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Free.Shared.Pdf.Skia;

/// <summary>
/// Writes /Lang as an incremental catalog update. SkiaSharp's metadata API does not expose
/// document language. Only the original classic cross-reference format emitted by Skia is read,
/// and all page streams, fonts, links, and other PDF objects remain in their original locations.
/// </summary>
internal static partial class SkiaPdfCatalogLanguage
{
    private const int CrossReferenceEntryLength = 20;
    private const int GeneratedTrailerReadLimit = 1024;
    private const int GeneratedCatalogReadLimit = 4096;

    public static void AppendUpdate(Stream stream, string language)
    {
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
            catalogNumber > (stream.Length - previousXref - xrefHeader.Length) / CrossReferenceEntryLength)
        {
            throw new InvalidDataException("Skia PDF export did not produce the expected cross-reference table.");
        }

        var entryOffset = previousXref + xrefHeader.Length + (catalogNumber * CrossReferenceEntryLength);
        var entry = ReadAscii(stream, entryOffset, CrossReferenceEntryLength);
        if (entry.Length != CrossReferenceEntryLength || !entry.EndsWith(" 00000 n \n", StringComparison.Ordinal) ||
            !TryParseOffset(entry.AsSpan(0, 10), out var catalogOffset) || catalogOffset <= 0 || catalogOffset >= previousXref)
        {
            throw new InvalidDataException("Skia PDF export did not produce a valid catalog cross-reference.");
        }

        var objectHeader = FormattableString.Invariant($"{catalogNumber} 0 obj\n");
        var catalog = ReadAscii(stream, catalogOffset, GeneratedCatalogReadLimit);
        var catalogEnd = catalog.IndexOf("\nendobj\n", StringComparison.Ordinal);
        if (catalogEnd < objectHeader.Length || !catalog.StartsWith(objectHeader, StringComparison.Ordinal))
            throw new InvalidDataException("Skia PDF export did not produce the expected catalog object.");

        var dictionary = catalog[objectHeader.Length..catalogEnd];
        if (!CatalogDictionaryPattern().IsMatch(dictionary) || !dictionary.EndsWith(">>", StringComparison.Ordinal))
            throw new InvalidDataException("Skia PDF export did not produce the expected catalog dictionary.");

        var languageEntry = $"/Lang <FEFF{Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(language))}>";
        var existingLanguage = CatalogLanguageKeyPattern().Matches(dictionary);
        if (existingLanguage.Count == 0)
        {
            dictionary = dictionary.Insert(dictionary.Length - 2, $"\n{languageEntry}");
        }
        else
        {
            var value = CatalogLanguageEntryPattern().Match(dictionary);
            if (existingLanguage.Count != 1 || !value.Success)
                throw new InvalidDataException("Skia PDF export produced an unexpected catalog language entry.");

            dictionary = dictionary.Remove(value.Index, value.Length).Insert(value.Index, languageEntry);
        }

        stream.Position = stream.Length;
        var catalogRevisionOffset = stream.Position;
        WriteAscii(stream, $"{objectHeader}{dictionary}\nendobj\n");
        var xrefRevisionOffset = stream.Position;
        WriteAscii(stream, FormattableString.Invariant(
            $"xref\n{catalogNumber} 1\n{catalogRevisionOffset:D10} 00000 n \ntrailer\n<<{trailerDictionary}\n/Prev {previousXref}>>\nstartxref\n{xrefRevisionOffset}\n%%EOF\n"));
    }

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
}
