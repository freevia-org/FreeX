using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// A canonical, deep, reflective dump of a <see cref="Workbook"/>'s observable state, used by
/// <see cref="FailureOutcomeMutationAuditTests"/> to detect that a command mutated the model.
///
/// Why reflection rather than the XLSX serializer: the audit's whole purpose is to catch mutations
/// made through arbitrary helper call chains (StructuredTableEditEffects, DuplicateSheetDrawing-
/// Cloner, the planners...), including ones a text scan cannot follow. A fingerprint built from the
/// serializer would only see state the serializer happens to persist, so a mutation to a
/// non-persisted field -- exactly the sort a helper might make -- would be invisible. Walking the
/// object graph sees everything reachable and public.
///
/// The dump is a sorted list of "path = value" lines so a mismatch names the property that changed
/// rather than just failing a hash comparison.
/// </summary>
internal static class WorkbookFingerprint
{
    // Deep enough for Workbook -> Sheet -> Table -> Column -> Filter -> ... ; the model does not
    // nest anywhere near this far, and the cap only exists so an unexpected cycle through a type
    // without reference identity (a struct chain) cannot hang the audit.
    private const int MaxDepth = 24;

    public static string Capture(Workbook workbook)
    {
        var lines = new List<string>();
        Walk("wb", workbook, lines, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
        lines.Sort(StringComparer.Ordinal);
        return string.Join("\n", lines);
    }

    /// <summary>The property paths that differ between two fingerprints, most useful first.</summary>
    public static IReadOnlyList<string> Diff(string before, string after)
    {
        var b = new HashSet<string>(before.Split('\n'), StringComparer.Ordinal);
        var a = new HashSet<string>(after.Split('\n'), StringComparer.Ordinal);
        var changed = new List<string>();
        foreach (var line in a.Where(l => !b.Contains(l)))
            changed.Add("+ " + line);
        foreach (var line in b.Where(l => !a.Contains(l)))
            changed.Add("- " + line);
        changed.Sort(StringComparer.Ordinal);
        return changed;
    }

    private static void Walk(string path, object? value, List<string> lines, HashSet<object> seen, int depth)
    {
        if (value is null)
        {
            lines.Add(path + " = <null>");
            return;
        }

        if (depth > MaxDepth)
        {
            lines.Add(path + " = <depth-capped>");
            return;
        }

        var type = value.GetType();

        if (IsScalar(type))
        {
            lines.Add(path + " = " + Scalar(value));
            return;
        }

        // Reference identity guard: the model shares objects (a style referenced by many cells, a
        // table referenced from its sheet), so without this the walk would re-expand them
        // endlessly on any cycle and redundantly otherwise.
        if (!type.IsValueType && !seen.Add(value))
        {
            lines.Add(path + " = <already-visited>");
            return;
        }

        // Sheet keeps its cells in a PRIVATE dictionary reachable only through methods
        // (EnumerateCells/GetUsedCells), never a property -- so the property/field walk below is
        // blind to cell contents, and an in-place value change (which moves no public property) was
        // detectable only as an incidental ContentVersion bump. Cell edits are the most common
        // mutation in the product and far too important to detect by accident, so read them
        // explicitly. FailureOutcomeMutationAuditTests.Fingerprint_DetectsAnInPlaceCellValueChange
        // keeps this honest.
        if (value is Sheet sheetValue)
        {
            var cells = sheetValue.EnumerateCells()
                .Select(entry => (Key: $"{entry.Address.Row}:{entry.Address.Col}", entry.Cell))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToList();
            lines.Add($"{path}.<cells>.Count = {cells.Count.ToString(CultureInfo.InvariantCulture)}");
            foreach (var (key, cell) in cells)
                Walk($"{path}.<cells>[{key}]", cell, lines, seen, depth + 1);
        }

        switch (value)
        {
            case IDictionary dictionary:
            {
                // Sorted by rendered key so dictionary ordering never registers as a change.
                var entries = new List<(string Key, object? Val)>();
                foreach (DictionaryEntry entry in dictionary)
                    entries.Add((Scalar(entry.Key), entry.Value));
                entries.Sort((x, y) => string.CompareOrdinal(x.Key, y.Key));
                lines.Add(path + ".Count = " + entries.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var (key, val) in entries)
                    Walk($"{path}[{key}]", val, lines, seen, depth + 1);
                return;
            }

            case IEnumerable enumerable and not string:
            {
                // HashSet/ISet has no meaningful order, so sort its rendered members; ordered
                // collections (List) keep their index, because reordering IS a mutation there.
                var isSet = type.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISet<>));
                var items = enumerable.Cast<object?>().ToList();
                lines.Add(path + ".Count = " + items.Count.ToString(CultureInfo.InvariantCulture));
                if (isSet)
                {
                    var rendered = items.Select(i => i is null ? "<null>" : Scalar(i)).ToList();
                    rendered.Sort(StringComparer.Ordinal);
                    for (var i = 0; i < rendered.Count; i++)
                        lines.Add($"{path}{{{i}}} = {rendered[i]}");
                }
                else
                {
                    for (var i = 0; i < items.Count; i++)
                        Walk($"{path}[{i.ToString(CultureInfo.InvariantCulture)}]", items[i], lines, seen, depth + 1);
                }

                return;
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            object? propertyValue;
            try
            {
                propertyValue = property.GetValue(value);
            }
            catch (Exception ex)
            {
                // A computed property that throws on the current state (e.g. one asserting an
                // invariant a half-built test fixture violates) must not abort the fingerprint --
                // record the throw itself, which is just as comparable as a value.
                lines.Add($"{path}.{property.Name} = <threw {ex.GetType().Name}>");
                continue;
            }

            Walk($"{path}.{property.Name}", propertyValue, lines, seen, depth + 1);
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                     .OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            Walk($"{path}.{field.Name}", field.GetValue(value), lines, seen, depth + 1);
        }
    }

    private static bool IsScalar(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type == typeof(string)
        || type == typeof(decimal)
        || type == typeof(DateTime)
        || type == typeof(DateTimeOffset)
        || type == typeof(TimeSpan)
        || type == typeof(Guid)
        || type == typeof(Uri);

    private static string Scalar(object? value) => value switch
    {
        null => "<null>",
        string s => "\"" + s + "\"",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "<null>"
    };
}
