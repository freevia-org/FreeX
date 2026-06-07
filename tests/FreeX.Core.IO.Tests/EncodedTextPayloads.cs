using System.Text;

namespace FreeX.Core.IO.Tests;

internal static class EncodedTextPayloads
{
    internal static byte[] WithBom(Encoding encoding, string text) =>
        encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();

    internal static TheoryData<byte[]> Utf16BomPayloads(string text) => new()
    {
        WithBom(Encoding.Unicode, text),
        WithBom(Encoding.BigEndianUnicode, text)
    };

    internal static TheoryData<byte[]> Utf32BomPayloads(string text)
    {
        var bigEndianUtf32 = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        return new TheoryData<byte[]>
        {
            WithBom(Encoding.UTF32, text),
            WithBom(bigEndianUtf32, text)
        };
    }
}
