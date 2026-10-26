using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r258: the coverage contract for <c>SaveTargetComparison</c>.
///
/// <para>Both comparisons use the strip-and-compare shape: replace the record's one collection
/// member with a shared instance, let record equality cover the scalars, compare the collection by
/// content. That is only sound while the collection member stripped is the ONLY member record
/// equality would compare by reference -- a second one added later would be silently ignored, and
/// the comparison would report two different saves as identical, dropping a real edit from the undo
/// stack. This contract computes that set from the types.</para>
///
/// <para>It also pins what the element comparisons rest on. <c>ScenarioCellValue</c> is compared with
/// <c>!=</c>, which is content equality only while it and the types it holds carry no collection of
/// their own; <c>WorksheetCustomViewState</c> is deliberately NOT compared that way, because r248
/// established it needs its own comparer.</para>
/// </summary>
public sealed class R258_SaveTargetComparisonCoverageContractTests
{
    public static TheoryData<string, string> StrippedRecords() => new()
    {
        { nameof(WorkbookCustomView), "Sheets" },
        { nameof(WorkbookScenario), "ChangingCells" },
    };

    [Theory]
    [MemberData(nameof(StrippedRecords))]
    public void TheStrippedMemberIsTheOnlyReferenceComparedOne(string typeName, string strippedMember)
    {
        var type = typeof(WorkbookCustomView).Assembly.GetType($"FreeX.Core.Model.{typeName}")
            ?? throw new InvalidOperationException($"{typeName} not found");

        var referenceCompared = ReferenceComparedMembers(type);

        referenceCompared.Should().BeEquivalentTo([strippedMember],
            $"SaveTargetComparison strips exactly {strippedMember} from {typeName} and lets record "
            + "equality cover the rest, which is correct only while nothing else on the record is "
            + "compared by reference. A new collection member here would be ignored outright.");
    }

    /// <summary>
    /// Whether <c>!=</c> is content equality for ScenarioCellValue is a RECURSIVE question, and the
    /// crude "reference type means reference comparison" rule the other tests use answers it wrongly.
    /// A record is a reference type with value equality, so a record-typed member is compared by
    /// content -- but only as far down as its own members go: record equality on a member that is
    /// itself a record recurses into that record, and stops being content equality the moment it
    /// reaches a collection. <c>ScenarioCellValue.Value</c> is declared as the abstract record
    /// <c>ScalarValue</c>, so this walks the whole reachable graph, every concrete subtype included,
    /// and fails if any of it bottoms out in a collection.
    /// </summary>
    [Fact]
    public void ScenarioCellValueIsSafeToCompareWithRecordEquality()
    {
        var unsafeMembers = new List<string>();
        CollectReferenceComparedReachableMembers(typeof(ScenarioCellValue), typeof(ScenarioCellValue).Name, [], unsafeMembers);

        // The one member the comparison handles itself rather than leaving to record equality. This
        // contract FOUND it: ScalarValue is abstract, and the array lives on a subtype the declared
        // member type never mentions, so no amount of reading the record would have surfaced it.
        unsafeMembers.RemoveAll(member => member.EndsWith("RangeValue.Cells (ScalarValue[,])", StringComparison.Ordinal));

        unsafeMembers.Should().BeEmpty(
            "SaveTargetComparison compares ScenarioCellValue elements with !=, which is content "
            + "equality only while nothing reachable from it is compared by reference. Found:\n"
            + string.Join("\n", unsafeMembers));
    }

    /// <summary>
    /// Walks the value graph. A member is compared by reference when its declared type has no
    /// <c>Equals(object)</c> override -- collections, arrays -- or is an interface, which routes
    /// through <c>EqualityComparer&lt;T&gt;.Default</c> to the runtime type and, for the collection
    /// interfaces these models use, to reference equality. Anything with a real override is followed
    /// into its own members, and an abstract record is followed into its concrete subtypes.
    /// </summary>
    private static void CollectReferenceComparedReachableMembers(
        Type type, string path, HashSet<Type> visited, List<string> unsafeMembers)
    {
        if (!visited.Add(type))
            return;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal))
                continue;

            var memberType = property.PropertyType;
            var memberPath = $"{path}.{property.Name} ({memberType.Name})";

            if (memberType.IsValueType || memberType == typeof(string))
                continue;

            if (memberType.IsInterface || !HasValueEquality(memberType))
            {
                unsafeMembers.Add(memberPath);
                continue;
            }

            CollectReferenceComparedReachableMembers(memberType, memberPath, visited, unsafeMembers);

            if (memberType.IsAbstract)
            {
                foreach (var subtype in memberType.Assembly.GetTypes().Where(memberType.IsAssignableFrom))
                {
                    if (subtype != memberType)
                        CollectReferenceComparedReachableMembers(subtype, $"{memberPath}:{subtype.Name}", visited, unsafeMembers);
                }
            }
        }
    }

    private static bool HasValueEquality(Type type) =>
        type.GetMethod(nameof(Equals), BindingFlags.Public | BindingFlags.Instance, [typeof(object)])
            ?.DeclaringType != typeof(object);

    /// <summary>
    /// The one deliberate exception, kept honest: the custom view's element type is NOT compared
    /// with record equality, because r248 found it carries members that need their own comparison.
    /// </summary>
    [Fact]
    public void CustomViewSheetsAreComparedThroughTheR248ComparerNotRecordEquality()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Commands"),
            "SaveTargetComparison.cs"));

        Regex.IsMatch(source, @"WorksheetCustomViewStateComparer\.Same\(").Should().BeTrue(
            "comparing the Sheets elements with == would reintroduce exactly the reference-comparison "
            + "bug this class exists to fix, one level down");
    }

    private static List<string> ReferenceComparedMembers(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite)
            .Where(property => !property.PropertyType.IsValueType && property.PropertyType != typeof(string))
            .Where(property => !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal))
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// And the exemption is only legitimate because the comparison really does handle it: this fails
    /// if <c>SameRange</c> is removed, so the exemption cannot outlive the code that earns it.
    /// </summary>
    [Fact]
    public void TheRangeValueArrayIsComparedByContentInTheComparison()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Commands"),
            "SaveTargetComparison.cs"));

        Regex.IsMatch(source, @"private static bool SameRange\(RangeValue").Should().BeTrue(
            "the recursive-reachability test exempts RangeValue.Cells on the grounds that this "
            + "comparison compares it element by element; without SameRange that exemption would "
            + "silently hide a reference comparison");
    }
}
