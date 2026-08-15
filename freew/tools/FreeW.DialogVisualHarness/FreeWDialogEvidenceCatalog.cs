using System.Text.RegularExpressions;
using Free.Shared.AppServices;

namespace FreeW.DialogVisualHarness;

public enum FreeWDialogHost
{
    Wpf,
    Avalonia,
}

public enum FreeWDialogRouteCoverage
{
    Paired,
    AvaloniaExtension,
}

public enum FreeWDialogOpenAction
{
    ReflectedDialog,
    StaticPrompt,
    BackstagePane,
    BookmarkManager,
    ManualHyphenation,
    ScreenClipOverlay,
    KnownDialog,
    Options,
    PageSetup,
    NotesPane,
    CupsPrint,
    CompareDocuments,
    PasswordPrompt,
    TableFormula,
    TableProperties,
    Style,
    CharacterFormattingPicker,
}

public enum FreeWDialogFixtureKind
{
    DefaultConstructor,
    BookmarkTargets,
    ManualHyphenationCandidate,
    BackstageProductionShell,
    ScreenClipSelection,
    DefaultRunFormatting,
    DefaultParagraphFormatting,
    EmptyListFormats,
    HarnessClipboardText,
    StyleCatalog,
    EmptyTextDocument,
    EmptySourceLists,
    PageSetupSection,
    NotesDocument,
    NoPrinters,
    CompareDocuments,
    PasswordPrompt,
    TableFormula,
    TableProperties,
    CharacterFormattingPicker,
}

public enum FreeWDialogPopulationKind
{
    Generic,
    None,
    ManualHyphenation,
    Style,
    FootnoteEndnoteOptions,
}

public enum FreeWDialogSurfaceKind
{
    Dialog,
    Pane,
    Overlay,
    Backstage,
}

public sealed record FreeWDialogHostRoute(
    string DialogTypeName,
    FreeWDialogOpenAction OpenAction,
    string? EntryPointName = null);

public sealed record FreeWDialogEvidenceRoute(
    string RouteId,
    FreeWDialogHostRoute? Wpf,
    FreeWDialogHostRoute Avalonia,
    FreeWDialogFixtureKind Fixture = FreeWDialogFixtureKind.DefaultConstructor,
    FreeWDialogPopulationKind Population = FreeWDialogPopulationKind.Generic,
    FreeWDialogSurfaceKind SurfaceKind = FreeWDialogSurfaceKind.Dialog,
    string? BackstageMethodName = null,
    bool UseWpfAuthoritySize = false,
    bool HasNativeFrame = true,
    int AvaloniaClientWidthAdjustment = 0)
{
    public FreeWDialogRouteCoverage Coverage =>
        Wpf is null ? FreeWDialogRouteCoverage.AvaloniaExtension : FreeWDialogRouteCoverage.Paired;

    public FreeWDialogHostRoute? ForHost(FreeWDialogHost host) =>
        host == FreeWDialogHost.Wpf ? Wpf : Avalonia;
}

public sealed record FreeWDialogCapturePlan(
    string Host,
    string ScenarioId,
    string RouteId,
    string State,
    string FullPngPath,
    string TargetPngPath,
    string ManifestFileName,
    string ManifestSchema,
    int ManifestSchemaVersion,
    string CapturedNote,
    string UnsupportedNote,
    int TargetHeight,
    bool UseWpfAuthoritySize,
    bool HasNativeFrame,
    int ClientWidthAdjustment);

public static class FreeWDialogEvidenceCatalog
{
    public const string ManifestSchema = "freew.dialog-capture-manifest.v1";

    private static readonly (string Entry, string Method)[] BackstageEntries =
    [
        ("home", "BuildHomePane"),
        ("new", "BuildNewPane"),
        ("open", "BuildOpenPane"),
        ("info", "BuildInfoPane"),
        ("share", "BuildSharePane"),
        ("save-as", "BuildSaveAsPane"),
        ("print", "BuildPrintPane"),
        ("export", "BuildExportPane"),
        ("account", "BuildAccountPane"),
        ("options", "BuildOptionsPane"),
    ];

    private static readonly IReadOnlyList<FreeWDialogEvidenceRoute> RouteItems = BuildRoutes();
    private static readonly IReadOnlyDictionary<string, FreeWDialogEvidenceRoute> RoutesById =
        RouteItems.ToDictionary(route => route.RouteId, StringComparer.OrdinalIgnoreCase);

