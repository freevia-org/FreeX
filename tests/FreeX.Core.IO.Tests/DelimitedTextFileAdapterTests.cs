using System.Globalization;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class DelimitedTextFileAdapterTests
{
    public static TheoryData<byte[]> Utf32BomDelimitedTextPayloads() =>
        EncodedTextPayloads.Utf32BomPayloads("Name\tAmount\tFlag\r\nCaf\u00e9\t42\tTRUE\r\n");

    private static int CountOccurrences(string value, string text)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(text, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += text.Length;
        }

        return count;
    }
}
