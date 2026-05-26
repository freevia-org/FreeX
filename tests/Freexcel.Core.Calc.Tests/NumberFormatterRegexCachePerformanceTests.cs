using System.Text.RegularExpressions;
using FluentAssertions;

namespace Freexcel.Core.Calc.Tests;

public sealed class NumberFormatterRegexCachePerformanceTests
{
    [Theory]
    [InlineData("NumberFormatColorMapper.cs")]
    [InlineData("NumberFormatter.DateTime.cs")]
    [InlineData("NumberFormatter.Fractions.cs")]
    [InlineData("NumberFormatter.Sections.cs")]
    public void HotNumberFormatterParsers_UseCachedRegexInstances(string fileName)
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.Calc", fileName));

        source.Should().Contain("private static readonly Regex");
        source.Should().NotMatchRegex(StaticRegexCallPattern);
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
