using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r259: the two coverage contracts this round's decisions rest on. They guard opposite risks.
///
/// <para><see cref="ChartDataTableModel"/> is a CLASS captured by <c>Clone</c>, so it needs a
/// hand-written member comparison, and the risk is that the comparison falls behind the type. The
/// field list comes from <c>Clone</c>, which has to be complete or cloning would lose formatting.</para>
///
/// <para><c>PivotOptionsSnapshot</c> is the opposite: nothing is hand-listed, because the decision
/// re-runs its <c>Capture</c> and compares records. That is complete by construction -- but only
/// while record equality is content equality for it, which holds only while every member is a
/// scalar. A collection member added there would be compared by REFERENCE against a freshly captured
/// snapshot, so the comparison would answer "changed" forever and the guard would quietly stop
/// firing. Nothing about the code would look wrong.</para>
/// </summary>
public sealed class R259_ChartDataTableAndPivotOptionsContractTests
{
    [Fact]
    public void ChartDataTableSameAsComparesEveryMemberCloneCopies()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Model"),
            "ChartModel.Support.cs"));

        var recordBody = TypeBody(source, "public sealed class ChartDataTableModel");
        recordBody.Should().NotBeNullOrEmpty("ChartDataTableModel must exist for this contract to check it");

        var cloneBody = MemberBody(recordBody!, recordBody!.IndexOf("public ChartDataTableModel Clone()", StringComparison.Ordinal));
        var sameBody = MemberBody(recordBody, recordBody.IndexOf("public bool SameAs(", StringComparison.Ordinal));

        cloneBody.Should().NotBeNullOrEmpty("Clone must exist -- it is this contract's field list");
        sameBody.Should().NotBeNullOrEmpty("SameAs must exist for this contract to check anything");

        var cloned = new Regex(@"^\s+([A-Za-z]\w*) = ", RegexOptions.Multiline)
            .Matches(cloneBody!)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        cloned.Should().HaveCountGreaterThan(8,
            "a short field list would mean the parse broke and this passed while guarding nothing");

        var missing = cloned
            .Where(name => !Regex.IsMatch(sameBody!, @"other\." + Regex.Escape(name) + @"\b"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "Clone copies these because losing them would corrupt a cloned data table, so SameAs "
            + "ignoring one means a no-op decision that calls a changed chart unchanged. Missing:\n"
            + string.Join("\n", missing));
    }

    [Fact]
    public void PivotOptionsSnapshotIsScalarOnlySoRecordEqualityIsContentEquality()
    {
        var snapshot = typeof(FreeX.Core.Commands.ConfigurePivotTableOptionsCommand)
            .GetNestedType("PivotOptionsSnapshot", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PivotOptionsSnapshot not found");

        var referenceCompared = snapshot
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal))
            .Where(property => !property.PropertyType.IsValueType && property.PropertyType != typeof(string))
            .Select(property => $"{property.Name} ({property.PropertyType.Name})")
            .ToList();

        referenceCompared.Should().BeEmpty(
            "ConfigurePivotTableOptionsCommand decides by re-running Capture and comparing records, "
            + "which is content equality only while every member is a scalar. A reference-typed "
            + "member here would be compared by reference against a fresh capture, so the guard "
            + "would silently never fire again. Give it a stripped comparison first. Found:\n"
            + string.Join("\n", referenceCompared));
    }

    /// <summary>
    /// The snapshot has to be big, or "compare the whole snapshot" would be guarding a fraction of
    /// what the dialog writes. r219 counted twenty-five assignments; this pins the order of magnitude
    /// so a snapshot that quietly shrank would be noticed.
    /// </summary>
    [Fact]
    public void PivotOptionsSnapshotStillCoversTheWholeOptionsDialog()
    {
        var snapshot = typeof(FreeX.Core.Commands.ConfigurePivotTableOptionsCommand)
            .GetNestedType("PivotOptionsSnapshot", BindingFlags.NonPublic)!;

        snapshot.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Count(property => !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal))
            .Should().BeGreaterThan(30,
                "r219 counted a 25-field assignment block; the snapshot must still cover at least that");
    }

    private static string? TypeBody(string source, string declaration)
    {
        var start = source.IndexOf(declaration, StringComparison.Ordinal);
        if (start < 0)
            return null;

        var next = source.IndexOf("\npublic sealed class ", start + 1, StringComparison.Ordinal);
        return next < 0 ? source[start..] : source[start..next];
    }

    private static string? MemberBody(string source, int start)
    {
        if (start < 0)
            return null;

        var open = source.IndexOf('{', start);
        if (open < 0)
            return null;

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(i + 1)];
            }
        }

        return null;
    }
}