    static FreeWDialogEvidenceCatalog()
    {
        var errors = Validate(RouteItems);
        if (errors.Count != 0)
            throw new InvalidOperationException("Invalid FreeW dialog evidence catalog: " + string.Join("; ", errors));
    }

    public static IReadOnlyList<FreeWDialogEvidenceRoute> Routes => RouteItems;

    public static bool TryGet(string routeId, out FreeWDialogEvidenceRoute route) =>
        RoutesById.TryGetValue(routeId, out route!);

    public static FreeWDialogEvidenceRoute GetRequired(string routeId) =>
        TryGet(routeId, out var route)
            ? route
            : throw new KeyNotFoundException($"Unknown FreeW dialog evidence route: {routeId}.");

    public static bool IsStaticPrompt(string routeId, FreeWDialogHost host) =>
        GetRequired(routeId).ForHost(host)?.OpenAction == FreeWDialogOpenAction.StaticPrompt;

    public static string CanonicalRoute(string host, string sourceRouteId) => (host, sourceRouteId) switch
    {
        ("wpf", "paragraph-breaks") or ("wpf", "paragraph-indent") => "paragraph",
        ("wpf", "watermark-options") or ("avalonia", "watermark") => "watermark",
        ("wpf", "statistics") => "word-count",
        ("wpf", "about") or ("avalonia", "free-winfo") => "about",
        _ => sourceRouteId,
    };

    public static IReadOnlyList<string> KnownTabs(string routeId) => routeId switch
    {
        "options" => ["General", "AutoCorrect", "AutoFormat As You Type"],
        "page-setup" => ["Margins", "Paper", "Layout"],
        _ => [],
    };

    public static IReadOnlyList<string> ValidTabs(string routeId, IEnumerable<string> discovered) => routeId switch
    {
        "compare-documents" => ["More"],
        "legal-notices" => ["Project License", "Legal Notices", "Privacy Notice", "Third-Party Notices", "Third-Party License Texts"],
        "password-prompt" or "screen-clip-overlay" or "symbol-picker" or "table-formula" => [],
        "table-properties" => ["Table", "Row", "Column", "Cell"],
        _ => discovered.ToArray(),
    };

    public static IReadOnlyList<string> ValidStates(
        string routeId,
        string surfaceKind,
        IReadOnlyList<string> tabs) => routeId switch
    {
        "legal-notices" => new[] { "initial" }.Concat(tabs.Select(tab => $"tab-{Kebab(tab)}")).ToArray(),
        "password-prompt" => ["initial", "populated"],
        "screen-clip-overlay" => ["open"],
        "symbol-picker" or "cell-shading" => ["initial"],
        _ => StateIds(surfaceKind, tabs),
    };

    public static IReadOnlyList<string> StateIds(string surfaceKind, IReadOnlyList<string> tabs) => surfaceKind switch
    {
        "pane" => ["seeded"],
        "overlay" or "backstage" => ["open"],
        _ => new[] { "initial", "populated", "validation-error" }
            .Concat(tabs.Select(tab => $"tab-{Kebab(tab)}"))
            .ToArray(),
    };

    public static string StateDescription(string state) => state switch
    {
        "initial" => "Default constructor state with initial keyboard focus.",
        "populated" => "Representative populated fields, selections, and checked options.",
        "validation-error" => "Representative validation or error state after invalid input.",
        "seeded" => "Seeded app-owned pane state after the route is opened.",
        "open" => "Opened app-owned overlay or Backstage pane state.",
        _ => $"Explicit route state: {state}.",
    };

