using Free.Shared.Shell.Wpf;
using FreeW.App.Presentation;
using System.Reflection;

namespace FreeW.App.Host;

/// <summary>
/// FreeW legal-notice provider. Supplies the resource list to the shared loader and returns
/// neutral (Title, Text) tuples. The embedded .txt/.md resources stay in FreeW.App.Host's
/// assembly; only the loading logic is shared via <see cref="SharedLegalNoticeLoader"/>.
/// </summary>
internal static class FreeWLegalNoticeProvider
{
    internal static IReadOnlyList<(string Title, string ResourceName)> ExpectedEmbeddedResources =>
        FreeWLegalNoticeManifest.Resources;

    public static IReadOnlyList<(string Title, string Text)> GetDocuments() =>
        GetDocuments(typeof(FreeWLegalNoticeProvider).Assembly);

    internal static IReadOnlyList<(string Title, string Text)> GetDocuments(Assembly assembly) =>
        SharedLegalNoticeLoader.GetDocuments(assembly, FreeWLegalNoticeManifest.Resources);
}
