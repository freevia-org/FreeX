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

        inScopeCount.Should().Be(487);
    }

    [Fact]
    public void FunctionParityDocument_CategorySummariesMatchFunctionRows()
    {
        var documented = ReadDocumentedFunctionsBySection();
        var summary = ReadCoverageSummary();

        foreach (var (category, counts) in summary.Where(entry => entry.Key != "TOTAL"))
        {
            var section = SummaryCategoryToSection(category);
            documented.Should().ContainKey(section);

            var rows = documented[section];
            counts.Implemented.Should().Be(rows.Count(entry => entry.Status.StartsWith("Implemented", StringComparison.OrdinalIgnoreCase)), category);
            counts.Partial.Should().Be(rows.Count(entry => entry.Status.StartsWith("Partial", StringComparison.OrdinalIgnoreCase)), category);
            counts.NotImplemented.Should().Be(rows.Count(entry => entry.Status.StartsWith("Not Implemented", StringComparison.OrdinalIgnoreCase)), category);
            counts.Excluded.Should().Be(rows.Count(entry => entry.Status.StartsWith("Excluded", StringComparison.OrdinalIgnoreCase)), category);
            counts.InScopeTotal.Should().Be(rows.Count(entry => !entry.Status.StartsWith("Excluded", StringComparison.OrdinalIgnoreCase)), category);
        }

        var totals = summary["TOTAL"];
        totals.Implemented.Should().Be(summary.Where(entry => entry.Key != "TOTAL").Sum(entry => entry.Value.Implemented));
        totals.Partial.Should().Be(summary.Where(entry => entry.Key != "TOTAL").Sum(entry => entry.Value.Partial));
        totals.NotImplemented.Should().Be(summary.Where(entry => entry.Key != "TOTAL").Sum(entry => entry.Value.NotImplemented));
        totals.Excluded.Should().Be(summary.Where(entry => entry.Key != "TOTAL").Sum(entry => entry.Value.Excluded));
        totals.InScopeTotal.Should().Be(summary.Where(entry => entry.Key != "TOTAL").Sum(entry => entry.Value.InScopeTotal));
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

    private static IReadOnlyDictionary<string, IReadOnlyList<(string Name, string Status)>> ReadDocumentedFunctionsBySection()
    {
        var path = FindWorkspaceFile("docs", "FUNCTION_PARITY.md");
        var sections = new Dictionary<string, List<(string Name, string Status)>>(StringComparer.Ordinal);
        var currentSection = "";
        var rowPattern = new Regex(@"^\|\s*(?<name>[A-Z][A-Z0-9._]*)\s*\|\s*(?<status>[^|]+?)\s*\|$");
        var sectionPattern = new Regex(@"^##\s+(?<section>.+?)\s*$");

        foreach (var line in File.ReadLines(path))
        {
            var sectionMatch = sectionPattern.Match(line);
            if (sectionMatch.Success)
            {
                currentSection = sectionMatch.Groups["section"].Value.Trim();
                sections.TryAdd(currentSection, []);
                continue;
            }

            var rowMatch = rowPattern.Match(line);
            if (!rowMatch.Success)
                continue;

            var name = rowMatch.Groups["name"].Value.Trim();
            var status = rowMatch.Groups["status"].Value.Trim();
            if (name == "Function" || name == "TOTAL")
                continue;

            sections[currentSection].Add((name, status));
        }

        return sections.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<(string Name, string Status)>)entry.Value,
            StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, CoverageSummaryCounts> ReadCoverageSummary()
    {
        var path = FindWorkspaceFile("docs", "FUNCTION_PARITY.md");
        var rows = new Dictionary<string, CoverageSummaryCounts>(StringComparer.Ordinal);
        var inSummary = false;

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("## Coverage Summary", StringComparison.Ordinal))
            {
                inSummary = true;
                continue;
            }

            if (inSummary && line.StartsWith("## ", StringComparison.Ordinal))
                break;
            if (!inSummary || !line.StartsWith("|", StringComparison.Ordinal))
                continue;

            var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim().Trim('*')).ToArray();
            if (cells.Length != 7 || cells[0] == "---" || cells[0] == "Category")
                continue;

            rows[cells[0]] = new CoverageSummaryCounts(
                ParseCount(cells[1]),
                ParseCount(cells[2]),
                ParseCount(cells[3]),
                ParseCount(cells[4]),
                ParseCount(cells[5]));
        }

        return rows;
    }

    private static string SummaryCategoryToSection(string category) => category switch
    {
        "Lambda / Advanced" => "Lambda / Advanced Calculation",
        _ => category
    };

    private static int ParseCount(string value) =>
        int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private readonly record struct CoverageSummaryCounts(
        int Implemented,
        int Partial,
        int NotImplemented,
        int Excluded,
        int InScopeTotal);

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
