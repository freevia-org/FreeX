using System.Reflection;
using Free.Shared.Shell;

namespace FreeW.App.Presentation;

/// <summary>Canonical embedded-resource manifest for every FreeW renderer.</summary>
public static class FreeWLegalNoticeManifest
{
    public static IReadOnlyList<LegalNoticeResource> Resources { get; } =
        Array.AsReadOnly<LegalNoticeResource>(
        [
            new("Project License", "FreeW.Legal.ProjectLicense.txt"),
            new("Legal Notices", "FreeW.Legal.LegalNotices.md"),
            new("Privacy Notice", "FreeW.Legal.PrivacyNotice.md"),
            new("Third-Party Notices", "FreeW.Legal.ThirdPartyNotices.md"),
            new("Third-Party License Texts", "FreeW.Legal.ThirdPartyLicenses.md"),
        ]);
}

/// <summary>Loads FreeW's app-owned legal manifest for either desktop renderer.</summary>
public static class FreeWLegalNoticeProvider
{
    public static IReadOnlyList<LegalNoticeDocument> GetDocuments(Assembly assembly) =>
        EmbeddedLegalNoticeLoader.GetDocuments(assembly, FreeWLegalNoticeManifest.Resources);
}
