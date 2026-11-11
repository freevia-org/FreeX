using Free.Shared.Shell;

namespace FreeP.App.Compositor;

/// <summary>FreeP Legal Notices content contract shared by both desktop renderers.</summary>
public static class FreePLegalNoticesPresentation
{
    public static LegalNoticesDialogPresentation Create(
        IReadOnlyList<LegalNoticeDocument> notices,
        Func<string, string?>? resolveText = null) =>
        ProductLegalNoticesPresentation.Create(
            "FreeP",
            notices,
            resolveText,
            LegalNoticesTextRenderingPolicy.GrayscaleAntialias);

    public static LegalNoticesDialogPresentation Create(
        IReadOnlyList<(string Title, string Text)> notices,
        Func<string, string?>? resolveText = null) =>
        ProductLegalNoticesPresentation.Create(
            "FreeP",
            notices,
            resolveText,
            LegalNoticesTextRenderingPolicy.GrayscaleAntialias);
}
