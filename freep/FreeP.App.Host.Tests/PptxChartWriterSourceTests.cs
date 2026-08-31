using System.IO;
using System.Text.RegularExpressions;

namespace FreeP.App.Host.Tests;

public sealed class PptxChartWriterSourceTests
{
    [Fact]
    public void ChartExPointFormatting_IndexesNativePointsOncePerSeries()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.Core.IO",
            "PptxChartWriter.cs"));
        var method = ExtractMethod(source, "private static void UpdateChartExSeriesDataPoints(");

        Regex.Matches(method, Regex.Escape("Elements(cx + \"dataPt\")"))
            .Should().HaveCount(1, "native ChartEx points should be enumerated once per series");
        method.Should()
            .Contain("var pointsByIndex = new Dictionary<int, XElement>()")
            .And.Contain("pointsByIndex.TryAdd(pointIndex, point)")
            .And.Contain("pointsByIndex.TryGetValue(pair.Key, out var point)")
            .And.Contain("pointsByIndex.TryAdd(pair.Key, point)")
            .And.Contain("var insertionAnchor = series[index].Elements()")
            .And.NotContain("TryParseChartExId(element.Attribute(\"idx\")?.Value) == pair.Key");
    }

    [Fact]
    public void ChartExDataEditing_IndexesValidatedDataIdsOnceBeforeMutation()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.Core.IO",
            "PptxChartWriter.cs"));
        var method = ExtractMethod(source, "private static void UpdateChartExData(");

        Regex.Matches(method, Regex.Escape("new Dictionary<int, XElement>()"))
            .Should().HaveCount(1, "native ChartEx data ids should be indexed once per chart");
        method.Should()
            .Contain("!dataById.TryAdd(dataId.Value, data)")
            .And.Contain("dataById.TryGetValue(dataId.Value, out var referencedData)")
            .And.NotContain("Select(data => (Data: data, Id:")
            .And.NotContain("dataById.FirstOrDefault")
            .And.NotContain("Distinct().Count()");
        method.IndexOf("dataById.TryGetValue", StringComparison.Ordinal)
            .Should().BeLessThan(method.IndexOf("ReplaceChartExPoints", StringComparison.Ordinal),
                "all references must resolve before the first chart-data mutation");
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"method '{signature}' should exist");

        var nextMethod = Regex.Match(
            source[(start + signature.Length)..],
            @"\r?\n    (private|internal|public) static ");

        return nextMethod.Success
            ? source[start..(start + signature.Length + nextMethod.Index)]
            : source[start..];
    }
}
