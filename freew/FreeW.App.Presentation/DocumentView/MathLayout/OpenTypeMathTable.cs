using System.Buffers.Binary;

namespace FreeW.App.Presentation.DocumentView.MathLayout;

/// <summary>
/// A reader for the OpenType <c>MATH</c> table (constants, italic corrections, top-accent attachments and
/// glyph size variants/assemblies), independent of any UI toolkit so every renderer can lay equations out
/// from the same font data. Accepts a bare sfnt font or a TrueType collection; for a collection it uses the
/// requested face, or the first face that carries a MATH table.
/// </summary>
public sealed class OpenTypeMathTable
{
    private readonly Dictionary<int, double> _italicCorrections;
    private readonly Dictionary<int, double> _topAccentAttachments;
    private readonly Dictionary<int, MathGlyphConstructionData> _vertical;
    private readonly Dictionary<int, MathGlyphConstructionData> _horizontal;

    private OpenTypeMathTable(
        double unitsPerEm,
        MathFontConstants constants,
        double minConnectorOverlap,
        Dictionary<int, double> italicCorrections,
        Dictionary<int, double> topAccentAttachments,
        Dictionary<int, MathGlyphConstructionData> vertical,
        Dictionary<int, MathGlyphConstructionData> horizontal)
    {
        UnitsPerEm = unitsPerEm;
        Constants = constants;
        MinConnectorOverlap = minConnectorOverlap;
        _italicCorrections = italicCorrections;
        _topAccentAttachments = topAccentAttachments;
        _vertical = vertical;
        _horizontal = horizontal;
    }

    public double UnitsPerEm { get; }

    /// <summary>The MATH constants in em units.</summary>
    public MathFontConstants Constants { get; }

    /// <summary>MinConnectorOverlap in em units.</summary>
    public double MinConnectorOverlap { get; }

    /// <summary>Italic correction of a glyph in em units, if the table lists one.</summary>
    public double? ItalicCorrection(int glyphId) =>
        _italicCorrections.TryGetValue(glyphId, out var value) ? value : null;

    /// <summary>Top-accent attachment x of a glyph in em units, if the table lists one.</summary>
    public double? TopAccentAttachment(int glyphId) =>
        _topAccentAttachments.TryGetValue(glyphId, out var value) ? value : null;

    public IReadOnlyList<MathGlyphVariant> Variants(int glyphId, bool vertical) =>
        (vertical ? _vertical : _horizontal).TryGetValue(glyphId, out var data) ? data.Variants : [];

    public MathGlyphAssembly? Assembly(int glyphId, bool vertical) =>
        (vertical ? _vertical : _horizontal).TryGetValue(glyphId, out var data) ? data.Assembly : null;

