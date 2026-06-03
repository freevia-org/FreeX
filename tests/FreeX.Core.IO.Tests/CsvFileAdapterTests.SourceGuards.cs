using System.Diagnostics;
using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit.Abstractions;

namespace FreeX.Core.IO.Tests;

public sealed partial class CsvFileAdapterTests
{
    [Fact]
    public void Save_ScansCellsWithoutCopyingUsedCellDictionary()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "CsvFileAdapter.cs"));

        source.Should().NotContain(
            "GetUsedCells()",
            "CSV save should build its output index in one streaming pass over occupied cells");
    }

    [Fact]
    public void Save_GroupsCellsByRowInsteadOfIndexingEveryCoordinate()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "CsvFileAdapter.cs"));

        source.Should().NotContain("Dictionary<(uint Row, uint Col), Cell>");
        source.Should().NotContain("TryGetValue((r, c)");
    }

    [Fact]
    public void Save_StreamsRowsWithoutPerRowStringArrayJoin()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "CsvFileAdapter.cs"));

        source.Should().NotContain("new string[endCol - startCol + 1]");
        source.Should().NotContain("string.Join(',', parts)");
    }

    [Fact]
    public void Load_ReusesAccessibleMemoryStreamBufferBeforeCopying()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.IO", "DelimitedTextWorkbookReader.cs"));

        source.Should().Contain("TryGetBuffer(out var sourceBytes)");
        source.Should().NotContain(
            "stream.CopyTo(memory);",
            "accessible MemoryStream inputs should decode their remaining buffer slice without copying first");
    }

    [Fact]
    public void Load_CoercesPlainNumbersBeforeExpensiveDateTimeProbes()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.IO", "DelimitedTextWorkbookReader.cs"));
        var start = source.IndexOf("private static ScalarValue CoerceValue", StringComparison.Ordinal);
        var end = source.IndexOf("private static bool TryReadError", start, StringComparison.Ordinal);
        var coerceValue = source[start..end];

        coerceValue.IndexOf("TryParseFiniteNumber(trimmed", StringComparison.Ordinal)
            .Should()
            .BeLessThan(coerceValue.IndexOf("TryParseDateTime(trimmed", StringComparison.Ordinal));
    }

}
