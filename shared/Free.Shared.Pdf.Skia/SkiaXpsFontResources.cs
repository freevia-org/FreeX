using System.Buffers.Binary;
using System.Security.Cryptography;
using Free.Shared.Xps;
using SkiaSharp;

namespace Free.Shared.Pdf.Skia;

internal sealed class SkiaXpsFontResources
{
    private const int SfntHeaderSize = 12;
    private const int SfntTableRecordSize = 16;
    private const int EmbeddingFlagsOffset = 8;
    private const ushort RestrictedEmbedding = 0x0002;
    private const ushort PreviewPrintEmbedding = 0x0004;
    private const ushort BitmapEmbeddingOnly = 0x0200;

    private readonly Dictionary<SKTypeface, XpsFontResource?> _fonts = new();
    private readonly Dictionary<string, byte[]> _parts = new(StringComparer.Ordinal);

    public XpsFontResource? For(SKTypeface typeface)
    {
        if (_fonts.TryGetValue(typeface, out var cached))
            return cached;

        var resource = Read(typeface);
        _fonts.Add(typeface, resource);
        return resource;
    }

    private XpsFontResource? Read(SKTypeface typeface)
    {
        using var stream = typeface.OpenStream(out var faceIndex);
        if (stream is null)
            return null;

        using var data = SKData.Create(stream);
        if (data is null || data.Size > int.MaxValue)
            return null;

        var bytes = data.AsSpan();
        var extension = GetEmbeddableExtension(bytes, faceIndex, out var isRestricted);
        if (extension is null)
            return null;

        var key = SHA256.HashData(bytes).AsSpan(0, 16);
        var id = new Guid(key, bigEndian: true);
        var path = $"Resources/Fonts/{id:D}.{extension}";
        if (!_parts.TryGetValue(path, out var partBytes))
        {
            partBytes = bytes.ToArray();
            // XPS §9.1.7.3 XORs the first 32 font bytes with the part GUID's reversed
            // display-order bytes, repeated twice.
            for (var index = 0; index < 32; index++)
                partBytes[index] ^= key[15 - index % 16];
            _parts.Add(path, partBytes);
        }

        return new XpsFontResource(path, partBytes)
        {
            FontFaceIndex = faceIndex,
            IsRestricted = isRestricted
        };
    }

    // A collection is copied intact into one OPC part. Verify the permissions of every
    // face, including faces that aren't referenced by any Glyphs on the page.
    internal static string? GetEmbeddableExtension(ReadOnlySpan<byte> data, int faceIndex, out bool isRestricted)
    {
        isRestricted = false;
        if (faceIndex < 0 || data.Length < 32)
            return null;

        if (!data[..4].SequenceEqual("ttcf"u8))
            return faceIndex == 0 && IsEmbeddableFace(data, 0, out isRestricted) ? "odttf" : null;

        var faceCount = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
        if (faceIndex >= faceCount || faceCount > (data.Length - SfntHeaderSize) / sizeof(uint))
            return null;

        for (var index = 0; index < faceCount; index++)
        {
            var offset = SfntHeaderSize + index * sizeof(uint);
            var faceOffset = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            if (!IsEmbeddableFace(data, faceOffset, out var faceIsRestricted))
                return null;
            isRestricted |= faceIsRestricted;
        }

        return "odttc";
    }

    private static bool IsEmbeddableFace(ReadOnlySpan<byte> data, uint faceOffset, out bool isRestricted)
    {
        isRestricted = false;
        if (faceOffset > data.Length - SfntHeaderSize)
            return false;

        var face = data[(int)faceOffset..];
        var hasOpenTypeHeader = face[..4].SequenceEqual("OTTO"u8) ||
                                BinaryPrimitives.ReadUInt32BigEndian(face) == 0x00010000;
        var tableCount = BinaryPrimitives.ReadUInt16BigEndian(face[4..]);
        if (!hasOpenTypeHeader || tableCount > (face.Length - SfntHeaderSize) / SfntTableRecordSize)
            return false;

        ushort? embeddingFlags = null;
        var hasOutlines = false;
        for (var index = 0; index < tableCount; index++)
        {
            var table = face.Slice(SfntHeaderSize + index * SfntTableRecordSize, SfntTableRecordSize);
            var tag = table[..4];
            var offset = BinaryPrimitives.ReadUInt32BigEndian(table[8..]);
            var length = BinaryPrimitives.ReadUInt32BigEndian(table[12..]);
            if (offset > data.Length || length > data.Length - offset || tag.SequenceEqual("fvar"u8))
                return false;

            if (tag.SequenceEqual("OS/2"u8) && length >= EmbeddingFlagsOffset + sizeof(ushort))
                embeddingFlags = BinaryPrimitives.ReadUInt16BigEndian(data[((int)offset + EmbeddingFlagsOffset)..]);

            if (tag.SequenceEqual("glyf"u8) || tag.SequenceEqual("CFF "u8))
                hasOutlines = true;
        }

        // Preserve the whole font, including fonts whose license prohibits subsetting.
        const ushort unsupportedFlags = RestrictedEmbedding | BitmapEmbeddingOnly;
        if (!hasOutlines || embeddingFlags is not { } flags || (flags & unsupportedFlags) != 0)
            return false;

        isRestricted = (flags & PreviewPrintEmbedding) != 0;
        return true;
    }
}
