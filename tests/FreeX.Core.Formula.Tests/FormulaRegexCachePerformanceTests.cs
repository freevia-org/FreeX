using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public sealed class FormulaRegexCachePerformanceTests
{
    [Fact]
    public void TextNumberParser_UsesCachedRegexesForRepeatedCoercionChecks()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.Formula", "ExcelTextNumberParser.cs"));

        source.Should().Contain("private static readonly Regex FakeLeapDayTextRegex");
        source.Should().Contain("private static readonly Regex MonthNameRegex");
        source.Should().Contain("private static readonly Regex AmPmRegex");
        source.Should().NotMatchRegex(StaticRegexCallPattern);
    }

    [Fact]
    public void DateTimeFunctions_UseCachedRegexesForRepeatedComponentChecks()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.Formula", "BuiltInFunctions.DateTime.cs"));

        source.Should().Contain("private static readonly Regex DateTimeTextHasTimeSeparatorRegex");
        source.Should().Contain("private static readonly Regex DateTimeTextHasDateSeparatorRegex");
        source.Should().NotMatchRegex(StaticRegexCallPattern);
    }

    private const string StaticRegexCallPattern = @"\bRegex\.(?:Match|IsMatch)\s*\(";

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
