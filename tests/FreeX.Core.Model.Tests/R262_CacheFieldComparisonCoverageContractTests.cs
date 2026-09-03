using System.Reflection;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r262: the coverage contract for <c>PivotSnapshotComparison.SameCacheFields</c> -- written after
/// the fact, because its absence is what cost r261 a round.
///
/// <para>r261 added that comparison with the strip-and-compare shape and NO contract, the only one in
/// this program to ship without one. <see cref="PivotCacheFieldModel"/> carries two collection
/// members, <c>SharedItems</c> and <c>SharedItemKinds</c>; only the first was stripped, so the second
/// was compared by REFERENCE inside the stripped record equality and always differed after a refresh
/// rebuilt the field list. The visible symptom was a guard that never reported a no-op -- which looks
/// exactly like a command that genuinely writes every time, and so was misdiagnosed as the command's
/// fault and reverted.</para>
///
/// <para>Every comparison in this program that HAD a contract was safe from this. That is the whole
/// argument for the contracts, demonstrated on my own code rather than on a hypothetical.</para>
/// </summary>
public sealed class R262_CacheFieldComparisonCoverageContractTests
{
    [Fact]
    public void EveryReferenceComparedMemberOfTheCacheFieldModelIsStripped()
    {
        var referenceCompared = typeof(PivotCacheFieldModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite)
            .Where(property => !property.PropertyType.IsValueType && property.PropertyType != typeof(string))
            .Where(property => !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal))
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        referenceCompared.Should().BeEquivalentTo(["GroupItems", "SharedItemKinds", "SharedItems"],
            "PivotSnapshotComparison.Strip(PivotCacheFieldModel) removes exactly these three before "
            + "letting record equality cover the scalars, and compares each by content afterwards. A "
            + "third collection member added here would be compared by REFERENCE against a rebuilt "
            + "cache field list, so SameCacheFields would answer 'changed' forever and every guard "
            + "built on it would silently stop firing -- which is exactly what happened in r261 when "
            + "SharedItemKinds was the member left out.");
    }

    /// <summary>
    /// The list above is only meaningful while the comparison really does strip both. Reading the
    /// source keeps the two in step, the same way r249's and r259's Clone-derived contracts do.
    /// </summary>
    [Fact]
    public void TheComparisonStripsBothOfThem()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Commands"),
            "PivotSnapshotComparison.cs"));

        var stripStart = source.IndexOf("private static PivotCacheFieldModel Strip(", StringComparison.Ordinal);
        stripStart.Should().BeGreaterThan(0, "the strip helper must exist for this contract to check it");

        var stripBody = source[stripStart..source.IndexOf("};", stripStart, StringComparison.Ordinal)];
        stripBody.Should().Contain("SharedItems = ");
        stripBody.Should().Contain("SharedItemKinds = ");
        stripBody.Should().Contain("GroupItems = ");
    }
}
