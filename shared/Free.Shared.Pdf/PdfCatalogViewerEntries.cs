namespace Free.Shared.Pdf;

/// <summary>Serializes validated viewer preferences shared by the draw-op PDF backends.</summary>
public static class PdfCatalogViewerEntries
{
    public static string Build(PdfDocumentViewerSettings? settings, bool hasOutlines)
    {
        if (settings is null)
            return hasOutlines ? " /PageMode /UseOutlines" : "";

        var pageLayout = settings.PageLayout switch
        {
            PdfViewerPageLayout.OneColumn => "/OneColumn",
            PdfViewerPageLayout.TwoColumnLeft => "/TwoColumnLeft",
            PdfViewerPageLayout.TwoColumnRight => "/TwoColumnRight",
            _ => "/SinglePage"
        };
        var pageMode = settings.OpenMode switch
        {
            PdfViewerOpenMode.FullScreen => "/FullScreen",
            PdfViewerOpenMode.Outlines => "/UseOutlines",
            _ => hasOutlines ? "/UseOutlines" : "/UseNone"
        };
        var title = settings.DisplayDocumentTitle ? " /DisplayDocTitle true" : "";
        var nonFullScreenMode = hasOutlines ? " /NonFullScreenPageMode /UseOutlines" : "";

        return $" /Version /1.7 /PageLayout {pageLayout} /PageMode {pageMode}" +
            " /ViewerPreferences << /PrintScaling /None /FitWindow true /CenterWindow true /PickTrayByPDFSize true" +
            title + nonFullScreenMode + " >>";
    }
}
