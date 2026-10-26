using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r266: coverage contracts for the two comparisons MoveRange's decision will need.
///
/// <para>Written before the decision that uses them, and they earned it: the sparkline comparison as
/// first drafted covered EIGHT of the model's twenty-nine members. That is the r262 failure exactly
/// -- a comparison that looks complete, compiles, and silently answers "unchanged" for a sparkline
/// whose colours or axis scaling moved -- and it was caught by writing the contract rather than by
/// re-reading the code.</para>
/// </summary>
public sealed class R266_MoveRangeComparisonCoverageContractTests
{
    [Fact]
    public void SameSparklineComparesEveryMemberOfTheModel()
    {
        var comparison = ComparisonSource();
        var start = comparison.IndexOf("internal static bool SameSparkline(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "SameSparkline must exist for this contract to check anything");
        var body = comparison[start..comparison.IndexOf(';', start)];

        var members = typeof(SparklineModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        members.Should().HaveCountGreaterThan(20,
            "a short member list would mean the reflection broke and this passed while guarding nothing");

        var missing = members
            .Where(name => !Regex.IsMatch(body, @"left\." + Regex.Escape(name) + @"\b"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "a move rewrites a sparkline IN PLACE on the captured instance, so identity says "
            + "'unchanged' for one that moved and every member has to be compared explicitly. "
            + "Missing:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void SparklineModelStillCarriesNoCollectionMember()
    {
        var collections = typeof(SparklineModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => !property.PropertyType.IsValueType && property.PropertyType != typeof(string))
            .Select(property => $"{property.Name} ({property.PropertyType.Name})")
            .ToList();

        collections.Should().BeEmpty(
            "SameSparkline compares every member with == or Equals, which is content equality only "
            + "while all of them are scalars. A collection member added here would be compared by "
            + "REFERENCE and would need stripping, exactly as PivotCacheFieldModel did in r262. "
            + "Found:\n" + string.Join("\n", collections));
    }

    [Fact]
    public void SameChartVerbatimComparesEveryMemberOfTheSnapshot()
    {
        var snapshotSource = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Commands"),
            "RowColumnShiftHelpers.PrintAndCharts.cs"));

        var typeStart = snapshotSource.IndexOf("internal sealed class ChartVerbatimSnapshot", StringComparison.Ordinal);
        typeStart.Should().BeGreaterThan(0, "ChartVerbatimSnapshot must exist for this contract to check it");
        var typeBody = snapshotSource[typeStart..snapshotSource.IndexOf("\n    }", typeStart, StringComparison.Ordinal)];

        var declared = new Regex(@"public\s+[\w<>,\?\s\.\(\)]+?\s([A-Z]\w*)\s*\{\s*get", RegexOptions.Compiled)
            .Matches(typeBody)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        declared.Should().HaveCountGreaterThan(3,
            "a short member list would mean the parse broke and this passed while guarding nothing");

        var comparison = ComparisonSource();
        var start = comparison.IndexOf("internal static bool SameChartVerbatim(", StringComparison.Ordinal);
        var body = comparison[start..comparison.IndexOf("\n    /// <summary>", start, StringComparison.Ordinal)];

        var missing = declared
            .Where(name => !Regex.IsMatch(body, @"left\." + Regex.Escape(name) + @"\b"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "a move can rewrite any of these formula collections, so a member left out means the "
            + "decision cannot see that rewrite. Missing:\n" + string.Join("\n", missing));
    }

    private static string ComparisonSource() => File.ReadAllText(Path.Combine(
        TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Commands"),
        "MoveRangeSnapshotComparison.cs"));
}
