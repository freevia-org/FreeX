using FreeW.Core.Model;
using FreeW.App.Presentation.ContextMenus;
using Free.Shared.Ribbon;

namespace FreeW.Ribbon.Definitions;

/// <summary>
/// Avalonia-specific FreeW ribbon surfaces. Ordinary tabs are authored by
/// <see cref="FreeWCanonicalRibbonTabs"/> and this profile retains only File and contextual
/// controls whose representation differs structurally from WPF.
///
/// <para>
/// Command wiring stays in the consuming app's registry; do not add per-command lambdas here.
/// </para>
/// </summary>
internal static class FreeWAvaloniaRibbonDefinition
{
    /// <summary>
    /// AV-PICTAB: preset point sizes offered by the Picture / Drawing Format Size combos.
    /// The user can also type an arbitrary value; the combo's free-text is parsed in the command.
    /// </summary>
    private static readonly string[] FloatSizes = FreeWRibbonDefinitionData.FloatSizes;

    // AV-PICTAB: wrap-mode menu shared by the Picture / Drawing Format "Wrap Text" dropdown.
    // The prefix is "image" or "shape" so the command ids match the WPF host.
    private static RibbonMenu BuildWrapMenu(string prefix) =>
        new(new RibbonMenuItem[]
        {
            new("In Line with Text", new RibbonCommandId($"freew.{prefix}-wrap-inline")),
            new("Square",            new RibbonCommandId($"freew.{prefix}-wrap-square")),
            new("Tight",             new RibbonCommandId($"freew.{prefix}-wrap-tight")),
            new("Top and Bottom",    new RibbonCommandId($"freew.{prefix}-wrap-top-bottom")),
            new("Behind Text",       new RibbonCommandId($"freew.{prefix}-wrap-behind")),
            new("In Front of Text",  new RibbonCommandId($"freew.{prefix}-wrap-front")),
        });

    // AV-PICTAB: rotate/flip menu shared by Picture / Drawing Format "Rotate" dropdown.
    private static RibbonMenu BuildRotateMenu(string prefix) =>
        new(new RibbonMenuItem[]
        {
            new("Rotate Right 90°", new RibbonCommandId($"freew.{prefix}-rotate-right90")),
            new("Rotate Left 90°",  new RibbonCommandId($"freew.{prefix}-rotate-left90")),
            RibbonMenuItem.Separator(),
            new("Flip Vertical",    new RibbonCommandId($"freew.{prefix}-flip-vertical")),
            new("Flip Horizontal",  new RibbonCommandId($"freew.{prefix}-flip-horizontal")),
        });

    private static RibbonMenu BuildFloatingPositionMenu(string prefix) =>
        new(FreeWRibbonDefinitionData.FloatingPositionPresets
            .Select(preset => new RibbonMenuItem(
                preset.Label,
                new RibbonCommandId($"freew.{prefix}-position-{preset.Suffix}")))
            .Concat(prefix == "image"
                ? [RibbonMenuItem.Separator(), new RibbonMenuItem("More Layout Options...", new RibbonCommandId("freew.image-position"))]
                : [])
            .ToArray());

    private static RibbonMenu BuildShapeSizeMenu() =>
        new(FreeWRibbonDefinitionData.FloatingSizePresets
            .Select(preset => new RibbonMenuItem(
                preset.Label,
                new RibbonCommandId($"freew.shape-size-{preset.Suffix}")))
            .ToArray());

    private static RibbonMenu BuildShapeAltTextMenu() =>
        new(FreeWRibbonDefinitionData.ShapeAltTextPresets
            .Select(preset => new RibbonMenuItem(
                preset.Label,
                new RibbonCommandId($"freew.shape-alt-text-{preset.Suffix}")))
            .ToArray());

    private static RibbonMenu BuildShapeStylesMenu() =>
        new(ShapeStylePreset.Catalog
            .Select(preset => new RibbonMenuItem(
                preset.Name,
                new RibbonCommandId($"freew.{preset.Id}")))
            .ToArray());

