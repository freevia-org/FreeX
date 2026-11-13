using Free.Shared.Shell;

namespace FreeW.App.Presentation;

/// <summary>FreeW Legal Notices content contract shared by both desktop renderers.</summary>
public static class FreeWLegalNoticesPresentation
{
    public static LegalNoticesDialogPresentation Create(
        IReadOnlyList<LegalNoticeDocument> notices,
        Func<string, string?>? resolveText = null) =>
        ProductLegalNoticesPresentation.Create(
            "FreeW",
            notices,
            resolveText,
            LegalNoticesTextRenderingPolicy.GrayscaleAntialias);

    public static LegalNoticesDialogPresentation Create(
        IReadOnlyList<(string Title, string Text)> notices,
        Func<string, string?>? resolveText = null) =>
        ProductLegalNoticesPresentation.Create(
            "FreeW",
            notices,
            resolveText,
            LegalNoticesTextRenderingPolicy.GrayscaleAntialias);
}
