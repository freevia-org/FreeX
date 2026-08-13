using System.Reflection;
using Free.Shared.Shell;

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
        return EmbeddedLegalNoticeLoader.GetDocuments(assembly, Resources);
    }
}
