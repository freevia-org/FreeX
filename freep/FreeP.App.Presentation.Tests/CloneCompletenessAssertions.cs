using System.Reflection;
using FluentAssertions;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r308: the shared machinery behind the hand-written-copy guards.
///
/// <para>r307 surveyed the class and found nineteen field-by-field copies across the three apps.
/// Guarding them one round at a time would have cost nineteen rounds and produced nineteen
/// near-identical test files; the property is the same for every one of them, so it is written once
/// here and applied by naming a type.</para>
///
/// <para>Scalar members only, and the limit is deliberate rather than incidental: reference-typed
/// members need per-type construction to vary meaningfully, and inventing a generic way to build
/// them would make the helper guess. A guard that covers the scalars completely and says so is
/// worth more than one that appears to cover everything.</para>
/// </summary>
internal static class CloneCompletenessAssertions
{
    internal static IReadOnlyList<PropertyInfo> ScalarProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => IsScalar(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

    private static bool IsScalar(Type type) =>
        type.IsEnum
        || type == typeof(bool)
        || type == typeof(int)
        || type == typeof(uint)
        || type == typeof(long)
        || type == typeof(double)
        || type == typeof(string)
        || type == typeof(Guid);

    private static object? DistinctValue(PropertyInfo property, object defaults)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var current = property.GetValue(defaults);

        if (type == typeof(bool))
            return !(bool)(current ?? false);
        if (type == typeof(int))
            return (current is int i ? i : 0) + 13;
        if (type == typeof(uint))
            return (current is uint u ? u : 0u) + 13u;
        if (type == typeof(long))
            return (current is long l ? l : 0L) + 13L;
        if (type == typeof(double))
            return (current is double d ? d : 0d) + 2.25;
        if (type == typeof(string))
            return "r308-" + property.Name;
        if (type == typeof(Guid))
            return Guid.NewGuid();
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type).Cast<object>().ToArray();
            return values.FirstOrDefault(value => !Equals(value, current)) ?? values[0];
        }

        return null;
    }

    /// <summary>
    /// Sets every scalar member to a value distinguishable from the default, calls the type's
    /// parameterless <c>Clone()</c>, and reports any member the clone did not carry across.
    /// </summary>
    internal static void AssertCloneCarriesEveryScalar(Type type)
    {
        var clone = type.GetMethod("Clone", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
        clone.Should().NotBeNull($"{type.Name} must expose a parameterless Clone() for this guard");

        var defaults = Activator.CreateInstance(type)!;
        var source = Activator.CreateInstance(type)!;
        var expected = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in ScalarProperties(type))
        {
            if (DistinctValue(property, defaults) is not { } value)
                continue;

            property.SetValue(source, value);
            expected[property.Name] = value;
        }

        expected.Should().NotBeEmpty(
            $"{type.Name} must have scalar members for this guard to check; an empty set means the "
            + "property filter stopped matching and this assertion is vacuous");

        var copy = clone!.Invoke(source, null)!;

        var missed = new List<string>();
        foreach (var (name, want) in expected)
        {
            var got = type.GetProperty(name)!.GetValue(copy);
            if (!Equals(want, got))
                missed.Add($"{name}: source [{want}] clone [{got}]");
        }

        missed.Should().BeEmpty(
            $"{type.Name}.Clone assigns its fields by hand, so a member added to the type and "
            + "forgotten in the copy is dropped silently -- the copy simply lacks it.\n"
            + string.Join("\n", missed));
    }
}
