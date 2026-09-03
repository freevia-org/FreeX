using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r253: the coverage contract for <see cref="WorksheetAutoFilterColumnComparison"/>.
///
/// <para>The comparison deliberately leans on compiler-generated record equality for the scalar
/// members, so that adding a scalar needs no edit there. That only stays sound while every member
/// record equality compares by REFERENCE is stripped out and compared by content instead. This
/// contract derives that set from the types themselves -- a member is reference-compared exactly
/// when its type is a reference type other than <c>string</c> -- and fails if one is not handled.
/// </para>
///
/// <para>The failure it exists to prevent is a silent one: an unstripped collection member makes
/// <c>SameAs</c> return false for two identical filters, which loses no data, and an unhandled
/// nested-model member makes it return TRUE for two different ones, which reports a real edit as a
/// no-op and drops it from the undo stack.</para>
/// </summary>
public sealed class R253_AutoFilterColumnComparisonCoverageContractTests
{
    /// <summary>
    /// The nested filter models each carry scalars plus one <c>NativeAttributes</c> dictionary, and
    /// the comparison handles that shape with <c>with { NativeAttributes = null }</c> plus a map
    /// comparison. A different reference-typed member added to one of them would be compared by
    /// reference and silently ignored.
    /// </summary>
    private static readonly Type[] NestedModels =
    [
        typeof(WorksheetAutoFilterTop10Model),
        typeof(WorksheetAutoFilterDynamicFilterModel),
        typeof(WorksheetAutoFilterColorFilterModel),
        typeof(WorksheetAutoFilterIconFilterModel),
        typeof(WorksheetAutoFilterCustomFilterModel),
        typeof(WorksheetAutoFilterDateGroupItemModel),
    ];

    [Fact]
    public void StripRemovesEveryMemberRecordEqualityWouldCompareByReference()
    {
        var source = ComparisonSource();
        var stripBody = MemberBody(source, source.IndexOf("private static WorksheetAutoFilterColumnModel Strip(", StringComparison.Ordinal));
        stripBody.Should().NotBeNullOrEmpty("Strip must exist for this contract to check anything");

        var referenceCompared = ReferenceComparedMembers(typeof(WorksheetAutoFilterColumnModel));

        referenceCompared.Should().HaveCountGreaterThan(8,
            "a short list would mean the classification broke and this contract passed while guarding nothing");

        var missing = referenceCompared
            .Where(name => !Regex.IsMatch(stripBody!, @"\b" + Regex.Escape(name) + @" = "))
            .ToList();

        missing.Should().BeEmpty(
            "record equality compares these by reference, so leaving one in the stripped pair makes "
            + "SameAs answer 'changed' for two filters built the same way. Missing from Strip:\n"
            + string.Join("\n", missing));
    }

    [Fact]
    public void SameAsComparesEveryStrippedMemberByContent()
    {
        var source = ComparisonSource();
        var sameBody = MemberBody(source, source.IndexOf("public static bool SameAs(", StringComparison.Ordinal));
        sameBody.Should().NotBeNullOrEmpty("SameAs must exist for this contract to check anything");

        var missing = ReferenceComparedMembers(typeof(WorksheetAutoFilterColumnModel))
            .Where(name => !Regex.IsMatch(sameBody!, @"\bleft\." + Regex.Escape(name) + @"\b"))
            .ToList();

        missing.Should().BeEmpty(
            "Strip removes these from the record-equality comparison, so a member stripped but never "
            + "compared afterwards is ignored outright -- SameAs would call two different filters the "
            + "same and the command would drop a real edit. Missing from SameAs:\n"
            + string.Join("\n", missing));
    }

    [Fact]
    public void NestedFilterModelsCarryNoReferenceMemberBeyondNativeAttributes()
    {
        foreach (var nested in NestedModels)
        {
            var unexpected = ReferenceComparedMembers(nested)
                .Where(name => !string.Equals(name, "NativeAttributes", StringComparison.Ordinal))
                .ToList();

            unexpected.Should().BeEmpty(
                $"{nested.Name} is compared by stripping NativeAttributes and letting record equality "
                + "cover the rest, which is only correct while NativeAttributes is its only "
                + "reference-compared member. Extend WorksheetAutoFilterColumnComparison's helper for "
                + $"{nested.Name} before adding:\n"
                + string.Join("\n", unexpected));
        }
    }

    /// <summary>
    /// Members whose declared type <c>EqualityComparer&lt;T&gt;.Default</c> compares by reference --
    /// every reference type except <c>string</c>, which has value equality.
    /// </summary>
    private static List<string> ReferenceComparedMembers(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => !property.PropertyType.IsValueType && property.PropertyType != typeof(string))
            .Where(property => !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal))
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    private static string ComparisonSource() => File.ReadAllText(Path.Combine(
        TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Model"),
        "WorksheetAutoFilterColumnComparison.cs"));

    /// <summary>
    /// The text of the member starting at <paramref name="start"/>, brace-matched from its own
    /// opening brace so it cannot run on into the next member. Handles both a braced body and an
    /// expression body terminated by a semicolon.
    /// </summary>
    private static string? MemberBody(string source, int start)
    {
        if (start < 0)
            return null;

        var open = source.IndexOf('{', start);
        var semicolon = source.IndexOf(';', start);
        if (open < 0)
            return null;
        if (semicolon >= 0 && semicolon < open)
            return source[start..semicolon];

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