    public static FreeWDialogCapturePlan CreateCapturePlan(
        string host,
        string scenarioId,
        string routeId,
        string state,
        string? tab)
    {
        var hostKind = ParseHost(host);
        TryGet(routeId, out var route);
        var hostRoute = route?.ForHost(hostKind);

        var safeScenarioId = SafeEvidenceName(scenarioId);
        var staticPrompt = hostRoute?.OpenAction == FreeWDialogOpenAction.StaticPrompt;
        var capturedNote = hostKind == FreeWDialogHost.Wpf
            ? staticPrompt
                ? "Real app-owned WPF static-prompt dialog captured before its cancel path returned; full and target images passed pixel-content validation."
                : "Real app-owned WPF dialog rendered through RenderTargetBitmap; full and target images passed pixel-content validation."
            : "Real app-owned Avalonia dialog rendered through CaptureRenderedFrame; full and target images passed pixel-content validation.";
        var unsupportedNote = hostKind == FreeWDialogHost.Wpf
            ? "No constructible app-owned WPF route adapter was available for this source family."
            : "The Avalonia adapter requires an app-owned route constructor or a temporary capture hook for this family.";

        return new FreeWDialogCapturePlan(
            HostId(hostKind),
            scenarioId,
            routeId,
            state,
            $"full/{HostId(hostKind)}/{safeScenarioId}.png",
            $"crops/{HostId(hostKind)}/{safeScenarioId}.png",
            $"{HostId(hostKind)}_dialog_capture_manifest.json",
            ManifestSchema,
            hostKind == FreeWDialogHost.Wpf ? 1 : 2,
            capturedNote,
            unsupportedNote,
            routeId == "compare-documents" && tab?.Equals("More", StringComparison.OrdinalIgnoreCase) == true ? 720 : 600,
            hostKind == FreeWDialogHost.Avalonia && route?.UseWpfAuthoritySize == true,
            route?.HasNativeFrame ?? true,
            hostKind == FreeWDialogHost.Avalonia ? route?.AvaloniaClientWidthAdjustment ?? 0 : 0);
    }

    public static string SafeEvidenceName(string value) =>
        VisualEvidenceTextPolicy.ToAsciiSafeArtifactName(value);

    public static string ManifestFileName(string host) =>
        $"{HostId(ParseHost(host))}_dialog_capture_manifest.json";

    public static int ManifestSchemaVersion(string host) =>
        ParseHost(host) == FreeWDialogHost.Wpf ? 1 : 2;

    public static IReadOnlyList<string> Validate() => Validate(RouteItems);

