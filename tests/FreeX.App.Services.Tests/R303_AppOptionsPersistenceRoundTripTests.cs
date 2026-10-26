using System.Reflection;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

/// <summary>
/// r303: applies r301/r302's round-trip property to configuration -- the pair whose failure a user
/// experiences as "my settings keep resetting".
///
/// <para><c>AppOptionsStoreTests</c> already covers the store's MECHANICS thoroughly: path
/// resolution, atomic write, unwritable targets, invalid JSON, and schema compatibility. What it
/// round-trips is two options out of forty-two. A property that fails to persist -- one with no
/// setter, an unsupported type, a name the serializer cannot map -- would silently revert on every
/// restart, and nothing here would notice.</para>
///
/// <para>Driven by REFLECTION rather than a hand-written list, so it covers a forty-third property
/// the day it is added. r302 needed a separate completeness guard because its assertions named
/// fields individually; enumerating the properties makes the test complete by construction instead,
/// which is the better shape when the type is this wide.</para>
///
/// <para>Goes through <c>SaveToPath</c>/<c>LoadFromPath</c> -- the real persistence path, including
/// its schema handling -- rather than a bare serializer, so what is proved is what actually happens
/// when the app restarts.</para>
/// </summary>
public sealed class R303_AppOptionsPersistenceRoundTripTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "freex-r303-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private string StorePath()
    {
        Directory.CreateDirectory(_directory);
        return Path.Combine(_directory, "options.json");
    }

    /// <summary>
    /// Transient runtime state that lives on the options object but is deliberately NOT a persisted
    /// setting. Excluded by name, with the reason, rather than silently skipped -- an unexplained
    /// exclusion is how a genuine persistence bug gets filed away as "known".
    /// </summary>
    private static readonly string[] NotSettings =
    [
        // Set by the store itself on every save and load to report the outcome of THAT operation.
        // Persisting it would resurrect yesterday's failure as today's state.
        nameof(AppOptions.LastPersistenceError),
    ];

    private static IReadOnlyList<PropertyInfo> WritableProperties() =>
        typeof(AppOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite)
            .Where(property => !NotSettings.Contains(property.Name, StringComparer.Ordinal))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// A value distinguishable from the default for each supported property type. Returns null for a
    /// type this test does not know how to vary, which the guard below turns into a failure rather
    /// than a silent skip.
    /// </summary>
    private static object? DistinctValue(PropertyInfo property, AppOptions defaults)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var current = property.GetValue(defaults);

        if (type == typeof(bool))
            return !(bool)(current ?? false);
        if (type == typeof(int))
            return (current is int i ? i : 0) + 7;
        if (type == typeof(string))
            return "r303-" + property.Name;
        if (type == typeof(List<string>))
            return new List<string> { "r303-a-" + property.Name, "r303-b" };
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type).Cast<object>().ToArray();
            return values.FirstOrDefault(value => !Equals(value, current)) ?? values[0];
        }

        return null;
    }

    [Fact]
    public void EveryWritableOptionSurvivesSaveAndLoad()
    {
        var defaults = new AppOptions();
        var options = new AppOptions();
        var unsupported = new List<string>();
        var expected = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in WritableProperties())
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
            "this test can only prove a property persists if it knows how to give it a "
            + "non-default value. An unrecognised type is an untested option, not a passing one -- "
            + "teach DistinctValue about it rather than letting it slip through.\n"
            + string.Join("\n", unsupported));

        // Both save and load call Normalize(), so the EXPECTATION is normalised the same way --
        // otherwise a legitimate normalisation (a clamped cap, a de-duplicated list) would be
        // indistinguishable from a value that failed to persist.
        options.Normalize();
        foreach (var property in WritableProperties())
        {
            if (expected.ContainsKey(property.Name))
                expected[property.Name] = property.GetValue(options);
        }

        var path = StorePath();
        AppOptionsStore.SaveToPath(options, path).Should().BeTrue("the save must succeed");
        var reloaded = AppOptionsStore.LoadFromPath(path);

        var lost = new List<string>();
        foreach (var property in WritableProperties())
        {
            if (!expected.TryGetValue(property.Name, out var want))
                continue;

            var got = property.GetValue(reloaded);
            var same = want is List<string> wantList
                ? got is List<string> gotList && wantList.SequenceEqual(gotList)
                : Equals(want, got);

            if (!same)
                lost.Add($"{property.Name}: saved [{Describe(want)}] loaded [{Describe(got)}]");
        }

        lost.Should().BeEmpty(
            "an option that does not survive save-and-load silently reverts every time the app "
            + "restarts, which the user experiences as a setting that will not stick.\n"
            + string.Join("\n", lost));
    }

    /// <summary>
    /// Guards the guard: if the property scan ever collapses, the test above would pass while
    /// checking nothing -- the vacuous-green shape this program has hit before.
    /// </summary>
    [Fact]
    public void TheScanFindsTheOptionSurface() =>
        WritableProperties().Should().HaveCountGreaterThan(30,
            "AppOptions carries dozens of settings; a collapsed count means the reflection filter "
            + "stopped matching and the round-trip test above is checking an empty list");

    /// <summary>
    /// The excluded property is excluded for a REASON, and the reason is testable: a persistence
    /// error is about the last save or load, so carrying it across a restart would show the user a
    /// failure that is no longer happening.
    /// </summary>
    [Fact]
    public void ThePersistenceErrorIsTransientAndDoesNotSurviveARestart()
    {
        var options = new AppOptions();
        options.SetPersistenceError("disk was full at 11:02");

        var path = StorePath();
        AppOptionsStore.SaveToPath(options, path).Should().BeTrue();
        var reloaded = AppOptionsStore.LoadFromPath(path);

        reloaded.LastPersistenceError.Should().BeNull(
            "this field reports the outcome of the CURRENT operation. Persisting it would report a "
            + "stale failure on every subsequent launch, long after the disk had space again");
    }

    private static string Describe(object? value) =>
        value is List<string> list ? string.Join("|", list) : value?.ToString() ?? "<null>";
}
