using System;
using System.IO;
using System.Linq;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class R55_FormulaPointWholeAreaTests
{
    [Fact]
    public void WpfWholeAreaAddPath_AppendsBeforeReplacement()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "FreeX.App.Host", "MainWindow.Selection.cs"));

        var columnMethod = ExtractMethod(source, "private void AddAdditionalColumnSelection");
        var rowMethod = ExtractMethod(source, "private void AddAdditionalRowSelection");

        columnMethod.Should().Contain("TryAppendDisjointFormulaRangeReference(range)");
        rowMethod.Should().Contain("TryAppendDisjointFormulaRangeReference(range)");
        columnMethod.IndexOf("TryAppendDisjointFormulaRangeReference(range)", StringComparison.Ordinal)
            .Should().BeLessThan(columnMethod.IndexOf("TryApplyFormulaRangeSelection", StringComparison.Ordinal));
        rowMethod.IndexOf("TryAppendDisjointFormulaRangeReference(range)", StringComparison.Ordinal)
            .Should().BeLessThan(rowMethod.IndexOf("TryApplyFormulaRangeSelection", StringComparison.Ordinal));
        source.Should().Contain("private bool TryAppendDisjointFormulaRangeReference(GridRange range)");
        source.Should().NotContain("ApplyWholeRowOrColumnFormulaReferenceShorthand");
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);

        var brace = source.IndexOf('{', start);
        brace.Should().BeGreaterThanOrEqualTo(0);

        var depth = 0;
        for (var index = brace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0)
                return source[start..(index + 1)];
        }

        throw new InvalidOperationException($"Could not find end of {signature}.");
    }

    private static string FindRepositoryFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(relativeParts);
}
