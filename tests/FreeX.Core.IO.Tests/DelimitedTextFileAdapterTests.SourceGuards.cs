using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class DelimitedTextFileAdapterTests
{
    [Fact]
    public void Load_DecodesBufferedTextWithoutCopyingMemoryStreamToArray()
    {
        var source = TestWorkspaceFiles.ReadCoreIoSource("DelimitedTextWorkbookReader.cs");

        source.Should().NotContain(
            "memory.ToArray()",
            "text load should decode the buffered stream segment without duplicating the full byte array");
    }

    [Fact]
    public void Load_CoercesValuesWithoutRepeatedTrimOrUppercaseAllocations()
    {
        var source = TestWorkspaceFiles.ReadCoreIoSource("DelimitedTextWorkbookReader.cs");
        var coercion = source[
            source.IndexOf("private static ScalarValue CoerceValue", StringComparison.Ordinal)..
            source.IndexOf("private static bool TryReadError", StringComparison.Ordinal)];

        CountOccurrences(coercion, ".Trim()").Should().Be(1);
        coercion.Should().Contain("var trimmed = value.Trim();");
        coercion.Should().NotContain("ToUpperInvariant()");
    }

    [Fact]
    public void Save_ReusesDelimiterBuffersInsteadOfAllocatingPerChunk()
    {
        var source = TestWorkspaceFiles.ReadCoreIoSource("DelimitedTextWorkbookWriter.cs");

        source.Should().NotContain(
            "new string(delimiter",
            "wide sparse delimited saves should not allocate delimiter strings for every row chunk");
    }

    [Fact]
    public void Save_SortsSnapshotInPlaceWithoutDuplicatingCellsIntoRowBuckets()
    {
        var source = TestWorkspaceFiles.ReadCoreIoSource("DelimitedTextWorkbookWriter.cs");

        source.Should().Contain("Array.Sort(cells, static (left, right) =>");
        source.Should().Contain("WriteRow(writer, delimiter, cells, rowStart, rowEnd, endCol, workbook, numberProvider);");
        source.Should().NotContain("EstimateRowCapacity");
        source.Should().NotContain("rowLookup");
        source.Should().NotContain("DelimitedTextRowBucket");
    }

    [Fact]
    public void Save_StreamsNumericValuesWithoutPerCellStringAllocation()
    {
        var source = TestWorkspaceFiles.ReadCoreIoSource("DelimitedTextWorkbookWriter.cs");

        // r393 added the number provider parameter (plain CSV writes the locale decimal mark, as
        // Excel does). The guard's subject is unchanged: the dense path must format into a stack
        // buffer and write it straight to the TextWriter, with no per-cell string and no trip
        // through WriteField's quoting.
        source.Should().Contain("WriteNumberValue(writer, delimiter, cell, number.Value, workbook, numberProvider);");
        source.Should().Contain("value.TryFormat(buffer, out var charsWritten, provider: numberProvider)");
        source.Should().Contain("writer.Write(buffer[..charsWritten]);");
        source.Should().NotContain("WriteField(writer, delimiter, new string(buffer");
        source.Should().NotContain("NumberValue n => n.Value.ToString(CultureInfo.InvariantCulture)");
    }

    [Fact]
    public void Save_ReadsNumberFormatWithoutCloningTheWholeCellStyle()
    {
        var source = TestWorkspaceFiles.ReadCoreIoSource("DelimitedTextWorkbookWriter.cs");

        source.Should().Contain("numberFormat = workbook.GetStyleNumberFormat(cell.StyleId);");
        source.Should().NotContain(
            "workbook.GetStyle(cell.StyleId)",
            "the dense export path only needs an immutable format string and must not allocate a defensive style clone per cell");
    }

    [Fact]
    public void Save_UsesSinglePassDateTimeShapeProbeWithoutLinq()
    {
        var source = TestWorkspaceFiles.ReadCoreIoSource("DelimitedTextWorkbookWriter.cs");
        var shapeProbe = source[
            source.IndexOf("private static bool HasSupportedDateTimeShape", StringComparison.Ordinal)..
            source.IndexOf("private static bool IsUnsignedCurrencyText", StringComparison.Ordinal)];

        shapeProbe.Should().Contain("foreach (var ch in value)");
        shapeProbe.Should().Contain("if (ch == ':' || char.IsLetter(ch))");
        shapeProbe.Should().Contain("if (digitRun >= 4)");
        shapeProbe.Should().NotContain("value.Contains");
        shapeProbe.Should().NotContain("HasFourConsecutiveDigits");
        shapeProbe.Should().NotContain(".Any(");
    }

}
