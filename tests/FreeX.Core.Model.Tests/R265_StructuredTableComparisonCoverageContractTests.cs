using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r265: the coverage contract for <c>StructuredTableComparison</c>, on the r249 pattern.
///
/// <para><c>StructuredTableModel.CaptureCopyState</c> is the model's own maintained enumeration of
/// what a table consists of -- its doc comment says "captures every table field", and every
/// structural edit round-trips through it -- so it is the field list. Twenty-seven members is well
/// past the point where re-reading is a check, and r262 established what an unchecked comparison of
/// this shape costs.</para>
/// </summary>
public sealed class R265_StructuredTableComparisonCoverageContractTests
{
    [Fact]
    public void SameComparesEveryMemberCaptureCopyStateRecords()
    {
        var modelSource = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Model"),
            "StructuredTableModel.cs"));

        var captureStart = modelSource.IndexOf("internal StructuredTableCopyState CaptureCopyState() =>", StringComparison.Ordinal);
        captureStart.Should().BeGreaterThan(0, "CaptureCopyState must exist -- it is this contract's field list");
        var captureBody = modelSource[captureStart..modelSource.IndexOf(';', captureStart)];

        // \r? before the anchor: these sources are CRLF, and Multiline's $ matches before the \n,
        // leaving the carriage return unmatched. Without it this found zero members -- caught by the
        // count assertion below, which is the third parse bug it has caught in three rounds.
        var captured = new Regex(@"^\s+([A-Z]\w*),?\r?$", RegexOptions.Multiline)
            .Matches(captureBody)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        captured.Should().HaveCountGreaterThan(20,
            "a short field list would mean the parse broke and this contract passed while guarding nothing");

        var comparisonSource = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Commands"),
            "StructuredTableComparison.cs"));

        var sameStart = comparisonSource.IndexOf("internal static bool Same(StructuredTableModel", StringComparison.Ordinal);
        sameStart.Should().BeGreaterThan(0, "Same must exist for this contract to check anything");
        var sameBody = comparisonSource[sameStart..comparisonSource.IndexOf("\n    /// <summary>", sameStart, StringComparison.Ordinal)];

        var missing = captured
            .Where(name => !Regex.IsMatch(sameBody, @"left\." + Regex.Escape(name) + @"\b"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "CaptureCopyState records these because a table copy would otherwise lose them, so the "
            + "comparison ignoring one means a no-op decision that calls a changed table unchanged. "
            + "Missing:\n" + string.Join("\n", missing));
    }

    /// <summary>
    /// The element type's own collections, which the strip-and-compare shape depends on. This is the
    /// exact assumption that was wrong in r262, where a model turned out to carry three collections
    /// and the comparison stripped one.
    /// </summary>
    [Fact]
    public void TheColumnModelsReferenceComparedMembersAreExactlyTheStrippedOnes()
    {
        var referenceCompared = typeof(StructuredTableColumnModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite)
            .Where(property => !property.PropertyType.IsValueType && property.PropertyType != typeof(string))
            .Where(property => !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal))
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        referenceCompared.Should().BeEquivalentTo(["NativeAttributes", "NativeChildXmls"],
            "StructuredTableComparison strips exactly these two from each column before letting "
            + "record equality cover the scalars. A third collection member added here would be "
            + "compared by REFERENCE against a rebuilt column list, so the comparison would answer "
            + "'changed' forever and the guard would silently stop firing -- the r262 failure exactly.");
    }
}
