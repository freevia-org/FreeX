using System.Reflection;
using System.Text;

namespace Free.Shared.Shell;

/// <summary>Loads an ordered app-owned set of embedded legal-notice documents.</summary>
public static class EmbeddedLegalNoticeLoader
{
    public static IReadOnlyList<(string Title, string Text)> GetDocuments(
        Assembly assembly,
        IReadOnlyList<(string Title, string ResourceName)> resources)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(resources);

        return resources
            .Select(resource => (resource.Title, ReadResourceText(assembly, resource.ResourceName)))
            .ToList();
    }

    private static string ReadResourceText(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded legal notice resource '{resourceName}' was not found.");
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
