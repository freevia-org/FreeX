using System.Reflection;
using System.Text;

namespace FreeX.App.Services;

public static class LegalNoticeProvider
{
    private static readonly LegalNoticeResource[] Resources =
    [
        new("Project License", "FreeX.Legal.ProjectLicense.txt"),
        new("Legal Notices", "FreeX.Legal.LegalNotices.md"),
        new("Privacy Notice", "FreeX.Legal.PrivacyNotice.md"),
        new("Third-Party Notices", "FreeX.Legal.ThirdPartyNotices.md"),
        new("Third-Party License Texts", "FreeX.Legal.ThirdPartyLicenses.md")
    ];

    internal static IReadOnlyList<LegalNoticeResource> ExpectedEmbeddedResources => Resources;

    public static IReadOnlyList<LegalNoticeDocument> GetDocuments() =>
        GetDocuments(typeof(LegalNoticeProvider).Assembly);

    internal static IReadOnlyList<LegalNoticeDocument> GetDocuments(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return Resources
            .Select(resource => new LegalNoticeDocument(
                resource.Title,
                resource.ResourceName,
                ReadResourceText(assembly, resource.ResourceName)))
            .ToList();
    }

    private static string ReadResourceText(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded legal notice resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}

internal sealed record LegalNoticeResource(string Title, string ResourceName);
