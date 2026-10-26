using System.Reflection;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeW.App.Presentation.Options;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// r304: bounds the class r303 opened by covering the sibling apps' options the same way.
///
/// <para>FreeX's forty-two-option DTO had two options round-tripped and forty untested, which is why
/// r303 found a real gap there. FreeW's six and FreeP's three are each named repeatedly in their own
/// dedicated test files, so the VALUES are covered -- checked before writing anything, rather than
/// assumed to need the same fix.</para>
///
/// <para>What is not covered is the next option. Those tests name settings individually, so a seventh
/// FreeW option would simply have no test, and a persistence failure on it would be invisible. This
/// closes that by enumerating the properties instead of listing them: it is redundant with the
/// existing per-option tests today, and it is the only thing that will cover the option added
/// tomorrow.</para>
///
/// <para>Written once over both DTOs rather than twice, because the property is identical and the
/// apps share the store -- a third sibling joins by adding one line.</para>
/// </summary>
public sealed class R304_SisterAppOptionsPersistenceRoundTripTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "freex-r304-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private string StorePath(string name)
    {
        Directory.CreateDirectory(_directory);
        return Path.Combine(_directory, name + ".json");
    }

    private static IReadOnlyList<PropertyInfo> WritableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite)
            // An INDEXER is a property to reflection but cannot be read without arguments; one of
            // the nested settings types has one, and reading it threw TargetParameterCountException
            // rather than reporting a persistence problem.
            .Where(property => property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

    private static object? DistinctValue(PropertyInfo property, object defaults)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var current = property.GetValue(defaults);

        if (type == typeof(bool))
            return !(bool)(current ?? false);
        if (type == typeof(int))
            return (current is int i ? i : 0) + 3;
        if (type == typeof(string))
            return "r304-" + property.Name;
        if (type == typeof(List<string>))
            return new List<string> { "r304-" + property.Name };
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type).Cast<object>().ToArray();
            return values.FirstOrDefault(value => !Equals(value, current)) ?? values[0];
        }

        // A nested settings object -- FreeW's AutoCorrect and AutoFormat are whole option PAGES held
        // this way. Varying them means recursing, and it matters: a nested object that fails to
        // serialise resets an entire page of settings at once, not one checkbox.
        if (type.IsClass && type != typeof(string) && type.GetConstructor(Type.EmptyTypes) is not null)
        {
            var nested = Activator.CreateInstance(type)!;
            var nestedDefaults = Activator.CreateInstance(type)!;
            var varied = false;

            foreach (var nestedProperty in WritableProperties(type))
            {
                var nestedValue = DistinctValue(nestedProperty, nestedDefaults);
                if (nestedValue is null)
                    continue;

                nestedProperty.SetValue(nested, nestedValue);
                varied = true;
            }

            return varied ? nested : null;
        }

        return null;
    }

    /// <summary>
    /// Structural comparison for the nested settings objects, which do not override Equals -- without
    /// it every nested page would compare unequal and the test would report loss that is not there.
    /// </summary>
    private static bool ValuesMatch(object? want, object? got)
    {
        if (want is null || got is null)
            return Equals(want, got);
        if (want is List<string> wantList)
            return got is List<string> gotList && wantList.SequenceEqual(gotList);

        var type = want.GetType();
        if (type.IsClass && type != typeof(string) && type.GetConstructor(Type.EmptyTypes) is not null)
        {
            return WritableProperties(type).All(property =>
                ValuesMatch(property.GetValue(want), property.GetValue(got)));
        }

        return Equals(want, got);
    }

    /// <summary>
    /// Generic so the property is stated once. <typeparamref name="T"/> is only required to be a
    /// settings DTO the shared store can persist, which is exactly what both apps use it as.
    /// </summary>
    private void AssertEveryOptionRoundTrips<T>(string name)
        where T : class, new()
    {
        var defaults = new T();
        var options = new T();
        var unsupported = new List<string>();
        var expected = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in WritableProperties(typeof(T)))
        {
            var value = DistinctValue(property, defaults);
            if (value is null)
            {
                unsupported.Add($"{property.Name} ({property.PropertyType.Name})");
                continue;
            }

            property.SetValue(options, value);
            expected[property.Name] = value;
        }

        unsupported.Should().BeEmpty(
            $"{name}: an option whose type this test cannot vary is an UNTESTED option, not a "
            + "passing one. Teach DistinctValue about it rather than letting it through.\n"
            + string.Join("\n", unsupported));

        if (options is INormalizableApplicationOptions normalizable)
        {
            // The store normalises on both save and load, so the expectation is normalised too --
            // otherwise a clamped cap would look identical to a value that failed to persist.
            normalizable.Normalize();
            foreach (var property in WritableProperties(typeof(T)))
            {
                if (expected.ContainsKey(property.Name))
                    expected[property.Name] = property.GetValue(options);
            }
        }

        var path = StorePath(name);
        JsonSettingsStore<T>.SaveToPath(options, path).Should().BeNull($"{name} must save");
        var (reloaded, error) = JsonSettingsStore<T>.LoadFromPath(path);
        error.Should().BeNull($"{name} was just written by this same store");

        var lost = new List<string>();
        foreach (var property in WritableProperties(typeof(T)))
        {
            if (!expected.TryGetValue(property.Name, out var want))
                continue;

            var got = property.GetValue(reloaded);
            if (!ValuesMatch(want, got))
                lost.Add($"{property.Name}: saved [{Describe(want)}] loaded [{Describe(got)}]");
        }

        lost.Should().BeEmpty(
            $"{name}: an option that does not survive save-and-load reverts on every restart, which "
            + "the user experiences as a setting that will not stick.\n" + string.Join("\n", lost));
    }

    [Fact]
    public void EveryFreeWOptionSurvivesSaveAndLoad() =>
        AssertEveryOptionRoundTrips<FreeWOptions>("FreeWOptions");

    [Fact]
    public void EveryFreePOptionSurvivesSaveAndLoad() =>
        AssertEveryOptionRoundTrips<FreePOptions>("FreePOptions");

    /// <summary>
    /// Guards the guard: a collapsed property scan would make both tests above pass while checking
    /// nothing, which is the vacuous-green shape this program has hit before.
    /// </summary>
    [Theory]
    [InlineData(typeof(FreeWOptions), 5)]
    [InlineData(typeof(FreePOptions), 3)]
    public void TheScanFindsEachAppsOptionSurface(Type type, int atLeast) =>
        WritableProperties(type).Should().HaveCountGreaterThanOrEqualTo(atLeast,
            $"{type.Name} carries settings; a collapsed count means the reflection filter stopped "
            + "matching and the round-trip test above is iterating an empty list");

    private static string Describe(object? value) =>
        value is List<string> list ? string.Join("|", list) : value?.ToString() ?? "<null>";
}