    private static IReadOnlyList<string> Validate(IReadOnlyList<FreeWDialogEvidenceRoute> routes)
    {
        var errors = new List<string>();
        foreach (var duplicate in routes.GroupBy(route => route.RouteId, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            errors.Add($"duplicate route id {duplicate.Key}");
        foreach (var route in routes)
        {
            if (string.IsNullOrWhiteSpace(route.RouteId))
                errors.Add("blank route id");
            else if (!route.RouteId.Equals(SafeEvidenceName(route.RouteId), StringComparison.Ordinal)
                     || !route.RouteId.Equals(route.RouteId.ToLowerInvariant(), StringComparison.Ordinal))
                errors.Add($"malformed route id {route.RouteId}");
            if (route.Wpf is not null && route.Coverage != FreeWDialogRouteCoverage.Paired)
                errors.Add($"WPF route {route.RouteId} is not paired");
            if (route.Wpf?.OpenAction == FreeWDialogOpenAction.StaticPrompt
                && string.IsNullOrWhiteSpace(route.Wpf.EntryPointName))
                errors.Add($"static WPF route {route.RouteId} has no entry point");
            if (route.Wpf?.OpenAction != FreeWDialogOpenAction.StaticPrompt
                && route.Wpf?.EntryPointName is not null)
                errors.Add($"non-static WPF route {route.RouteId} has an entry point");
            if (route.SurfaceKind == FreeWDialogSurfaceKind.Backstage && string.IsNullOrWhiteSpace(route.BackstageMethodName))
                errors.Add($"backstage route {route.RouteId} has no pane builder");
            if (route.SurfaceKind != FreeWDialogSurfaceKind.Backstage && route.BackstageMethodName is not null)
                errors.Add($"non-backstage route {route.RouteId} has a pane builder");
            if (route.UseWpfAuthoritySize && route.Wpf is null)
                errors.Add($"Avalonia-only route {route.RouteId} requests WPF authority sizing");
        }
        foreach (var duplicate in routes
                     .Where(route => route.BackstageMethodName is not null)
                     .GroupBy(route => route.BackstageMethodName!, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
            errors.Add($"duplicate backstage pane builder {duplicate.Key}");
        return errors;
    }

    private static FreeWDialogHost ParseHost(string host) => host.ToLowerInvariant() switch
    {
        "wpf" => FreeWDialogHost.Wpf,
        "avalonia" => FreeWDialogHost.Avalonia,
        _ => throw new ArgumentOutOfRangeException(nameof(host), host, "Unknown FreeW dialog evidence host."),
    };

    private static string HostId(FreeWDialogHost host) =>
        host == FreeWDialogHost.Wpf ? "wpf" : "avalonia";

    private static string Kebab(string value) =>
        Regex.Replace(value, "([a-z0-9])([A-Z])", "$1-$2")
            .Replace('_', '-')
            .Replace(' ', '-')
            .ToLowerInvariant();

    private static IReadOnlyList<FreeWDialogEvidenceRoute> BuildRoutes()
    {
        var routes = new List<FreeWDialogEvidenceRoute>
        {
            Pair("about", "AboutDialog", population: FreeWDialogPopulationKind.None),
            Pair("accessibility-report", "AccessibilityReportDialog", useWpfAuthoritySize: true),
            Pair("bookmark-manager", "BookmarkManagerDialog", fixture: FreeWDialogFixtureKind.BookmarkTargets,
                wpfAction: FreeWDialogOpenAction.BookmarkManager, avaloniaAction: FreeWDialogOpenAction.BookmarkManager),
            Pair("borders-and-shading", "BordersAndShadingDialog"),
            Pair("building-blocks-organizer", "BuildingBlocksOrganizerDialog"),
            Pair("chart-axis-titles", "ChartAxisTitlesDialog"),
            Pair("chart-size", "ChartSizeDialog"),
            Pair("chart-title", "ChartTitleDialog"),
            Pair("cell-shading", "CellShadingDialog"),
            Pair("compare-documents", "CompareDocumentsDialog", fixture: FreeWDialogFixtureKind.CompareDocuments,
                avaloniaAction: FreeWDialogOpenAction.CompareDocuments),
            Pair("cross-reference", "CrossReferenceDialog"),
            Pair("customize-theme-colors", "CustomizeThemeColorsDialog"),
            Pair("customize-theme-fonts", "CustomizeThemeFontsDialog"),
            Pair("date-time", "DateTimeDialog"),
            Pair("document-inspector", "DocumentInspectorDialog"),
            Pair("find-replace", "FindReplaceDialog"),
            Pair("footnote-endnote-options", "FootnoteEndnoteOptionsDialog", population: FreeWDialogPopulationKind.FootnoteEndnoteOptions),
            Pair("icon-picker", "IconPickerDialog"),
            Pair("image-adjust", "ImageAdjustDialog"),
            Pair("image-border", "ImageBorderDialog"),
            Pair("image-crop", "ImageCropDialog"),
            Pair("image-position", "ImagePositionDialog"),
            Pair("image-size", "ImageSizeDialog"),
            Pair("insert-chart", "InsertChartDialog"),
            Pair("insert-smart-art", "InsertSmartArtDialog"),
            Pair("legal-notices", "LegalNoticesDialog"),
            Pair("manage-styles", "ManageStylesDialog", fixture: FreeWDialogFixtureKind.EmptyTextDocument,
                wpfAction: FreeWDialogOpenAction.StaticPrompt, wpfEntryPointName: "Ask", useWpfAuthoritySize: true),
            Pair("mark-citation", "MarkCitationDialog"),
            Pair("properties", "PropertiesDialog"),
            Pair("restrict-editing", "RestrictEditingDialog"),
            Pair("sort", "SortDialog"),
            Pair("symbol-picker", "SymbolPickerDialog"),
            Pair("table-formula", "TableFormulaDialog", fixture: FreeWDialogFixtureKind.TableFormula,
                avaloniaAction: FreeWDialogOpenAction.TableFormula),
            Pair("table-of-authorities", "TableOfAuthoritiesDialog"),
            Pair("table-properties", "TablePropertiesDialog", fixture: FreeWDialogFixtureKind.TableProperties,
                avaloniaAction: FreeWDialogOpenAction.TableProperties),
            Pair("tabs", "TabsDialog"),
            Pair("watermark", "WatermarkOptionsDialog", "WatermarkDialog"),
            Pair("word-count", "StatisticsDialog", "WordCountDialog"),
            Pair("zoom", "ZoomDialog"),

            Pair("columns", "ColumnsDialog", avaloniaAction: FreeWDialogOpenAction.KnownDialog),
            Pair("custom-paragraph-spacing", "CustomParagraphSpacingDialog", avaloniaAction: FreeWDialogOpenAction.KnownDialog),
            Pair("drop-cap-options", "DropCapOptionsDialog", avaloniaAction: FreeWDialogOpenAction.KnownDialog),
            Pair("hyphenation-options", "HyphenationOptionsDialog", avaloniaAction: FreeWDialogOpenAction.KnownDialog),
            Pair("line-number-options", "LineNumberOptionsDialog", avaloniaAction: FreeWDialogOpenAction.KnownDialog),
            Pair("options", "OptionsDialog", avaloniaAction: FreeWDialogOpenAction.Options),
            Pair("page-setup", "PageSetupDialog", fixture: FreeWDialogFixtureKind.PageSetupSection,
                avaloniaAction: FreeWDialogOpenAction.PageSetup),
            Pair("password-prompt", "PasswordPromptDialog", fixture: FreeWDialogFixtureKind.PasswordPrompt,
                avaloniaAction: FreeWDialogOpenAction.PasswordPrompt),
            Pair("screen-clip-overlay", "ScreenClipOverlay", fixture: FreeWDialogFixtureKind.ScreenClipSelection,
                wpfAction: FreeWDialogOpenAction.ScreenClipOverlay, avaloniaAction: FreeWDialogOpenAction.ScreenClipOverlay,
                surfaceKind: FreeWDialogSurfaceKind.Overlay, hasNativeFrame: false),

            Pair("font", "FontDialog", fixture: FreeWDialogFixtureKind.DefaultRunFormatting,
                wpfAction: FreeWDialogOpenAction.StaticPrompt, wpfEntryPointName: "Prompt", useWpfAuthoritySize: true),
            Pair("manual-hyphenation", "ManualHyphenationDialog", fixture: FreeWDialogFixtureKind.ManualHyphenationCandidate,
                population: FreeWDialogPopulationKind.ManualHyphenation,
                wpfAction: FreeWDialogOpenAction.ManualHyphenation, avaloniaAction: FreeWDialogOpenAction.ManualHyphenation),
            Pair("multilevel-list", "MultilevelListDialog", fixture: FreeWDialogFixtureKind.EmptyListFormats,
                wpfAction: FreeWDialogOpenAction.StaticPrompt, wpfEntryPointName: "Prompt", useWpfAuthoritySize: true, avaloniaClientWidthAdjustment: 1),
            Pair("paragraph", "ParagraphBreaksDialog", "ParagraphDialog", fixture: FreeWDialogFixtureKind.DefaultParagraphFormatting,
                wpfAction: FreeWDialogOpenAction.StaticPrompt, wpfEntryPointName: "Prompt", useWpfAuthoritySize: true),
            Pair("paste-special", "PasteSpecialDialog", fixture: FreeWDialogFixtureKind.HarnessClipboardText,
                wpfAction: FreeWDialogOpenAction.StaticPrompt, wpfEntryPointName: "Prompt", useWpfAuthoritySize: true),
            Pair("style", "StyleDialog", fixture: FreeWDialogFixtureKind.StyleCatalog,
                population: FreeWDialogPopulationKind.Style,
                wpfAction: FreeWDialogOpenAction.StaticPrompt, avaloniaAction: FreeWDialogOpenAction.Style,
                wpfEntryPointName: "AskNew", useWpfAuthoritySize: true),

            AvaloniaOnly("bookmark", "BookmarkDialog"),
            AvaloniaOnly("caption", "CaptionDialog"),
            AvaloniaOnly("citation-source-picker", "CitationSourcePickerDialog"),
            Pair("comment-list", "CommentListDialog"),
            Pair("comment-reply", "CommentReplyDialog"),
            AvaloniaOnly("character-formatting-picker", "CharacterFormattingPickerDialog",
                FreeWDialogFixtureKind.CharacterFormattingPicker, FreeWDialogOpenAction.CharacterFormattingPicker),
            Pair("draw-table-dimension", "DrawTableDimensionDialog"),
            Pair("field-picker", "FieldPickerDialog"),
            AvaloniaOnly("header-footer-text", "HeaderFooterTextDialog"),
            Pair("hyperlink", "HyperlinkDialog"),
            AvaloniaOnly("image-alt-text", "ImageAltTextDialog"),
            AvaloniaOnly("link-bookmark", "LinkBookmarkDialog"),
            Pair("manage-sources", "ManageSourcesDialogWindow", "ManageSourcesDialog",
                fixture: FreeWDialogFixtureKind.EmptySourceLists,
                wpfAction: FreeWDialogOpenAction.StaticPrompt,
                wpfEntryPointName: "AskManageSourcesForVisualHarness",
                useWpfAuthoritySize: true),
            AvaloniaOnly("note-text", "NoteTextDialog"),
            AvaloniaOnly("page-borders", "PageBordersDialog"),
            AvaloniaOnly("page-color", "PageColorDialog"),
            Pair("page-number-format", "PageNumberFormatDialog"),
            AvaloniaOnly("print-preview", "PrintPreviewDialog"),
            Pair("proofing-language", "ProofingLanguageDialog"),
            AvaloniaOnly("quick-part", "QuickPartDialog"),
            AvaloniaOnly("quick-part-name", "QuickPartNameDialog"),
            Pair("save-compatibility-warning", "SaveCompatibilityWarningDialog"),
            Pair("screen-tip", "ScreenTipDialog"),
            AvaloniaOnly("set-as-default-confirmation", "SetAsDefaultConfirmationDialog"),
            AvaloniaOnly("smart-art-edit", "SmartArtEditDialog"),
            AvaloniaOnly("source-author-editor", "SourceAuthorEditorDialog"),
            AvaloniaOnly("source-conflict-resolution", "SourceConflictResolutionDialog"),
            AvaloniaOnly("source-entry", "SourceEntryDialog"),
            AvaloniaOnly("style-set", "StyleSetDialog"),
            Pair("table-text-conversion", "TableTextConversionDialog"),
            AvaloniaOnly("theme-effects", "ThemeEffectsDialog"),
            AvaloniaOnly("thesaurus", "ThesaurusDialog"),
            AvaloniaOnly("notes-pane", "NotesPane", FreeWDialogFixtureKind.NotesDocument, FreeWDialogOpenAction.NotesPane,
                FreeWDialogSurfaceKind.Pane),
            AvaloniaOnly("cups-print", "CupsPrintDialog", FreeWDialogFixtureKind.NoPrinters, FreeWDialogOpenAction.CupsPrint),
        };

        foreach (var (entry, method) in BackstageEntries)
        {
            routes.Add(Pair(
                $"backstage-{entry}",
                "BackstageView",
                fixture: FreeWDialogFixtureKind.BackstageProductionShell,
                wpfAction: FreeWDialogOpenAction.BackstagePane,
                avaloniaAction: FreeWDialogOpenAction.BackstagePane,
                surfaceKind: FreeWDialogSurfaceKind.Backstage,
                backstageMethodName: method));
        }

        return routes.OrderBy(route => route.RouteId, StringComparer.Ordinal).ToArray();
    }

    private static FreeWDialogEvidenceRoute Pair(
        string routeId,
        string wpfTypeName,
        string? avaloniaTypeName = null,
        FreeWDialogFixtureKind fixture = FreeWDialogFixtureKind.DefaultConstructor,
        FreeWDialogPopulationKind population = FreeWDialogPopulationKind.Generic,
        FreeWDialogOpenAction wpfAction = FreeWDialogOpenAction.ReflectedDialog,
        FreeWDialogOpenAction avaloniaAction = FreeWDialogOpenAction.ReflectedDialog,
        string? wpfEntryPointName = null,
        FreeWDialogSurfaceKind surfaceKind = FreeWDialogSurfaceKind.Dialog,
        string? backstageMethodName = null,
        bool useWpfAuthoritySize = false,
        bool hasNativeFrame = true,
        int avaloniaClientWidthAdjustment = 0) =>
        new(
            routeId,
            new FreeWDialogHostRoute(wpfTypeName, wpfAction, wpfEntryPointName),
            new FreeWDialogHostRoute(avaloniaTypeName ?? wpfTypeName, avaloniaAction),
            fixture,
            population,
            surfaceKind,
            backstageMethodName,
            useWpfAuthoritySize,
            hasNativeFrame,
            avaloniaClientWidthAdjustment);

    private static FreeWDialogEvidenceRoute AvaloniaOnly(
        string routeId,
        string typeName,
        FreeWDialogFixtureKind fixture = FreeWDialogFixtureKind.DefaultConstructor,
        FreeWDialogOpenAction action = FreeWDialogOpenAction.ReflectedDialog,
        FreeWDialogSurfaceKind surfaceKind = FreeWDialogSurfaceKind.Dialog) =>
        new(routeId, null, new FreeWDialogHostRoute(typeName, action), fixture, SurfaceKind: surfaceKind);
}
