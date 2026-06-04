using System.IO;
using System.Reflection;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

internal static class DialogSourceTestSupport
{
    public static string ReadHostSources(params string[] fileNames) =>
        ReadHostSourcesWithSeparator(Environment.NewLine, fileNames);

    public static string ReadHostSourcesWithSeparator(string separator, params string[] fileNames) =>
        string.Join(separator, fileNames.Select(ReadHostSource));

    public static string ReadClassSource(string fileName, string startMarker, string endMarker)
    {
        var source = ReadHostSource(fileName);
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

    public static T GetPrivateField<T>(object instance, string name)
        where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(instance).Should().BeOfType<T>().Subject;
    }

    private static string ReadHostSource(string fileName) =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", fileName));
}
