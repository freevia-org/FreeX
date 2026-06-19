using System.Reflection;
using FluentAssertions;

/// <summary>
/// App-neutral mechanics behind the source-hygiene tests: read a set of source files
/// through a caller-supplied reader, extract a region of source between markers, and
/// reach private members by reflection. The *which app / which file* concern stays with
/// the caller (pass a reader and the file names); the read/extract/reflect mechanics are
/// shared so a sister app does not reinvent them.
/// </summary>
/// <remarks>
/// WPF-free on purpose: this file is auto-linked into every <c>*.Tests</c> project,
/// including portable (<c>net10.0</c>) ones. WPF-coupled helpers (routed-event handler
/// invocation, button clicks) stay in each app's own test-support shim.
/// </remarks>
internal static class SourceTextTestSupport
{
    /// <summary>Reads each file via <paramref name="readSource"/> and joins with a newline.</summary>
    public static string ReadSources(Func<string, string> readSource, params string[] fileNames) =>
        ReadSources(readSource, Environment.NewLine, fileNames);

    /// <summary>Reads each file via <paramref name="readSource"/> and joins with <paramref name="separator"/>.</summary>
    public static string ReadSources(Func<string, string> readSource, string separator, params string[] fileNames)
    {
        ArgumentNullException.ThrowIfNull(readSource);
        return string.Join(separator, fileNames.Select(readSource));
    }

    /// <summary>
    /// Returns the slice of <paramref name="source"/> from the first occurrence of
    /// <paramref name="startMarker"/> to <paramref name="endMarker"/> (or end of file when the
    /// end marker is empty or not found). The engine behind "extract a C# method/region body".
    /// </summary>
    public static string ExtractBetweenMarkers(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);

        var end = string.IsNullOrEmpty(endMarker)
            ? source.Length
            : source.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0)
            end = source.Length;

        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    /// <summary>Reads an instance private field (walking the base-type chain) and asserts its runtime type.</summary>
    public static T GetPrivateField<T>(object instance, string name)
        where T : class
    {
        var type = instance.GetType();
        FieldInfo? field = null;
        while (type is not null && field is null)
        {
            field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            type = type.BaseType;
        }

        field.Should().NotBeNull();
        return field!.GetValue(instance).Should().BeOfType<T>().Subject;
    }

    /// <summary>Finds an instance private method by name, walking the base-type chain.</summary>
    public static MethodInfo GetPrivateMethod(object instance, string methodName)
    {
        var type = instance.GetType();
        MethodInfo? method = null;
        while (type is not null && method is null)
        {
            method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            type = type.BaseType;
        }

        method.Should().NotBeNull();
        return method!;
    }
}
