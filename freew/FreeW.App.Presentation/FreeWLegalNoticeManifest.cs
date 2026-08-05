namespace FreeW.App.Presentation;

/// <summary>Canonical embedded-resource manifest for every FreeW renderer.</summary>
public static class FreeWLegalNoticeManifest
{
    public static IReadOnlyList<(string Title, string ResourceName)> Resources { get; } =
        Array.AsReadOnly<(string Title, string ResourceName)>(
        [
            ("Project License", "FreeW.Legal.ProjectLicense.txt"),
            ("Legal Notices", "FreeW.Legal.LegalNotices.md"),
            ("Privacy Notice", "FreeW.Legal.PrivacyNotice.md"),
            ("Third-Party Notices", "FreeW.Legal.ThirdPartyNotices.md"),
            ("Third-Party License Texts", "FreeW.Legal.ThirdPartyLicenses.md"),
        ]);
}
