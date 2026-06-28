using System.IO;
using System.Text.RegularExpressions;

namespace FreeP.App.Host.Tests;

public sealed class PptxPackageReaderSourceTests
{
    [Fact]
    public void SmartArtAndDspXmlParsing_UsesSharedOpcXmlLoader()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "freep",
            "FreeP.Core.IO",
            "PptxPackageReader.cs"));

        ExtractMethod(source, "private static SmartArtData? ReadSmartArtData(")
            .Should()
            .Contain("OpcXml.LoadXml(")
            .And.NotContain("XDocument.Load(");

        ExtractMethod(source, "private static void ReadDspDrawing(")
            .Should()
            .Contain("OpcXml.LoadXml(")
            .And.NotContain("XDocument.Load(");
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

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
