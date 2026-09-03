using System.Reflection;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r248: the coverage contract behind WorksheetCustomViewStateComparer, built on r234's pattern for
/// the same reason -- thirty members is well past the point where re-reading the comparison is a
/// check, and a member added to the record and forgotten there would silently report "no change"
/// for a view that did change.
/// </summary>
public sealed class R248_ViewStateComparisonCoverageContractTests
{
    /// <summary>Members deliberately excluded from the comparison, with why.</summary>
    private static readonly Dictionary<string, string> DeliberatelyNotCompared = new();

    [Fact]
    public void EveryViewStateMemberIsComparedOrExempted()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Commands"),
            "WorksheetCustomViewStateComparer.cs"));

        var start = source.IndexOf("internal static bool Same(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "Same must exist for this contract to mean anything");
        var body = source[start..source.IndexOf(';', start)];

        var members = typeof(WorksheetCustomViewState)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Where(name => name != "EqualityContract")
            .ToList();

        members.Should().HaveCountGreaterThan(20,
            "an empty or tiny reflection result would make this pass while guarding nothing");

        var missing = members
            .Where(name => !DeliberatelyNotCompared.ContainsKey(name))
            .Where(name => !body.Contains("left." + name, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "a member of WorksheetCustomViewState the comparison does not look at is a thing a "
            + "custom view can change and the guard will call unchanged. Compare it, or exempt it "
            + "with a reason. Missing:\n" + string.Join("\n", missing));
    }
}
