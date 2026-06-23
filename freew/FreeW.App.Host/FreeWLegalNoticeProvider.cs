using System.IO;
using System.Reflection;
using System.Text;

namespace FreeW.App.Host;

internal sealed record FreeWLegalNoticeDocument(string Title, string ResourceName, string Text);

internal static class FreeWLegalNoticeProvider
{
    private static readonly FreeWLegalNoticeResource[] Resources =
    [
        new("Project License", "FreeW.Legal.ProjectLicense.txt"),
        new("Legal Notices", "FreeW.Legal.LegalNotices.md"),
        new("Privacy Notice", "FreeW.Legal.PrivacyNotice.md"),
        new("Third-Party Notices", "FreeW.Legal.ThirdPartyNotices.md"),
        new("Third-Party License Texts", "FreeW.Legal.ThirdPartyLicenses.md")
    ];

    internal static IReadOnlyList<FreeWLegalNoticeResource> ExpectedEmbeddedResources => Resources;

    public static IReadOnlyList<FreeWLegalNoticeDocument> GetDocuments() =>
        GetDocuments(typeof(FreeWLegalNoticeProvider).Assembly);

    internal static IReadOnlyList<FreeWLegalNoticeDocument> GetDocuments(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return Resources
            .Select(resource => new FreeWLegalNoticeDocument(
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

internal sealed record FreeWLegalNoticeResource(string Title, string ResourceName);
