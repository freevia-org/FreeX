using System.IO;
using System.Reflection;
using System.Text;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// Loads embedded legal-notice resources for any app that embeds them as manifest resources.
/// Each app supplies its own assembly and ordered list of (Title, ResourceName) pairs;
/// the embedded .txt/.md files stay in each app's own assembly.
/// </summary>
public static class SharedLegalNoticeLoader
{
    /// <summary>
    /// Reads all supplied resources from <paramref name="assembly"/> and returns ordered
    /// (Title, Text) tuples ready for display.
    /// </summary>
    public static IReadOnlyList<(string Title, string Text)> GetDocuments(
        Assembly assembly,
        IReadOnlyList<(string Title, string ResourceName)> resources)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(resources);

        return resources
            .Select(r => (r.Title, ReadResourceText(assembly, r.ResourceName)))
            .ToList();
    }

    private static string ReadResourceText(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded legal notice resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
