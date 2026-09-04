using System.Reflection;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

/// <summary>
/// r305: <c>AppOptions.CopyFrom</c> assigns forty-odd fields BY HAND, and nothing checked that the
/// list is complete.
///
/// <para>It exists so shells and sibling windows can keep one live options reference while a reload
/// is adopted -- "replaces this runtime snapshot with another while preserving object identity". A
/// field missing from that list is therefore not a crash: the reloaded value is read from disk
/// correctly and then simply not copied across, so the window keeps showing the OLD setting until
/// it is restarted. r303 proved the same options persist; this is the other half of the same
/// journey.</para>
///
/// <para>The same shape as the snapshot-completeness contracts that drove this program's no-op
/// ledger to zero: the risk is not that today's list is wrong, it is that the next property added
/// to the type will not be added to the list, and nothing would say so.</para>
/// </summary>
public sealed class R305_AppOptionsCopyFromCompletenessTests
{
    /// <summary>
    /// Transient runtime state rather than a setting; it has a private setter and is deliberately
    /// not part of the snapshot (see r303).
    /// </summary>
    private static readonly string[] NotSettings = [nameof(AppOptions.LastPersistenceError)];

    private static IReadOnlyList<PropertyInfo> SettingProperties() =>
        typeof(AppOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => !NotSettings.Contains(property.Name, StringComparer.Ordinal))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

    private static object? DistinctValue(PropertyInfo property, AppOptions defaults)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var current = property.GetValue(defaults);

        if (type == typeof(bool))
            return !(bool)(current ?? false);
        if (type == typeof(int))
            return (current is int i ? i : 0) + 5;
        if (type == typeof(string))
            return "r305-" + property.Name;
        if (type == typeof(List<string>))
            return new List<string> { "r305-" + property.Name };
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type).Cast<object>().ToArray();
            return values.FirstOrDefault(value => !Equals(value, current)) ?? values[0];
        }

        return null;
    }

    [Fact]
    public void CopyFromCarriesEverySetting()
    {
        var defaults = new AppOptions();
        var source = new AppOptions();
        var unsupported = new List<string>();
        var expected = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in SettingProperties())
        {
            var value = DistinctValue(property, defaults);
            if (value is null)
            {
                unsupported.Add($"{property.Name} ({property.PropertyType.Name})");
                continue;
            }

            property.SetValue(source, value);
            expected[property.Name] = value;
        }

        unsupported.Should().BeEmpty(
            "a property this test cannot vary is one it cannot check; teach DistinctValue about it "
            + "rather than letting it pass silently.\n" + string.Join("\n", unsupported));

        // CopyFrom ends with Normalize(), which legitimately rewrites DefaultFormat, the font
        // settings and both list settings. The expectation is normalised the same way, so what is
        // measured is whether CopyFrom CARRIES each field -- not whether an arbitrary test value
        // survives validation. The first draft skipped this and reported DefaultFormat as lost when
        // normalization had simply rejected "r305-DefaultFormat" as an extension, which it is.
        source.Normalize();
        foreach (var property in SettingProperties())
        {
            if (expected.ContainsKey(property.Name))
                expected[property.Name] = property.GetValue(source);
        }

        var target = new AppOptions();
        target.CopyFrom(source);

        var missed = new List<string>();
        foreach (var property in SettingProperties())
        {
            if (!expected.TryGetValue(property.Name, out var want))
                continue;

            var got = property.GetValue(target);
            var same = want is List<string> wantList
                ? got is List<string> gotList && wantList.SequenceEqual(gotList)
                : Equals(want, got);

            if (!same)
                missed.Add($"{property.Name}: source [{Describe(want)}] target [{Describe(got)}]");
        }

        missed.Should().BeEmpty(
            "CopyFrom adopts a reloaded settings file into the live options object that windows "
            + "already hold. A setting it does not copy is read from disk correctly and then "
            + "dropped, so the window keeps showing the old value until the app restarts.\n"
            + string.Join("\n", missed));
    }

    /// <summary>
    /// A list copied by REFERENCE would let a later edit to the source mutate the target -- the
    /// aliasing bug that a field-by-field copy invites and that a value comparison cannot see.
    /// </summary>
    [Fact]
    public void CopyFromDoesNotAliasCollectionSettings()
    {
        var source = new AppOptions();
        source.SpellCheckCustomDictionaryWords = ["freex"];
        source.Normalize();

        var target = new AppOptions();
        target.CopyFrom(source);

        // Snapshot AFTER the copy and BEFORE mutating the source: comparing against a literal would
        // fail whenever normalization reorders or supplements a list, which is not what this test is
        // about. What matters is only that a later edit to the source cannot reach the target.
        var copied = target.SpellCheckCustomDictionaryWords.ToList();
        source.SpellCheckCustomDictionaryWords.Add("workbook");

        target.SpellCheckCustomDictionaryWords.Should().Equal(copied,
            "the copy must own its list. Sharing the instance means an edit in one window's options "
            + "silently rewrites another's, and CopyFrom exists precisely so several windows can "
            + "hold the same options object");
    }

    /// <summary>
    /// The same aliasing question for the quick-access toolbar, kept separate because its
    /// normalization is the one most likely to hand back a shared instance.
    /// </summary>
    [Fact]
    public void CopyFromDoesNotAliasTheQuickAccessToolbarList()
    {
        var source = new AppOptions();
        source.Normalize();

        var target = new AppOptions();
        target.CopyFrom(source);

        var copied = target.QuickAccessToolbarCommands.ToList();
        source.QuickAccessToolbarCommands.Add("r305-added-later");

        target.QuickAccessToolbarCommands.Should().Equal(copied,
            "adding a command in one window must not appear in another window's toolbar");
    }

    [Fact]
    public void TheScanFindsTheSettingSurface() =>
        SettingProperties().Should().HaveCountGreaterThan(30,
            "a collapsed count means the reflection filter stopped matching and the completeness "
            + "test above is iterating an empty list");

    private static string Describe(object? value) =>
        value is List<string> list ? string.Join("|", list) : value?.ToString() ?? "<null>";
}
