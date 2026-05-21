using Freexcel.Core.Formula;
using FluentAssertions;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Freexcel.Core.Formula.Tests;

public sealed class FormulaParityCatalogTests
{
    private static readonly string[] ExcludedFunctionNames =
    [
        "CUBEKPIMEMBER",
        "CUBEMEMBER",
        "CUBESET",
        "CUBESETCOUNT",
        "CUBEVALUE",
        "RTD",
        "WEBSERVICE"
    ];

    [Fact]
    public void DocumentedInScopeFunctions_AreRegisteredBuiltIns()
    {
        var documented = ReadDocumentedFunctions()
            .Where(entry => !entry.Status.StartsWith("Excluded", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Name)
            .OrderBy(name => name)
            .ToArray();

        BuiltInFunctions.Names
            .OrderBy(name => name)
            .Should()
            .Contain(documented);
    }

    [Fact]
    public void ExcludedFunctions_AreNotRegisteredBuiltIns()
    {
        BuiltInFunctions.Names.Should().NotIntersectWith(ExcludedFunctionNames);
    }

    [Fact]
    public void Registry_DoesNotContainUndocumentedFunctions()
    {
        var documented = ReadDocumentedFunctions()
            .Where(entry => !entry.Status.StartsWith("Excluded", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        BuiltInFunctions.Names
            .Where(name => !documented.Contains(name))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void FunctionParityDocument_TotalMatchesImplementedRows()
    {
        var inScopeCount = ReadDocumentedFunctions()
            .Count(entry => !entry.Status.StartsWith("Excluded", StringComparison.OrdinalIgnoreCase));

        inScopeCount.Should().Be(345);
    }

    private static IReadOnlyList<(string Name, string Status)> ReadDocumentedFunctions()
    {
        var path = FindWorkspaceFile("docs", "FUNCTION_PARITY.md");
        var rows = new List<(string Name, string Status)>();
        var rowPattern = new Regex(@"^\|\s*(?<name>[A-Z][A-Z0-9._]*)\s*\|\s*(?<status>[^|]+?)\s*\|$");

        foreach (var line in File.ReadLines(path))
        {
            var match = rowPattern.Match(line);
            if (!match.Success)
                continue;

            var name = match.Groups["name"].Value.Trim();
            var status = match.Groups["status"].Value.Trim();
            if (name == "Function" || name == "TOTAL")
                continue;

            rows.Add((name, status));
        }

        return rows;
    }

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        return FindWorkspaceFileFromSource(relativeParts);
    }

    private static string FindWorkspaceFileFromSource(
        string[] relativeParts,
        [CallerFilePath] string sourceFilePath = "")
    {
        var cwdCandidate = Path.Combine(new[] { Directory.GetCurrentDirectory() }.Concat(relativeParts).ToArray());
        if (File.Exists(cwdCandidate))
            return cwdCandidate;

        var current = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
