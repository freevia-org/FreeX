namespace FreeW.Ribbon.Definitions;

/// <summary>
/// Avalonia-specific FreeW ribbon surface. Canonical ordinary and contextual builders retain the
/// established Avalonia control representations while this renderer owns only the File surface.
/// </summary>
internal static class FreeWAvaloniaRibbonDefinition
{
    internal static RibbonDefinition Build(FreeWRibbonCapabilities capabilities) =>
        new RibbonDefinitionBuilder()
            .Tab("file", "File", "F", tab =>
                tab.Group("document", "Document", null, 100, group =>
                {
                    group.Button("freew.backstage", "File...");
                    group.Button("freew.new", "New");
                    group.Button("freew.open", "Open");
                    group.Button("freew.import-pdf-text", "Import PDF (text only)");
                    group.Button("freew.save", "Save");
                }))
            .AddHomeTab(capabilities)
            .AddInsertTab(capabilities)
            .AddLayoutTab(capabilities)
            .AddDesignTab(capabilities)
            .AddViewTab(capabilities)
            .AddReviewTab(capabilities)
            .AddDeveloperTab(capabilities)
            .AddReferencesTab(capabilities)
            .AddMailingsTab(capabilities)
            .AddHelpTab(capabilities)
            .AddTableContextualTabs(capabilities)
            .AddHeaderFooterDesignTab(capabilities)
            .AddPictureContextualTab(capabilities)
            .AddDrawingContextualTab(capabilities)
            .AddChartContextualTabs(capabilities)
            .AddSmartArtContextualTab(capabilities)
            .Build();
}
