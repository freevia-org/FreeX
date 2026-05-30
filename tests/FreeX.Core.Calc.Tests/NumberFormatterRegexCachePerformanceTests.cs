using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public sealed class NumberFormatterRegexCachePerformanceTests
{
    [Theory]
    [InlineData("NumberFormatColorMapper.cs")]
    [InlineData("NumberFormatter.cs")]
    [InlineData("NumberFormatter.DateTime.cs")]
    [InlineData("NumberFormatter.Fractions.cs")]
    [InlineData("NumberFormatter.Sections.cs")]
    public void HotNumberFormatterParsers_UseCachedRegexInstances(string fileName)
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.Calc", fileName));

        source.Should().Contain("private static readonly Regex");
        source.Should().NotMatchRegex(StaticRegexCallPattern);
    }

    [Fact]
    public void HotNumberFormatterSectionSelection_ParsesSectionsInSinglePass()
    {
        var numberSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.Calc", "NumberFormatter.cs"));
        var dateTimeSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.Calc", "NumberFormatter.DateTime.cs"));
        var sectionsSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.Calc", "NumberFormatter.Sections.cs"));

        numberSource.Should().Contain("ParseSections(sections, indexedColors, theme, out var hasConditions)");
        dateTimeSource.Should().Contain("ParseSections(sections, indexedColors, theme, out var hasConditions)");
        sectionsSource.Should().Contain("private static ParsedSection[] ParseSections(");
        sectionsSource.Should().Contain("for (var i = 0; i < sections.Length; i++)");
        sectionsSource.Should().Contain("hasConditions |= parsedSection.Condition is not null;");
        numberSource.Should().NotContain("sections.Select(section => ParseSection");
        numberSource.Should().NotContain(".Any(section => section.Condition");
        dateTimeSource.Should().NotContain("sections.Select(section => ParseSection");
        dateTimeSource.Should().NotContain(".Any(section => section.Condition");
    }

    private const string StaticRegexCallPattern = @"\bRegex\.(?:Match|IsMatch|Replace)\s*\(";

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
