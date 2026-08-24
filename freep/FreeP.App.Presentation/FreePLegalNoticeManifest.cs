using Free.Shared.Shell;

namespace FreeP.App.Compositor;

/// <summary>Canonical embedded-resource manifest shared by FreeP's desktop renderers.</summary>
public static class FreePLegalNoticeManifest
{
    public static IReadOnlyList<LegalNoticeResource> Resources { get; } =
        Array.AsReadOnly<LegalNoticeResource>(
        [
            new("Project License", "FreeP.Legal.ProjectLicense.txt"),
            new("Legal Notices", "FreeP.Legal.LegalNotices.md"),
            new("Privacy Notice", "FreeP.Legal.PrivacyNotice.md"),
            new("Third-Party Notices", "FreeP.Legal.ThirdPartyNotices.md"),
            new("Third-Party License Texts", "FreeP.Legal.ThirdPartyLicenses.md"),
        ]);
}

/// <summary>Loads FreeP's renderer-independent, offline legal notice bundle.</summary>
public static class FreePLegalNoticeProvider
{
    public static IReadOnlyList<LegalNoticeDocument> GetDocuments() =>
        EmbeddedLegalNoticeLoader.GetDocuments(
            typeof(FreePLegalNoticeProvider).Assembly,
            FreePLegalNoticeManifest.Resources);
}
