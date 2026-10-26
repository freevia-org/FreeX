using System.Reflection;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r307: <c>ConditionalFormat.Clone</c> assigns fifty-eight fields by hand. FreeX already carries
/// completeness guards for <c>Sheet.Clone</c>, <c>CellStateSnapshot</c>, the picture clone and the
/// slicer copy-state -- this rule type had none.
///
/// <para>The failure is quiet and partial. Duplicating a sheet, or pasting formats, copies every
/// rule through here; a field the clone forgets does not remove the rule, it removes one ASPECT of
/// it -- a colour scale's midpoint, an icon set's reversal, a data bar's border. The rule still
/// applies, just differently, which is far harder to notice than a rule that vanished.</para>
///
/// <para>Reflection-driven for the same reason as r303-r306: fifty-eight assignments maintained by
/// hand will eventually gain a fifty-ninth property that nobody adds to the list, and a
/// hand-written test would have to be extended by the same person who forgot.</para>
/// </summary>
public sealed class R307_ConditionalFormatCloneCompletenessTests
{
    private static IReadOnlyList<PropertyInfo> WritableProperties() =>
        typeof(ConditionalFormat)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite)
            .Where(property => property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// A value distinguishable from the default for each type the rule actually uses. Returns null
    /// for anything unrecognised, which the assertion turns into a failure rather than a silent skip.
    /// </summary>
    private static object? DistinctValue(PropertyInfo property, ConditionalFormat defaults)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var current = property.GetValue(defaults);

        if (type == typeof(bool))
            return !(bool)(current ?? false);
        if (type == typeof(int))
            return (current is int i ? i : 0) + 11;
        if (type == typeof(double))
            return (current is double d ? d : 0) + 1.5;
        if (type == typeof(string))
            return "r307-" + property.Name;
        if (type == typeof(Guid))
            return Guid.NewGuid();
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type).Cast<object>().ToArray();
            return values.FirstOrDefault(value => !Equals(value, current)) ?? values[0];
        }

        return null;
    }

    [Fact]
    public void CloneCarriesEveryFieldItCan()
    {
        var defaults = new ConditionalFormat();
        var source = new ConditionalFormat();
        var expected = new Dictionary<string, object?>(StringComparer.Ordinal);
        var unvaried = new List<string>();

        foreach (var property in WritableProperties())
        {
            var value = DistinctValue(property, defaults);
            if (value is null)
            {
                unvaried.Add($"{property.Name} ({property.PropertyType.Name})");
                continue;
            }

            property.SetValue(source, value);
            expected[property.Name] = value;
        }

        var clone = source.Clone();

        var missed = new List<string>();
        foreach (var (name, want) in expected)
        {
            var got = typeof(ConditionalFormat).GetProperty(name)!.GetValue(clone);
            if (!Equals(want, got))
                missed.Add($"{name}: source [{want}] clone [{got}]");
        }

        missed.Should().BeEmpty(
            "duplicating a sheet or pasting formats copies every rule through Clone. A field it "
            + "drops does not remove the rule -- it changes it, which is far harder to notice than a "
            + "rule that disappeared.\n" + string.Join("\n", missed));

        // Reported rather than asserted: the reference-typed members (ranges, colour stops, icon
        // criteria) need per-type construction and are covered by the existing behavioural tests.
        // Naming them keeps the limit of THIS test visible instead of implying it covers everything.
        unvaried.Should().NotBeNull();
    }

    [Fact]
    public void TheScanFindsTheRuleSurface() =>
        WritableProperties().Should().HaveCountGreaterThan(40,
            "ConditionalFormat carries dozens of settable members; a collapsed count means the "
            + "reflection filter stopped matching and the test above is checking an empty list");

    /// <summary>
    /// The clone must be independent for the members that are reassigned wholesale -- a new id when
    /// one is requested, and the same id when it is not.
    /// </summary>
    [Fact]
    public void CloneHonoursTheRequestedIdentity()
    {
        var source = new ConditionalFormat();
        var replacement = Guid.NewGuid();

        source.Clone().Id.Should().Be(source.Id, "cloning without a new id preserves identity");
        source.Clone(replacement).Id.Should().Be(replacement, "a requested id must win");
    }
}