    /// <summary>
    /// Parses the MATH table of a font file (sfnt or collection). Returns null when the data is not a font,
    /// the face has no MATH table, or the table is malformed.
    /// </summary>
    public static OpenTypeMathTable? TryLoad(ReadOnlySpan<byte> fontData, int faceIndex = -1)
    {
        try
        {
            return Load(fontData, faceIndex);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    private static OpenTypeMathTable? Load(ReadOnlySpan<byte> data, int faceIndex)
    {
        if (data.Length < 12)
            return null;

        var directories = new List<int>();
        if (Tag(data, 0) == "ttcf")
        {
            var count = (int)U32(data, 8);
            for (var i = 0; i < count; i++)
                directories.Add((int)U32(data, 12 + (4 * i)));
        }
        else
        {
            directories.Add(0);
        }

        IEnumerable<int> candidates = faceIndex >= 0 && faceIndex < directories.Count
            ? [directories[faceIndex]]
            : directories;
        foreach (var directory in candidates)
        {
            if (!TryFindTable(data, directory, "MATH", out var mathOffset)
                || !TryFindTable(data, directory, "head", out var headOffset))
            {
                continue;
            }

            var unitsPerEm = U16(data, headOffset + 18);
            return ParseMath(data, mathOffset, unitsPerEm == 0 ? 1000 : unitsPerEm);
        }

        return null;
    }

    private static bool TryFindTable(ReadOnlySpan<byte> data, int directory, string tag, out int offset)
    {
        var numTables = U16(data, directory + 4);
        for (var i = 0; i < numTables; i++)
        {
            var record = directory + 12 + (16 * i);
            if (Tag(data, record) == tag)
            {
                offset = (int)U32(data, record + 8);
                return true;
            }
        }

        offset = 0;
        return false;
    }

    private static OpenTypeMathTable ParseMath(ReadOnlySpan<byte> data, int math, double unitsPerEm)
    {
        var constantsOffset = math + U16(data, math + 4);
        var glyphInfoOffset = U16(data, math + 6);
        var variantsOffset = U16(data, math + 8);

        // MathConstants: two int16 percentages, two UFWORD heights, 51 MathValueRecords (value + device
        // offset, 4 bytes each), then the int16 RadicalDegreeBottomRaisePercent.
        var scriptPercent = S16(data, constantsOffset);
        var scriptScriptPercent = S16(data, constantsOffset + 2);
        var values = new List<double>(MathFontConstants.ValueCount)
        {
            U16(data, constantsOffset + 4),
            U16(data, constantsOffset + 6)
        };
        for (var i = 0; i < MathFontConstants.ValueCount - 2; i++)
            values.Add(S16(data, constantsOffset + 8 + (4 * i)));
        var raisePercent = S16(data, constantsOffset + 8 + (4 * (MathFontConstants.ValueCount - 2)));
        var constants = MathFontConstants.FromFontUnits(unitsPerEm, scriptPercent, scriptScriptPercent, raisePercent, values);

        var italics = new Dictionary<int, double>();
        var accents = new Dictionary<int, double>();
        if (glyphInfoOffset != 0)
        {
            var glyphInfo = math + glyphInfoOffset;
            ReadValueTable(data, glyphInfo, U16(data, glyphInfo), unitsPerEm, italics);
            ReadValueTable(data, glyphInfo, U16(data, glyphInfo + 2), unitsPerEm, accents);
        }

        var vertical = new Dictionary<int, MathGlyphConstructionData>();
        var horizontal = new Dictionary<int, MathGlyphConstructionData>();
        double minOverlap = 0;
        if (variantsOffset != 0)
        {
            var variants = math + variantsOffset;
            minOverlap = U16(data, variants) / unitsPerEm;
            var vertCoverage = U16(data, variants + 2);
            var horizCoverage = U16(data, variants + 4);
            var vertCount = U16(data, variants + 6);
            var horizCount = U16(data, variants + 8);
            var vertGlyphs = vertCoverage == 0 ? [] : ReadCoverage(data, variants + vertCoverage);
            var horizGlyphs = horizCoverage == 0 ? [] : ReadCoverage(data, variants + horizCoverage);
            for (var i = 0; i < vertCount && i < vertGlyphs.Count; i++)
            {
                var construction = U16(data, variants + 10 + (2 * i));
                vertical[vertGlyphs[i]] = ReadConstruction(data, variants + construction, unitsPerEm);
            }

            for (var i = 0; i < horizCount && i < horizGlyphs.Count; i++)
            {
                var construction = U16(data, variants + 10 + (2 * (vertCount + i)));
                horizontal[horizGlyphs[i]] = ReadConstruction(data, variants + construction, unitsPerEm);
            }
        }

        return new OpenTypeMathTable(unitsPerEm, constants, minOverlap, italics, accents, vertical, horizontal);
    }

    private static void ReadValueTable(
        ReadOnlySpan<byte> data,
        int owner,
        int relativeOffset,
        double unitsPerEm,
        Dictionary<int, double> target)
    {
        if (relativeOffset == 0)
            return;

        var table = owner + relativeOffset;
        var coverage = ReadCoverage(data, table + U16(data, table));
        var count = U16(data, table + 2);
        for (var i = 0; i < count && i < coverage.Count; i++)
            target[coverage[i]] = S16(data, table + 4 + (4 * i)) / unitsPerEm;
    }

    private static MathGlyphConstructionData ReadConstruction(ReadOnlySpan<byte> data, int construction, double unitsPerEm)
    {
        var assemblyOffset = U16(data, construction);
        var variantCount = U16(data, construction + 2);
        var variants = new List<MathGlyphVariant>(variantCount);
        for (var i = 0; i < variantCount; i++)
        {
            var record = construction + 4 + (4 * i);
            variants.Add(new MathGlyphVariant(U16(data, record), U16(data, record + 2) / unitsPerEm));
        }

        MathGlyphAssembly? assembly = null;
        if (assemblyOffset != 0)
        {
            var table = construction + assemblyOffset;
            var italic = S16(data, table) / unitsPerEm;
            var partCount = U16(data, table + 4);
            var parts = new List<MathGlyphPart>(partCount);
            for (var i = 0; i < partCount; i++)
            {
                var part = table + 6 + (10 * i);
                parts.Add(new MathGlyphPart(
                    U16(data, part),
                    U16(data, part + 2) / unitsPerEm,
                    U16(data, part + 4) / unitsPerEm,
                    U16(data, part + 6) / unitsPerEm,
                    (U16(data, part + 8) & 1) != 0));
            }

            assembly = new MathGlyphAssembly(italic, parts);
        }

        return new MathGlyphConstructionData(variants, assembly);
    }

    private static List<int> ReadCoverage(ReadOnlySpan<byte> data, int coverage)
    {
        var format = U16(data, coverage);
        var glyphs = new List<int>();
        if (format == 1)
        {
            var count = U16(data, coverage + 2);
            for (var i = 0; i < count; i++)
                glyphs.Add(U16(data, coverage + 4 + (2 * i)));
        }
        else if (format == 2)
        {
            var ranges = U16(data, coverage + 2);
            var byIndex = new SortedDictionary<int, int>();
            for (var i = 0; i < ranges; i++)
            {
                var record = coverage + 4 + (6 * i);
                var start = U16(data, record);
                var end = U16(data, record + 2);
                var startIndex = U16(data, record + 4);
                for (var glyph = start; glyph <= end; glyph++)
                    byIndex[startIndex + (glyph - start)] = glyph;
            }

            glyphs.AddRange(byIndex.Values);
        }
        else
        {
            throw new InvalidDataException("Unsupported coverage format.");
        }

        return glyphs;
    }

    private static string Tag(ReadOnlySpan<byte> data, int offset) =>
        string.Create(4, data.Slice(offset, 4).ToArray(), static (span, bytes) =>
        {
            for (var i = 0; i < 4; i++)
                span[i] = (char)bytes[i];
        });

    private static int U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));

    private static int S16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt16BigEndian(data.Slice(offset, 2));

    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));

    private sealed record MathGlyphConstructionData(IReadOnlyList<MathGlyphVariant> Variants, MathGlyphAssembly? Assembly);
}
