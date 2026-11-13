using Free.Shared.Shell;

namespace FreeP.App.Compositor;

/// <summary>Canonical embedded-resource manifest shared by FreeP's desktop renderers.</summary>
public static class FreePLegalNoticeManifest
{
    public static IReadOnlyList<LegalNoticeResource> Resources { get; } =
        ProductLegalNoticeManifest.Create("FreeP");
}

/// <summary>Loads FreeP's renderer-independent, offline legal notice bundle.</summary>
public static class FreePLegalNoticeProvider
{
    public static IReadOnlyList<LegalNoticeDocument> GetDocuments() =>
        EmbeddedLegalNoticeLoader.GetDocuments(
            typeof(FreePLegalNoticeProvider).Assembly,
            FreePLegalNoticeManifest.Resources);
}