    private static RibbonMenu BuildShapeChangeMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Rectangle", new RibbonCommandId("freew.shape-change-rectangle")),
            new("Rounded Rectangle", new RibbonCommandId("freew.shape-change-rounded")),
            new("Ellipse", new RibbonCommandId("freew.shape-change-ellipse")),
        });

    private static RibbonMenu BuildShapeEditMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Convert to Freeform", new RibbonCommandId("freew.shape-convert-freeform")),
            new("Edit Points", new RibbonCommandId("freew.shape-edit-points")),
        });

    private static RibbonMenu BuildShapeFillMenu() =>
        new(new RibbonMenuItem[]
        {
            new("No Fill", new RibbonCommandId("freew.shape-fill-no-fill")),
            RibbonMenuItem.Separator(),
            new("Gradient Blue", new RibbonCommandId("freew.shape-fill-gradient-blue")),
            new("Gradient Orange", new RibbonCommandId("freew.shape-fill-gradient-orange")),
            new("Pattern Diagonal", new RibbonCommandId("freew.shape-fill-pattern-diag")),
        });

    private static RibbonMenu BuildShapeOutlineMenu() =>
        new(new RibbonMenuItem[]
        {
            new("No Outline", new RibbonCommandId("freew.shape-outline-no-outline")),
            new("Solid", new RibbonCommandId("freew.shape-outline-solid")),
            new("Dash", new RibbonCommandId("freew.shape-outline-dash")),
            new("Dot", new RibbonCommandId("freew.shape-outline-dot")),
        });

    private static RibbonMenu BuildShapeEffectsMenu() =>
        new(new RibbonMenuItem[]
        {
            new("None", new RibbonCommandId("freew.shape-effects-none")),
            RibbonMenuItem.Separator(),
            new("Shadow", new RibbonCommandId("freew.shape-effect-shadow")),
            new("Glow", new RibbonCommandId("freew.shape-effect-glow")),
            new("Soft Edges", new RibbonCommandId("freew.shape-effect-soft-edge")),
            new("Reflection", new RibbonCommandId("freew.shape-effect-reflection")),
            new("Bevel", new RibbonCommandId("freew.shape-effect-bevel")),
        });

    private static RibbonMenu BuildShapeTextDirectionMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Horizontal", new RibbonCommandId("freew.shape-text-horizontal")),
            new("Rotate 90°", new RibbonCommandId("freew.shape-text-rotate90")),
            new("Rotate 270°", new RibbonCommandId("freew.shape-text-rotate270")),
        });

    private static RibbonMenu BuildTableBordersMenu() =>
        new(new RibbonMenuItem[]
        {
            new("All Borders",      new RibbonCommandId("freew.table-borders.all")),
            new("Outside Borders",  new RibbonCommandId("freew.table-borders.outside")),
            new("Inside Borders",   new RibbonCommandId("freew.table-borders.inside")),
            new("No Border",        new RibbonCommandId("freew.table-borders.none")),
            RibbonMenuItem.Separator(),
            new("Top Border",       new RibbonCommandId("freew.table-borders.top")),
            new("Bottom Border",    new RibbonCommandId("freew.table-borders.bottom")),
            new("Left Border",      new RibbonCommandId("freew.table-borders.left")),
            new("Right Border",     new RibbonCommandId("freew.table-borders.right")),
        });

    /// <summary>
    /// AV-CHARTTAB: Chart Design &gt; Change Chart Type dropdown — one item per <see cref="ChartKind"/>.
    /// Command ids are <c>freew.chart-type-&lt;kind&gt;</c> (lower-case), matching the WPF host.
    /// </summary>
    private static RibbonMenu BuildChartTypeMenu() =>
        new(Enum.GetValues<ChartKind>()
            .Select(k => new RibbonMenuItem(k.ToString(),
                new RibbonCommandId($"freew.chart-type-{k.ToString().ToLowerInvariant()}")))
            .ToArray());

    /// <summary>
    /// AV-CHARTTAB: Chart Design &gt; Chart Styles dropdown — one item per <see cref="ChartStyle.Catalog"/>
    /// entry. Command ids are <c>freew.chart-style-&lt;id&gt;</c>, matching the WPF host.
    /// </summary>
    private static RibbonMenu BuildChartStyleMenu() =>
        new(ChartStyle.Catalog
            .Select(s => new RibbonMenuItem(s.Name, new RibbonCommandId($"freew.chart-style-{s.Id}")))
            .ToArray());

    /// <summary>
    /// AV-CHARTTAB: Chart Design &gt; Change Colors dropdown — one item per <see cref="ChartColorScheme.Catalog"/>
    /// entry. Command ids are <c>freew.chart-colors-&lt;id&gt;</c>.
    /// </summary>
    private static RibbonMenu BuildChartColorsMenu() =>
        new(ChartColorScheme.Catalog
            .Select(s => new RibbonMenuItem(s.Name, new RibbonCommandId($"freew.chart-colors-{s.Id}")))
            .ToArray());

    /// <summary>
    /// AV-CHARTTAB: SmartArt Design &gt; Layouts dropdown. The menu is driven by the shared preset catalog
    /// so every layout that the model and renderer can represent is reachable from the Avalonia host.
    /// Command ids are <c>freew.smartart-layout-&lt;preset-id&gt;</c>.
    /// </summary>
    private static RibbonMenu BuildSmartArtLayoutMenu() =>
        new(SmartArtLayoutPreset.Catalog
            .Select(preset => new RibbonMenuItem(
                preset.Name,
                new RibbonCommandId($"freew.smartart-layout-{preset.Id}")))
            .ToArray());

    /// <summary>
    /// AV-CHARTTAB: SmartArt Design &gt; Change Colors dropdown. SmartArt native color-scheme ids differ
    /// from chart color-scheme ids. Command ids are <c>freew.smartart-colors-&lt;id&gt;</c>.
    /// </summary>
    private static RibbonMenu BuildSmartArtColorsMenu() =>
        new(SmartArtColorScheme.Catalog
            .Select(s => new RibbonMenuItem(s.Name, new RibbonCommandId($"freew.smartart-colors-{s.Id}")))
            .ToArray());

    private static RibbonMenu BuildTableStylesMenu() => FreeWContextMenuPlanner.BuildTableStyles();

    internal static RibbonDefinition Build(FreeWRibbonCapabilities capabilities) =>
        new RibbonDefinitionBuilder()
            .Tab("file", "File", "F", tab =>
                tab.Group("document", "Document", null, 100, g =>
                {
                    g.Button("freew.backstage", "File...");
                    g.Button("freew.new",  "New");
                    g.Button("freew.open", "Open");
                    g.Button("freew.import-pdf-text", "Import PDF (text only)");
                    g.Button("freew.save", "Save");
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
            // ── Table contextual tabs (shown only when caret is in a table cell) ─────────────
            .ContextualTab("table-design", "Table Design",
                new RibbonTabContext(capabilities.TableContextKey, "Table Tools", RibbonContextColor.Teal),
                tab =>
                {
                    tab.Group("table-style-options", "Table Style Options", null, 100, g =>
                    {
                        g.Toggle("freew.table-header-row",   "Header Row");
                        g.Toggle("freew.table-last-row",     "Last Row");
                        g.Toggle("freew.table-first-column", "First Column");
                        g.Toggle("freew.table-last-column",  "Last Column");
                        g.Toggle("freew.table-banded-rows",  "Banded Rows");
                        g.Toggle("freew.table-banded-cols",  "Banded Columns");
                    });
                    tab.Group("table-style", "Table Style", null, 90, g =>
                    {
                        g.Dropdown("freew.table-styles", "Table Styles", BuildTableStylesMenu());
                        g.Button("freew.table-shading", "Shading");
                        g.Dropdown("freew.table-borders", "Borders", BuildTableBordersMenu());
                    });
                    tab.Group("draw-borders", "Draw Borders", null, 80, g =>
                    {
                        g.Button("freew.draw-table", "Draw Table", b => b with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Medium,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table, RibbonCommandIconAccent.Border)
                        });
                        g.Button("freew.eraser", "Eraser", b => b with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Medium,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Clear)
                        });
                    });
                })
            .ContextualTab("table-layout", "Table Layout",
                new RibbonTabContext(capabilities.TableContextKey, "Table Tools", RibbonContextColor.Teal),
                tab =>
                {
                    tab.Group("table-select", "Table", null, 110, g =>
                    {
                        g.Button("freew.table-select-table", "Select Table");
                        g.Button("freew.table-select-row",   "Select Row");
                        g.Button("freew.table-select-col",   "Select Column");
                        g.Button("freew.table-select-cell",  "Select Cell");
                        g.Toggle("freew.table-view-gridlines", "View Gridlines");
                        g.Button("freew.table-properties", "Properties");
                    });
                    tab.Group("table-rows-cols", "Rows & Columns", null, 100, g =>
                    {
                        g.Button("freew.table-insert-above",     "Insert Above");
                        g.Button("freew.table-insert-below",     "Insert Below");
                        g.Button("freew.table-insert-col-left",  "Insert Left");
                        g.Button("freew.table-insert-col-right", "Insert Right");
                        g.Button("freew.table-delete-row",       "Delete Row");
                        g.Button("freew.table-delete-col",       "Delete Column");
                        g.Button("freew.table-delete",           "Delete Table");
                    });
                    tab.Group("table-merge", "Merge", null, 90, g =>
                    {
                        g.Button("freew.table-merge-cells", "Merge Cells");
                        g.Button("freew.table-split-cell",  "Split Cell");
                        g.Button("freew.split-table", "Split Table");
                    });
                    tab.Group("table-cell-size", "Cell Size", null, 95, g =>
                    {
                        g.Button("freew.table-row-height", "Row Height");
                        g.Button("freew.table-col-width", "Column Width");
                        g.Button("freew.table-distribute-rows", "Distribute Rows");
                        g.Button("freew.table-distribute-cols", "Distribute Columns");
                        g.Button("freew.table-autofit-contents", "AutoFit Contents");
                        g.Button("freew.table-autofit-window", "AutoFit Window");
                        g.Button("freew.table-autofit-fixed", "Fixed Column Width");
                    });
                    // BY2: cell alignment parity with WPF's table-layout Alignment group.
                    // 9 buttons = 3 vertical (Top/Middle/Bottom) × 3 horizontal (Left/Center/Right).
                    tab.Group("table-alignment", "Alignment", null, 110, g =>
                    {
                        g.Button("freew.cell-align-top-left",      "Top Left");
                        g.Button("freew.cell-align-top-center",    "Top Center");
                        g.Button("freew.cell-align-top-right",     "Top Right");
                        g.Button("freew.cell-align-middle-left",   "Middle Left");
                        g.Button("freew.cell-align-middle-center", "Middle Center");
                        g.Button("freew.cell-align-middle-right",  "Middle Right");
                        g.Button("freew.cell-align-bottom-left",   "Bottom Left");
                        g.Button("freew.cell-align-bottom-center", "Bottom Center");
                        g.Button("freew.cell-align-bottom-right",  "Bottom Right");
                        g.Button("freew.table-cell-margins", "Cell Margins");
                        g.Button("freew.cell-text-direction-horizontal", "Horizontal");
                        g.Button("freew.cell-text-direction-rotate90", "Rotate Text Up");
                        g.Button("freew.cell-text-direction-rotate270", "Rotate Text Down");
                    });
                    tab.Group("table-data", "Data", null, 80, g =>
                    {
                        g.Toggle("freew.table-repeat-header", "Repeat Header Row");
                        g.Button("freew.table-formula", "Formula");
                        g.Button("freew.sort", "Sort");
                        g.Button("freew.table-to-text", "Convert to Text");
                    });
                })
            .AddHeaderFooterDesignTab(capabilities)
            // ── AV-PICTAB: Picture Format contextual tab (shown when a floating IMAGE is selected) ──
            .ContextualTab("picture-format", "Picture Format",
                new RibbonTabContext(capabilities.PictureContextKey, "Picture Tools", RibbonContextColor.Orange),
                tab =>
                {
                    tab.Group("picture-arrange", "Arrange", null, 100, g =>
                    {
                        g.Dropdown("freew.image-position", "Position", BuildFloatingPositionMenu("image"));
                        g.Dropdown("freew.image-wrap", "Wrap Text", BuildWrapMenu("image"));
                        g.Dropdown("freew.image-rotate", "Rotate", BuildRotateMenu("image"));
                        g.Button("freew.shape-bring-to-front", "Bring to Front");
                        g.Button("freew.shape-send-to-back",   "Send to Back");
                        g.Button("freew.shape-bring-forward",  "Bring Forward");
                        g.Button("freew.shape-send-backward",  "Send Backward");
                        g.Button("freew.image-align-left", "Align Left");
                        g.Button("freew.image-align-center", "Align Center");
                        g.Button("freew.image-align-right", "Align Right");
                        g.Button("freew.image-align-to-page",  "Align to Page");
                        g.Button("freew.image-align-to-margin", "Align to Margin");
                        g.Button("freew.image-distribute-h",   "Distribute Horizontally");
                        g.Button("freew.image-distribute-v",   "Distribute Vertically");
                        g.Button("freew.object-group",         "Group");
                        g.Button("freew.object-ungroup",       "Ungroup");
                    });
                    tab.Group("picture-styles", "Picture Styles", null, 98, g =>
                    {
                        foreach (var preset in PictureStyleCatalog.Catalog)
                        {
                            g.Button($"freew.image-style-{preset.Id}", preset.Name, b => b with
                            {
                                Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border)
                            });
                        }
                    });
                    tab.Group("picture-adjust", "Adjust", null, 90, g =>
                    {
                        g.Button("freew.image-reset", "Reset Picture", b => b with
                        {
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh)
                        });
                        g.Button("freew.image-border", "Picture Border", b => b with
                        {
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border, RibbonCommandIconAccent.Border)
                        });
                        g.Button("freew.image-crop", "Crop", b => b with
                        {
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Scale)
                        });
                    });
                    tab.Group("picture-size", "Size", null, 90, g =>
                    {
                        g.ComboBox("freew.image-width",  "Width",  c => c with { Items = FloatSizes, Width = 72 });
                        g.ComboBox("freew.image-height", "Height", c => c with { Items = FloatSizes, Width = 72 });
                        g.Button("freew.image-size", "Size", b => b with
                        {
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Size)
                        });
                        g.Button("freew.image-alt-text", "Alt Text", b => b with
                        {
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Info)
                        });
                    });
                })
            // ── AV-PICTAB: Drawing Format contextual tab (shown when a non-image float is selected) ──
            .ContextualTab("drawing-format", "Drawing Format",
                new RibbonTabContext(capabilities.DrawingContextKey, "Drawing Tools", RibbonContextColor.Purple),
                tab =>
                {
                    // Shape Styles — gallery/fill/outline use the shared object-format model commands.
                    tab.Group("drawing-styles", "Shape Styles", null, 100, g =>
                    {
                        g.Dropdown("freew.shape-styles-gallery", "Shape Styles", BuildShapeStylesMenu());
                        g.Dropdown("freew.shape-fill", "Shape Fill", BuildShapeFillMenu());
                        g.Dropdown("freew.shape-outline", "Shape Outline", BuildShapeOutlineMenu());
                        g.Dropdown("freew.shape-effects", "Shape Effects", BuildShapeEffectsMenu());
                        g.Dropdown("freew.shape-change", "Change Shape", BuildShapeChangeMenu());
                        g.Dropdown("freew.shape-edit-shape", "Edit Shape", BuildShapeEditMenu());
                        g.Dropdown("freew.shape-text-direction", "Text Direction", BuildShapeTextDirectionMenu());
                    });
                    tab.Group("drawing-arrange", "Arrange", null, 90, g =>
                    {
                        g.Dropdown("freew.shape-position", "Position", BuildFloatingPositionMenu("shape"));
                        g.Dropdown("freew.shape-wrap", "Wrap Text", BuildWrapMenu("shape"));
                        g.Dropdown("freew.shape-rotate", "Rotate", BuildRotateMenu("shape"));
                        g.Button("freew.image-bring-to-front", "Bring to Front");
                        g.Button("freew.image-send-to-back",   "Send to Back");
                        g.Button("freew.image-bring-forward",  "Bring Forward");
                        g.Button("freew.image-send-backward",  "Send Backward");
                        g.Button("freew.shape-align-left", "Align Left");
                        g.Button("freew.shape-align-center", "Align Center");
                        g.Button("freew.shape-align-right", "Align Right");
                        g.Button("freew.shape-align-to-page",  "Align to Page");
                        g.Button("freew.shape-align-to-margin", "Align to Margin");
                        g.Button("freew.shape-distribute-h",   "Distribute Horizontally");
                        g.Button("freew.shape-distribute-v",   "Distribute Vertically");
                        g.Button("freew.object-group",         "Group");
                        g.Button("freew.object-ungroup",       "Ungroup");
                    });
                    tab.Group("drawing-size", "Size", null, 80, g =>
                    {
                        g.ComboBox("freew.shape-width",  "Width",  c => c with { Items = FloatSizes, Width = 72 });
                        g.ComboBox("freew.shape-height", "Height", c => c with { Items = FloatSizes, Width = 72 });
                        g.Dropdown("freew.shape-size", "Size", BuildShapeSizeMenu());
                        g.Dropdown("freew.shape-alt-text", "Alt Text", BuildShapeAltTextMenu());
                    });
                })
            // ── AV-CHARTTAB: Chart Design contextual tab (shown when a floating CHART is selected) ──
            .ContextualTab("chart-design", "Chart Design",
                new RibbonTabContext(capabilities.ChartContextKey, "Chart Tools", RibbonContextColor.Green),
                tab =>
                {
                    tab.Group("chart-type", "Type", null, 100, g =>
                    {
                        g.Dropdown("freew.chart-type", "Change Chart Type", BuildChartTypeMenu());
                    });
                    tab.Group("chart-data", "Data", null, 80, g =>
                    {
                        g.ComboBox("freew.chart-edit-data", "Edit Data", c => c with
                        {
                            Items = new[] { "Quarterly Sales", "Monthly Revenue" },
                            Width = 132
                        });
                    });
                    tab.Group("chart-quick-layout", "Quick Layout", null, 85, g =>
                    {
                        foreach (var layout in ChartQuickLayout.Catalog)
                        {
                            g.Button($"freew.chart-quick-layout-{layout.Id}", layout.Name, b => b with
                            {
                                Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid)
                            });
                        }
                    });
                    tab.Group("chart-styles", "Chart Styles", null, 90, g =>
                    {
                        g.Dropdown("freew.chart-style",  "Chart Styles",  BuildChartStyleMenu());
                        g.Dropdown("freew.chart-colors", "Change Colors", BuildChartColorsMenu());
                    });
                    tab.Group("chart-elements", "Chart Elements", null, 80, g =>
                    {
                        g.Toggle("freew.chart-toggle-legend", "Legend");
                        g.Button("freew.chart-title", "Chart Title");
                        g.Button("freew.chart-axis-titles", "Axis Titles");
                    });
                })
            // ── AV-CHARTTAB: Chart Format contextual tab — shared Arrange/Size (reuse shape commands) ──
            .ContextualTab("chart-format", "Chart Format",
                new RibbonTabContext(capabilities.ChartContextKey, "Chart Tools", RibbonContextColor.Green),
                tab =>
                {
                     tab.Group("chart-arrange", "Arrange", null, 100, g =>
                     {
                         g.Dropdown("freew.shape-rotate", "Rotate", BuildRotateMenu("shape"));
                         g.Dropdown("freew.shape-wrap", "Wrap Text", BuildWrapMenu("shape"));
                        g.Button("freew.image-bring-to-front", "Bring to Front");
                        g.Button("freew.image-send-to-back",   "Send to Back");
                        g.Button("freew.image-bring-forward",  "Bring Forward");
                        g.Button("freew.image-send-backward",  "Send Backward");
                    });
                    tab.Group("chart-size", "Size", null, 90, g =>
                    {
                        g.ComboBox("freew.chart-size", "Size", c => c with
                        {
                            Items = new[] { "360 x 216", "400 x 300", "468 x 288" },
                            Width = 90
                        });
                        g.ComboBox("freew.shape-width",  "Width",  c => c with { Items = FloatSizes, Width = 72 });
                        g.ComboBox("freew.shape-height", "Height", c => c with { Items = FloatSizes, Width = 72 });
                        g.Button("freew.chart-size-dialog", "More Size Options...");
                    });
                })
            // ── AV-CHARTTAB: SmartArt Design contextual tab (shown when a floating SMARTART is selected) ──
            .ContextualTab("smartart-design", "SmartArt Design",
                new RibbonTabContext(capabilities.SmartArtContextKey, "SmartArt Tools", RibbonContextColor.Blue),
                tab =>
                {
                    tab.Group("smartart-create-graphic", "Create Graphic", null, 120, g =>
                    {
                        g.Button("freew.smartart-add-shape", "Add Shape");
                        g.Button("freew.smartart-remove-shape", "Remove Shape");
                        g.RowBreak();
                        g.Button("freew.smartart-promote", "Promote");
                        g.Button("freew.smartart-demote", "Demote");
                        g.RowBreak();
                        g.Button("freew.smartart-move-up", "Move Up");
                        g.Button("freew.smartart-move-down", "Move Down");
                    });
                    tab.Group("smartart-edit", "Edit", null, 90, g =>
                        g.Button("freew.smartart-edit-text", "Edit Text"));
                    tab.Group("smartart-layouts", "Layouts", null, 100, g =>
                    {
                        g.Dropdown("freew.smartart-layout", "Layouts", BuildSmartArtLayoutMenu());
                    });
                    tab.Group("smartart-styles", "SmartArt Styles", null, 90, g =>
                    {
                        g.Dropdown("freew.smartart-colors", "Change Colors", BuildSmartArtColorsMenu());
                        g.ComboBox("freew.smartart-change-style", "Styles", combo => combo with
                        {
                            Items = SmartArtStyle.Catalog.Select(style => style.Name).ToArray(),
                            Width = 116,
                        });
                    });
                     tab.Group("smartart-arrange", "Arrange", null, 80, g =>
                     {
                         g.Dropdown("freew.shape-rotate", "Rotate", BuildRotateMenu("shape"));
                         g.Dropdown("freew.shape-wrap", "Wrap Text", BuildWrapMenu("shape"));
                        g.Button("freew.image-bring-to-front", "Bring to Front");
                        g.Button("freew.image-send-to-back",   "Send to Back");
                    });
                    tab.Group("smartart-size", "Size", null, 70, g =>
                    {
                        g.ComboBox("freew.shape-width",  "Width",  c => c with { Items = FloatSizes, Width = 72 });
                        g.ComboBox("freew.shape-height", "Height", c => c with { Items = FloatSizes, Width = 72 });
                    });
                })
            .Build();

}
