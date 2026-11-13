using System.Reflection;
using Free.Shared.Shell;

namespace FreeX.App.Services;

public static class LegalNoticeProvider
{
    private static readonly IReadOnlyList<LegalNoticeResource> Resources =
        ProductLegalNoticeManifest.Create("FreeX");

    internal static IReadOnlyList<LegalNoticeResource> ExpectedEmbeddedResources => Resources;

    public static IReadOnlyList<LegalNoticeDocument> GetDocuments() =>
        GetDocuments(typeof(LegalNoticeProvider).Assembly);

    internal static IReadOnlyList<LegalNoticeDocument> GetDocuments(Assembly assembly)
    {
        return EmbeddedLegalNoticeLoader.GetDocuments(assembly, Resources);
    }
}
