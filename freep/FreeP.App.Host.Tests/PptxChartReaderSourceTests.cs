using System.IO;
using System.Text.RegularExpressions;

namespace FreeP.App.Host.Tests;

public sealed class PptxChartReaderSourceTests
{
    [Fact]
    public void ChartExSeriesData_IndexesIdsOnceAndUsesConstantTimeLookup()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.Core.IO",
            "PptxChartReader.cs"));
        var readMethod = ExtractMethod(source, "internal static ChartShape? ReadChartExPart(");
        var lookupMethod = ExtractMethod(source, "private static XElement? FindChartExSeriesData(");

        Regex.Matches(readMethod, Regex.Escape("new Dictionary<int, XElement>()"))
            .Should().HaveCount(1, "ChartEx data ids should be indexed once per chart");
        readMethod.Should()
            .Contain("dataById.TryAdd(ParseInt(dataElement.Attribute(\"id\")?.Value), dataElement)")
            .And.Contain("FindChartExSeriesData(dataElements, dataById, seriesElement)");
        lookupMethod.Should()
            .Contain("dataById.TryGetValue(id, out var data)")
            .And.Contain("dataElements.Count == 1 ? dataElements[0] : null")
            .And.NotContain("dataElements.FirstOrDefault")
            .And.NotContain("ParseInt(data.Attribute(\"id\")?.Value)");
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
