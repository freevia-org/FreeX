using System.Reflection;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r234: the coverage contract behind <c>CellEditCompanionSnapshot.SameCell</c>. That helper decides
/// whether a command's writes actually changed anything, and thirteen commands are meant to depend
/// on it -- so a field added to <see cref="Cell"/> and forgotten here would be a partial mirror
/// thirteen times over, each one silently reporting "nothing changed" for an edit that did.
/// <para>
/// This reads the SOURCE of SameCell rather than reflecting over its behaviour, for the same reason
/// r208's contract does: what needs asserting is that the author considered every member, and that
/// is a property of the text. Every settable member of Cell must appear in SameCell or carry an
/// exemption with a reason.
/// </para>
/// </summary>
public sealed class R234_CellChangeComparisonCoverageContractTests
{
    /// <summary>Members deliberately excluded from the comparison, with why.</summary>
    private static readonly Dictionary<string, string> DeliberatelyNotCompared = new()
    {
        ["CachedAst"] =
            "a derived parse cache, cleared automatically whenever FormulaText changes. Two cells "
            + "with the same formula are the same cell whether or not either has been parsed yet, "
            + "so comparing it would report a change for work the user did not do",
    };

    [Fact]
    public void EverySettableCellMemberIsComparedOrExempted()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Commands"),
            "CellEditCompanionSnapshot.cs"));

        var start = source.IndexOf("internal static bool SameCell(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "SameCell must exist for this contract to mean anything");
        var body = source[start..source.IndexOf(';', start)];

        var settable = typeof(Cell).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite)
            .Select(property => property.Name)
            .ToList();

        settable.Should().HaveCountGreaterThan(5,
            "an empty or tiny reflection result would make this test pass while guarding nothing");

        var missing = settable
            .Where(name => !DeliberatelyNotCompared.ContainsKey(name))
            .Where(name => !body.Contains("left." + name, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "a settable member of Cell that SameCell does not look at is a field this comparison "
            + "will call unchanged when it changed. Compare it, or add it to "
            + "DeliberatelyNotCompared with the reason. Missing:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryExemptionStillNamesALiveCellMember()
    {
        var live = typeof(Cell).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        DeliberatelyNotCompared.Keys.Where(name => !live.Contains(name))
            .Should().BeEmpty("a stale exemption would silently cover a future member of that name");
    }
}
