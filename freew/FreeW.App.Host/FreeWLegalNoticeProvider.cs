using Free.Shared.Shell.Wpf;
using System.Reflection;

namespace FreeW.App.Host;

/// <summary>
/// FreeW legal-notice provider. Supplies the resource list to the shared loader and returns
/// neutral (Title, Text) tuples. The embedded .txt/.md resources stay in FreeW.App.Host's
/// assembly; only the loading logic is shared via <see cref="SharedLegalNoticeLoader"/>.
/// </summary>
internal static class FreeWLegalNoticeProvider
{
    private static readonly (string Title, string ResourceName)[] Resources =
    [
        ("Project License", "FreeW.Legal.ProjectLicense.txt"),
        ("Legal Notices", "FreeW.Legal.LegalNotices.md"),
        ("Privacy Notice", "FreeW.Legal.PrivacyNotice.md"),
        ("Third-Party Notices", "FreeW.Legal.ThirdPartyNotices.md"),
        ("Third-Party License Texts", "FreeW.Legal.ThirdPartyLicenses.md")
    ];

    internal static IReadOnlyList<(string Title, string ResourceName)> ExpectedEmbeddedResources => Resources;

    public static IReadOnlyList<(string Title, string Text)> GetDocuments() =>
        GetDocuments(typeof(FreeWLegalNoticeProvider).Assembly);

    internal static IReadOnlyList<(string Title, string Text)> GetDocuments(Assembly assembly) =>
        SharedLegalNoticeLoader.GetDocuments(assembly, Resources);
}
