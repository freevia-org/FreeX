using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r256: the coverage contract for the pivot snapshots' <c>Matches</c>, on the r249 pattern -- the
/// type's own <c>Capture</c> is the field list, because Capture has to be complete or undo would
/// lose pivot state.
///
/// <para>A member Capture records but Matches ignores is a thing the command writes and the no-op
/// decision cannot see, which reports "nothing changed" for an edit that happened -- and, because
/// these commands push their undo entry on that decision, drops the edit from the undo stack.</para>
///
/// <para>The second test pins the assumption <c>SameScalarRecords</c> rests on: that the element
/// records other than <c>PivotFieldModel</c> carry no collection member, so record equality IS
/// content equality for them. That assumption is cheap to hold and silent to break.</para>
/// </summary>
public sealed class R256_PivotSnapshotComparisonCoverageContractTests
{
    public static TheoryData<string> SnapshotRecords() =>
    [
        "PivotFilterStateSnapshot",
        "PivotLayoutStateSnapshot",
        "PivotViewStateSnapshot",
        "PivotCalculatedItemsStateSnapshot",
        "PivotFieldLayoutStateSnapshot",
    ];

    /// <summary>
    /// Element types compared with <c>EqualityComparer&lt;T&gt;.Default</c>. PivotFieldModel is
    /// deliberately absent: it carries SelectedItems, and PivotSnapshotComparison.SameField strips
    /// and compares that member itself.
    /// </summary>
    public static TheoryData<Type> ScalarElementRecords() =>
    [
        typeof(PivotDataFieldModel),
        typeof(PivotLabelFilterModel),
        typeof(PivotValueFilterModel),
        typeof(PivotSortModel),
        typeof(PivotCalculatedFieldModel),
        typeof(PivotCalculatedItemModel),
    ];

    [Theory]
    [MemberData(nameof(SnapshotRecords))]
    public void MatchesComparesEveryMemberCaptureRecords(string recordName)
    {
        var source = SnapshotSource();
        var recordBody = RecordBody(source, recordName);
        recordBody.Should().NotBeNullOrEmpty($"{recordName} must exist for this contract to check it");

        var captureBody = MemberBody(recordBody!, recordBody!.IndexOf($"internal static {recordName} Capture(", StringComparison.Ordinal));
        var matchesBody = MemberBody(recordBody, recordBody.IndexOf("public bool Matches(", StringComparison.Ordinal));

        captureBody.Should().NotBeNullOrEmpty($"{recordName}.Capture must exist -- it is this contract's field list");
        matchesBody.Should().NotBeNullOrEmpty($"{recordName}.Matches must exist for this contract to check anything");

        var captured = new Regex(@"pivotTable\.([A-Za-z]\w*)", RegexOptions.Compiled)
            .Matches(captureBody!)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        captured.Should().HaveCountGreaterThan(2,
            "a tiny field list would make this pass while guarding nothing");

        var missing = captured
            .Where(name => !Regex.IsMatch(matchesBody!, @"pivotTable\." + Regex.Escape(name) + @"\b"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            $"{recordName}.Capture records these because undo would otherwise lose them, so Matches "
            + "ignoring one means a no-op decision that calls a changed pivot unchanged. Missing:\n"
            + string.Join("\n", missing));
    }

    [Theory]
    [MemberData(nameof(ScalarElementRecords))]
    public void ScalarComparedElementRecordsCarryNoCollectionMember(Type element)
    {
        var collections = element.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite || property.GetMethod?.IsPublic == true)
            .Where(property => !property.PropertyType.IsValueType && property.PropertyType != typeof(string))
            .Where(property => !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal))
            .Select(property => $"{property.Name} ({property.PropertyType.Name})")
            .ToList();

        collections.Should().BeEmpty(
            $"{element.Name} is compared with EqualityComparer<T>.Default, which is content equality "
            + "only while every member is a scalar. A reference-typed member added here would be "
            + "compared by REFERENCE, and against a freshly built list that is always 'different' -- "
            + "so give it a stripped comparison in PivotSnapshotComparison, the way PivotFieldModel "
            + "has one for SelectedItems. Found:\n"
            + string.Join("\n", collections));
    }

    private static string SnapshotSource() => File.ReadAllText(Path.Combine(
        TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Commands"),
        "PivotTableCommandStateSnapshots.cs"));

    /// <summary>One record's text, from its declaration to the start of the next top-level one.</summary>
    private static string? RecordBody(string source, string recordName)
    {
        var start = source.IndexOf($"internal sealed record {recordName}(", StringComparison.Ordinal);
        if (start < 0)
            return null;

        var next = source.IndexOf("\ninternal sealed record ", start + 1, StringComparison.Ordinal);
        return next < 0 ? source[start..] : source[start..next];
    }

    private static string? MemberBody(string source, int start)
    {
        if (start < 0)
            return null;

        var open = source.IndexOf('{', start);
        var semicolon = source.IndexOf(';', start);
        if (semicolon >= 0 && (open < 0 || semicolon < open))
            return source[start..semicolon];
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
