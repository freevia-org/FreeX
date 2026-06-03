using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class DelimitedTextFileAdapterTests
{
    private static string FindWorkspaceFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate workspace file {Path.Combine(parts)}.");
    }

    public static TheoryData<byte[]> Utf32BomDelimitedTextPayloads() => new()
    {
        Encoding.UTF32.GetPreamble()
            .Concat(Encoding.UTF32.GetBytes("Name\tAmount\tFlag\r\nCaf\u00e9\t42\tTRUE\r\n"))
            .ToArray(),
        new UTF32Encoding(bigEndian: true, byteOrderMark: true)
            .GetPreamble()
            .Concat(new UTF32Encoding(bigEndian: true, byteOrderMark: true)
                .GetBytes("Name\tAmount\tFlag\r\nCaf\u00e9\t42\tTRUE\r\n"))
            .ToArray()
    };

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
