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
        var source = File.ReadAllText(TestWorkspaceFiles.FindWorkspaceFile(
            "src", "FreeX.Core.IO", "DelimitedTextWorkbookReader.cs"));

        source.Should().NotContain(
            "memory.ToArray()",
            "text load should decode the buffered stream segment without duplicating the full byte array");
    }

    [Fact]
    public void Load_CoercesValuesWithoutRepeatedTrimOrUppercaseAllocations()
    {
        var source = File.ReadAllText(TestWorkspaceFiles.FindWorkspaceFile(
            "src", "FreeX.Core.IO", "DelimitedTextWorkbookReader.cs"));
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
        var source = File.ReadAllText(TestWorkspaceFiles.FindWorkspaceFile(
            "src", "FreeX.Core.IO", "DelimitedTextWorkbookWriter.cs"));

        source.Should().NotContain(
            "new string(delimiter",
            "wide sparse delimited saves should not allocate delimiter strings for every row chunk");
    }

    [Fact]
    public void Save_StreamsNumericValuesWithoutPerCellStringAllocation()
    {
        var source = File.ReadAllText(TestWorkspaceFiles.FindWorkspaceFile(
            "src", "FreeX.Core.IO", "DelimitedTextWorkbookWriter.cs"));

        source.Should().Contain("WriteNumberValue(writer, number.Value);");
        source.Should().Contain("value.TryFormat(buffer, out var charsWritten, provider: CultureInfo.InvariantCulture)");
        source.Should().NotContain("NumberValue n => n.Value.ToString(CultureInfo.InvariantCulture)");
    }

    [Fact]
    public void Save_UsesSinglePassDateTimeShapeProbeWithoutLinq()
    {
        var source = File.ReadAllText(TestWorkspaceFiles.FindWorkspaceFile(
            "src", "FreeX.Core.IO", "DelimitedTextWorkbookWriter.cs"));
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
