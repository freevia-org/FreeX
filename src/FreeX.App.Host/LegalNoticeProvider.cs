using System.IO;
using System.Reflection;
using System.Text;

namespace FreeX.App.Host;

public static class LegalNoticeProvider
{
    private static readonly (string Title, string ResourceName)[] Resources =
    [
        ("Project License", "FreeX.Legal.ProjectLicense.txt"),
        ("Legal Notices", "FreeX.Legal.LegalNotices.md"),
        ("Privacy Notice", "FreeX.Legal.PrivacyNotice.md"),
        ("Third-Party Notices", "FreeX.Legal.ThirdPartyNotices.md"),
        ("Third-Party License Texts", "FreeX.Legal.ThirdPartyLicenses.md")
    ];

    public static IReadOnlyList<LegalNoticeDocument> GetDocuments()
    {
        var assembly = typeof(LegalNoticeProvider).Assembly;
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
