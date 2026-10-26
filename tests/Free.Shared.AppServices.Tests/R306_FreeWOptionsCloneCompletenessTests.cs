using System.Reflection;
using FluentAssertions;
using FreeW.App.Presentation.Options;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// r306: bounds r305's class in FreeW. <c>FreeWOptions.Clone()</c> is the sibling of FreeX's
/// <c>CopyFrom</c> -- a hand-written field-by-field copy -- and no test called it at all.
///
/// <para>Its purpose makes the failure specific: it captures the Options dialog's OPEN-TIME state
/// for the reload-before-write merge. A field the clone forgets is a field the merge believes was
/// never edited, so the user's change to it is discarded when the dialog is accepted -- and nothing
/// errors.</para>
///
/// <para>The nested pages are shared by REFERENCE on purpose, and the type says why: "Production
/// code never mutates AutoFormat or AutoCorrect in place -- an edit always assigns a freshly built
/// replacement object". So unlike r305's FreeX lists, independence is NOT the property here.
/// Asserting it would have contradicted a documented design decision; the sharing is pinned
/// instead, so changing to a deep copy becomes a deliberate act rather than a silent one.</para>
/// </summary>
public sealed class R306_FreeWOptionsCloneCompletenessTests
{
    private static IReadOnlyList<PropertyInfo> SettingProperties() =>
        typeof(FreeWOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite)
            .Where(property => property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

    private static object? DistinctValue(PropertyInfo property, FreeWOptions defaults)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var current = property.GetValue(defaults);

        if (type == typeof(bool))
            return !(bool)(current ?? false);
        if (type == typeof(int))
            // Stays inside the valid cap range, so Normalize cannot legitimately rewrite it and be
            // mistaken for a field the clone dropped -- the trap r303 and r305 both hit.
            return FreeWOptions.MinRecentFilesCap + 1;
        if (type == typeof(string))
            return null;      // varied explicitly below; the valid values are constrained
        if (type.IsClass && type != typeof(string) && type.GetConstructor(Type.EmptyTypes) is not null)
        {
            // A fresh DEFAULT instance is not enough: AutoFormatOptions has value equality, so an
            // unmodified copy compares equal to the default and the guard below correctly reported
            // it as "not actually varied". Flip a flag inside it so the value genuinely differs.
            var nested = Activator.CreateInstance(type)!;
            foreach (var nestedProperty in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!nestedProperty.CanWrite
                    || nestedProperty.GetIndexParameters().Length != 0
                    || nestedProperty.PropertyType != typeof(bool))
                {
                    continue;
                }

                nestedProperty.SetValue(nested, !(bool)nestedProperty.GetValue(nested)!);
                return nested;
            }

            return nested;
        }

        return null;
    }

    [Fact]
    public void CloneCarriesEverySetting()
    {
        var defaults = new FreeWOptions();
        var source = new FreeWOptions
        {
            // The constrained values are set by hand so normalization cannot reject them; the rest
            // are varied by reflection so a new property is covered the day it is added.
            DefaultSaveFormat = FreeWOptions.DocxDefaultFormat,
            UiLanguage = "fr-FR",
        };

        var expected = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in SettingProperties())
        {
            if (DistinctValue(property, defaults) is { } value)
                property.SetValue(source, value);
        }

        source.Normalize();
        foreach (var property in SettingProperties())
            expected[property.Name] = property.GetValue(source);

        var clone = source.Clone();

        var missed = new List<string>();
        foreach (var property in SettingProperties())
        {
            var got = property.GetValue(clone);
            if (!Equals(expected[property.Name], got))
                missed.Add($"{property.Name}: source [{expected[property.Name]}] clone [{got}]");
        }

        missed.Should().BeEmpty(
            "Clone captures the Options dialog's open-time state for the reload-before-write merge. "
            + "A field it drops is one the merge treats as never edited, so the user's change to it "
            + "is discarded when they press OK -- silently.\n" + string.Join("\n", missed));
    }

    /// <summary>
    /// Every setting must be varied from its default, or the comparison above could pass on values
    /// that were never actually changed -- a test that proves nothing while looking thorough.
    /// </summary>
    [Fact]
    public void TheTestActuallyVariesEverySetting()
    {
        var defaults = new FreeWOptions();
        var source = new FreeWOptions { DefaultSaveFormat = FreeWOptions.DocxDefaultFormat, UiLanguage = "fr-FR" };
        foreach (var property in SettingProperties())
        {
            if (DistinctValue(property, defaults) is { } value)
                property.SetValue(source, value);
        }

        source.Normalize();

        var unchanged = SettingProperties()
            .Where(property => Equals(property.GetValue(defaults), property.GetValue(source)))
            .Select(property => property.Name)
            .Where(name => !string.Equals(name, nameof(FreeWOptions.DefaultSaveFormat), StringComparison.Ordinal))
            .ToList();

        unchanged.Should().BeEmpty(
            "a setting left at its default is one the clone test cannot distinguish from a dropped "
            + "field. DefaultSaveFormat is excluded because FreeW ships exactly one save format, so "
            + "there is no second valid value to move it to.\n" + string.Join("\n", unchanged));
    }

    /// <summary>
    /// The documented design: the nested option pages are shared, not deep-copied, because an edit
    /// replaces the whole object rather than mutating it. Pinned so a change to deep copying is a
    /// decision someone makes, not one that happens.
    /// </summary>
    [Fact]
    public void CloneSharesTheNestedPagesByDesign()
    {
        var source = new FreeWOptions();
        var clone = source.Clone();

        clone.AutoFormat.Should().BeSameAs(source.AutoFormat,
            "the type documents that production never mutates these in place -- an edit assigns a "
            + "fresh object -- so sharing the reference is deliberate rather than an oversight");
        clone.AutoCorrect.Should().BeSameAs(source.AutoCorrect);
    }
}
