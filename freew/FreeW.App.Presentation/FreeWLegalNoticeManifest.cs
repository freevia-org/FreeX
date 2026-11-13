using System.Reflection;
using Free.Shared.Shell;

namespace FreeW.App.Presentation;

/// <summary>Canonical embedded-resource manifest for every FreeW renderer.</summary>
public static class FreeWLegalNoticeManifest
{
    public static IReadOnlyList<LegalNoticeResource> Resources { get; } =
        ProductLegalNoticeManifest.Create("FreeW");
}

/// <summary>Loads FreeW's app-owned legal manifest for either desktop renderer.</summary>
public static class FreeWLegalNoticeProvider
{
    public static IReadOnlyList<LegalNoticeDocument> GetDocuments(Assembly assembly) =>
        EmbeddedLegalNoticeLoader.GetDocuments(assembly, FreeWLegalNoticeManifest.Resources);
}
