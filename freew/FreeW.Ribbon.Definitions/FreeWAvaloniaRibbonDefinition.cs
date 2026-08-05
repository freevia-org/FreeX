using FreeW.Core.Model;
using FreeW.App.Presentation.ContextMenus;
using Free.Shared.Ribbon;

namespace FreeW.Ribbon.Definitions;

/// <summary>
/// FreeW's ribbon definition for the Avalonia shell. The portable
/// <see cref="RibbonDefinition"/> model lives in Free.Shared.Ribbon (the same definition the WPF
/// host renders); the WPF host's FreeWRibbon layout can't be referenced from Avalonia, so this
/// authors an equivalent portable definition here.
///
/// <para>
/// Command wiring stays in the consuming app's registry; do not add per-command lambdas here.
/// </para>
/// </summary>
internal static class FreeWAvaloniaRibbonDefinition
{
    private static readonly string[] FontSizes = FreeWRibbonDefinitionData.FontSizes;

    private static readonly string[] FontFamilies = FreeWRibbonDefinitionData.FontFamilies;

    /// <summary>
    /// AV-PICTAB: preset point sizes offered by the Picture / Drawing Format Size combos.
    /// The user can also type an arbitrary value; the combo's free-text is parsed in the command.
    /// </summary>
    private static readonly string[] FloatSizes = FreeWRibbonDefinitionData.FloatSizes;

    /// <summary>
    /// Standard colour palette offered by the Font Color dropdown.
    /// Each entry maps to a distinct command id of the form <c>freew.font-color.*</c>
    /// registered by the consuming app command registry.
    /// Clicking the button opens this flyout; no colour is set until the user picks one,
    /// so the previous selection colour is never silently cleared.
    /// </summary>
    private static RibbonMenu BuildFontColorMenu() =>
        new(FreeWRibbonDefinitionData.FontColors
            .Select(fc => new RibbonMenuItem(fc.Label, new RibbonCommandId(fc.CommandId)))
            .ToArray());

    private static RibbonMenu BuildParagraphShadingMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Yellow", new RibbonCommandId("freew.para-shading.yellow")),
            new("Green", new RibbonCommandId("freew.para-shading.green")),
            new("Cyan", new RibbonCommandId("freew.para-shading.cyan")),
            new("Gold", new RibbonCommandId("freew.para-shading.gold")),
            new("Red", new RibbonCommandId("freew.para-shading.red")),
            new("Gray", new RibbonCommandId("freew.para-shading.gray")),
            new("Light Gray", new RibbonCommandId("freew.para-shading.light-gray")),
            new("Light Yellow", new RibbonCommandId("freew.para-shading.light-yellow")),
            new("Light Blue", new RibbonCommandId("freew.para-shading.light-blue")),
            new("Light Green", new RibbonCommandId("freew.para-shading.light-green")),
            new("Light Peach", new RibbonCommandId("freew.para-shading.light-peach")),
            new("Very Light Gray", new RibbonCommandId("freew.para-shading.very-light-gray")),
            RibbonMenuItem.Separator(),
            new("No Color", new RibbonCommandId("freew.para-shading.none")),
        });

    private static RibbonMenu BuildCharacterShadingMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Yellow", new RibbonCommandId("freew.char-shading.yellow")),
            new("Green", new RibbonCommandId("freew.char-shading.green")),
            new("Cyan", new RibbonCommandId("freew.char-shading.cyan")),
            new("Gold", new RibbonCommandId("freew.char-shading.gold")),
            new("Red", new RibbonCommandId("freew.char-shading.red")),
            new("Gray", new RibbonCommandId("freew.char-shading.gray")),
            new("Light Gray", new RibbonCommandId("freew.char-shading.light-gray")),
            new("Light Yellow", new RibbonCommandId("freew.char-shading.light-yellow")),
            new("Light Blue", new RibbonCommandId("freew.char-shading.light-blue")),
            new("Light Green", new RibbonCommandId("freew.char-shading.light-green")),
            new("Light Peach", new RibbonCommandId("freew.char-shading.light-peach")),
            new("Very Light Gray", new RibbonCommandId("freew.char-shading.very-light-gray")),
            RibbonMenuItem.Separator(),
            new("No Color", new RibbonCommandId("freew.char-shading.none")),
        });

    private static RibbonMenu BuildCharacterBorderMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Black", new RibbonCommandId("freew.char-border.black")),
            new("Red", new RibbonCommandId("freew.char-border.red")),
            new("Blue", new RibbonCommandId("freew.char-border.blue")),
            new("Green", new RibbonCommandId("freew.char-border.green")),
            new("Gold", new RibbonCommandId("freew.char-border.gold")),
            new("Purple", new RibbonCommandId("freew.char-border.purple")),
            new("Gray", new RibbonCommandId("freew.char-border.gray")),
            new("Dark Red", new RibbonCommandId("freew.char-border.dark-red")),
            new("Dark Blue", new RibbonCommandId("freew.char-border.dark-blue")),
            new("Dark Green", new RibbonCommandId("freew.char-border.dark-green")),
            new("Brown", new RibbonCommandId("freew.char-border.brown")),
            new("Dark Gray", new RibbonCommandId("freew.char-border.dark-gray")),
            RibbonMenuItem.Separator(),
            new("No Border", new RibbonCommandId("freew.char-border.none")),
        });

    private static RibbonMenu BuildHighlightMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Black", new RibbonCommandId("freew.highlight.black")),
            new("Dark Gray", new RibbonCommandId("freew.highlight.dark-gray")),
            new("Gray", new RibbonCommandId("freew.highlight.gray")),
            new("Dark Red", new RibbonCommandId("freew.highlight.dark-red")),
            new("Red", new RibbonCommandId("freew.highlight.red")),
            new("Gold", new RibbonCommandId("freew.highlight.gold")),
            new("Yellow", new RibbonCommandId("freew.highlight.yellow")),
            new("Light Green", new RibbonCommandId("freew.highlight.light-green")),
            new("Green", new RibbonCommandId("freew.highlight.green")),
            new("Cyan", new RibbonCommandId("freew.highlight.cyan")),
            new("Blue", new RibbonCommandId("freew.highlight.blue")),
            new("Dark Blue", new RibbonCommandId("freew.highlight.dark-blue")),
            new("Purple", new RibbonCommandId("freew.highlight.purple")),
            new("White", new RibbonCommandId("freew.highlight.white")),
            RibbonMenuItem.Separator(),
            new("No Color", new RibbonCommandId("freew.highlight.none")),
        });

    private static RibbonMenu BuildDisplayForReviewMenu() =>
        new(new RibbonMenuItem[]
        {
            new("All Markup", new RibbonCommandId("freew.display-for-review-all-markup")),
            new("Simple Markup", new RibbonCommandId("freew.display-for-review-simple-markup")),
            new("No Markup", new RibbonCommandId("freew.display-for-review-no-markup")),
            new("Original", new RibbonCommandId("freew.display-for-review-original")),
        });

    private static RibbonMenu BuildShowMarkupMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Insertions and Deletions", new RibbonCommandId("freew.show-markup-insertions-deletions")),
            new("Comments", new RibbonCommandId("freew.show-markup-comments")),
            new("Formatting", new RibbonCommandId("freew.show-markup-formatting")),
            RibbonMenuItem.Separator(),
            new("Show Revisions in Balloons", new RibbonCommandId("freew.show-markup-balloons")),
        });

    private static RibbonMenu BuildReadModeMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Narrow Column Width", new RibbonCommandId("freew.read-mode-column-narrow")),
            new("Default Column Width", new RibbonCommandId("freew.read-mode-column-default")),
            new("Wide Column Width", new RibbonCommandId("freew.read-mode-column-wide")),
            RibbonMenuItem.Separator(),
            new("No Color", new RibbonCommandId("freew.read-mode-color-none")),
            new("Sepia", new RibbonCommandId("freew.read-mode-color-sepia")),
            new("Inverse (Dark Mode)", new RibbonCommandId("freew.read-mode-color-inverse")),
        });

    // AV-PICTAB: wrap-mode menu shared by the Picture / Drawing Format "Wrap Text" dropdown.
    // <paramref name="prefix"/> is "image" or "shape" so the command ids match the WPF host
    // (freew.image-wrap-* / freew.shape-wrap-*).
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

    /// <summary>AV-INSERT: Insert &gt; Table dropdown — common row×column size presets.</summary>
    private static RibbonMenu BuildTableSizeMenu() =>
        new(new RibbonMenuItem[]
        {
            new("2 × 2 Table",       new RibbonCommandId("freew.table-2x2")),
            new("3 × 3 Table",       new RibbonCommandId("freew.table-3x3")),
            new("4 × 4 Table",       new RibbonCommandId("freew.table-4x4")),
            new("5 × 2 Table",       new RibbonCommandId("freew.table-5x2")),
        });

    /// <summary>AV-REF: References &gt; Insert Caption dropdown — Figure / Table caption labels.</summary>
    private static RibbonMenu BuildMarginsMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Normal", new RibbonCommandId("freew.page-margins-normal")),
            new("Narrow", new RibbonCommandId("freew.page-margins-narrow")),
            new("Wide", new RibbonCommandId("freew.page-margins-wide")),
            RibbonMenuItem.Separator(),
            new("Custom Margins...", new RibbonCommandId("freew.custom-margins")),
        });

    private static RibbonMenu BuildPageSizeMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Letter", new RibbonCommandId("freew.page-size-letter")),
            new("A4", new RibbonCommandId("freew.page-size-a4")),
            RibbonMenuItem.Separator(),
            new("More Paper Sizes...", new RibbonCommandId("freew.more-paper-sizes")),
        });

    private static RibbonMenu BuildColumnsMenu() =>
        new(new RibbonMenuItem[]
        {
            new("One", new RibbonCommandId("freew.columns-one")),
            new("Two", new RibbonCommandId("freew.columns-two")),
            new("Three", new RibbonCommandId("freew.columns-three")),
            new("Left", new RibbonCommandId("freew.columns-left")),
            new("Right", new RibbonCommandId("freew.columns-right")),
            RibbonMenuItem.Separator(),
            new("More Columns...", new RibbonCommandId("freew.columns-more")),
        });

    private static RibbonMenu BuildBreaksMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Page Break", new RibbonCommandId("freew.page-break")),
            new("Column Break", new RibbonCommandId("freew.column-break")),
            RibbonMenuItem.Separator(),
            new("Next Page", new RibbonCommandId("freew.section-break-next-page")),
            new("Continuous", new RibbonCommandId("freew.section-break-continuous")),
            new("Even Page", new RibbonCommandId("freew.section-break-even-page")),
            new("Odd Page", new RibbonCommandId("freew.section-break-odd-page")),
        });

    private static RibbonMenu BuildCaptionMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Figure", new RibbonCommandId("freew.insert-caption.figure")),
            new("Table",  new RibbonCommandId("freew.insert-caption.table")),
            new("Equation", new RibbonCommandId("freew.insert-caption.equation")),
        });

    private static RibbonMenu BuildTableOfFiguresMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Figure", new RibbonCommandId("freew.tof.figure")),
            new("Table", new RibbonCommandId("freew.tof.table")),
            new("Equation", new RibbonCommandId("freew.tof.equation")),
        });

    private static RibbonMenu BuildMultilevelListMenu() =>
        new(new RibbonMenuItem[]
        {
            new(FreeWRibbonText.MultilevelPromoteCommand.Label, new RibbonCommandId("freew.multilevel-promote")),
            new(FreeWRibbonText.MultilevelDemoteCommand.Label, new RibbonCommandId("freew.multilevel-demote")),
            new(FreeWRibbonDefinitionData.MultilevelListPresetNames[0], new RibbonCommandId("freew.multilevel-preset-0")),
            new(FreeWRibbonDefinitionData.MultilevelListPresetNames[1], new RibbonCommandId("freew.multilevel-preset-1")),
            new(FreeWRibbonDefinitionData.MultilevelListPresetNames[2], new RibbonCommandId("freew.multilevel-preset-2")),
            new(FreeWRibbonText.MultilevelDefineCommand.Label, new RibbonCommandId("freew.multilevel-define")),
        });

    /// <summary>
    /// AV-STYLES: Home &gt; Styles gallery dropdown — the full built-in style set (paragraph and character
    /// styles), one item per <see cref="BuiltInStyles.Gallery"/> entry. Each item's command id is
    /// <c>freew.style.&lt;id&gt;</c> (matching <see cref="FreeWRibbonDefinitionData.StyleCommandId"/>).
    /// </summary>
    private static RibbonMenu BuildStylesMenu() =>
        new(BuiltInStyles.Gallery
            .Select(d => new RibbonMenuItem(
                d.Type == StyleType.Character ? $"{d.Name}  (a)" : d.Name,
                new RibbonCommandId(FreeWRibbonDefinitionData.StyleCommandId(d.Id))))
            .ToArray());

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

    /// <summary>AV-INSERT2: Insert &gt; Cover Page gallery — the three built-in cover-page presets.</summary>
    private static RibbonMenu BuildCoverPageMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Default", new RibbonCommandId("freew.cover-page.default")),
            new("Banded",  new RibbonCommandId("freew.cover-page.banded")),
            new("Motion",  new RibbonCommandId("freew.cover-page.motion")),
        });

    /// <summary>AV-INSERT2: Insert &gt; Drop Cap menu matching the WPF host routes.</summary>
    private static RibbonMenu BuildDropCapMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Dropped",   new RibbonCommandId("freew.drop-cap-dropped")),
            new("In Margin", new RibbonCommandId("freew.drop-cap-in-margin")),
            new("None (Remove)", new RibbonCommandId("freew.drop-cap-none")),
            RibbonMenuItem.Separator(),
            new("Drop Cap Options...", new RibbonCommandId("freew.drop-cap-options")),
        });

    /// <summary>
    /// AV-INSERT2: Insert &gt; Quick Parts menu — Word document-property fields, a Date field, and a
    /// free-text snippet (opens a dialog). Command ids match the registry wiring.
    /// </summary>
    private static RibbonMenu BuildQuickPartsMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Document Property — Title",   new RibbonCommandId("freew.quick-parts.title")),
            new("Document Property — Author",  new RibbonCommandId("freew.quick-parts.author")),
            new("Document Property — Subject", new RibbonCommandId("freew.quick-parts.subject")),
            new("Document Property — Keywords", new RibbonCommandId("freew.quick-parts.keywords")),
            new("Document Property — Comments", new RibbonCommandId("freew.quick-parts.comments")),
            new("Field — Date",                new RibbonCommandId("freew.quick-parts.date")),
            RibbonMenuItem.Separator(),
            new("Insert Snippet…",             new RibbonCommandId("freew.quick-parts.snippet")),
            new("Field…",                      new RibbonCommandId("freew.field")),
            RibbonMenuItem.Separator(),
            new("Save Selection to Quick Part Gallery…", new RibbonCommandId("freew.save-quickpart")),
            new("Building Blocks Organizer…",  new RibbonCommandId("freew.building-blocks-organizer")),
        });

    /// <summary>
    /// AV-INSERT2: Insert &gt; Equation menu — a default sample (E=mc²) plus a few common OMML structures.
    /// Each preset maps to a <c>freew.equation.*</c> command that inserts the corresponding equation.
    /// </summary>
    private static RibbonMenu BuildEquationMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Insert New Equation", new RibbonCommandId("freew.equation.default")),
            RibbonMenuItem.Separator(),
            new("Fraction  a/b",       new RibbonCommandId("freew.equation.fraction")),
            new("Script  xⁿ",          new RibbonCommandId("freew.equation.script")),
            new("Radical  √x",         new RibbonCommandId("freew.equation.radical")),
            new("Nth Root",            new RibbonCommandId("freew.equation.nthroot")),
            new("Integral  ∫",         new RibbonCommandId("freew.equation.integral")),
            new("Summation  ∑",        new RibbonCommandId("freew.equation.summation")),
            new("Product",             new RibbonCommandId("freew.equation.product")),
            RibbonMenuItem.Separator(),
            new("Accent",              new RibbonCommandId("freew.equation.accent")),
            new("Bar",                 new RibbonCommandId("freew.equation.bar")),
            new("Bracket",             new RibbonCommandId("freew.equation.bracket")),
            new("Matrix",              new RibbonCommandId("freew.equation.matrix")),
            new("Function",            new RibbonCommandId("freew.equation.func")),
            new("Group Character",     new RibbonCommandId("freew.equation.groupchr")),
        });

    /// <summary>
    /// AV-DESIGN: Design &gt; Themes dropdown — one item per built-in <see cref="DocumentTheme.Catalog"/>
    /// entry. Command ids are <c>freew.theme.&lt;name&gt;</c> (lower-case), matching the registry wiring.
    /// </summary>
    private static RibbonMenu BuildThemeMenu() =>
        new(DocumentTheme.Catalog
            .Select(t => new RibbonMenuItem(t.Name,
                new RibbonCommandId($"freew.theme.{t.Name.ToLowerInvariant()}")))
            .ToArray());

    /// <summary>AV-DESIGN: Design &gt; Colors dropdown — one item per theme palette.</summary>
    private static RibbonMenu BuildThemeColorsMenu() =>
        new(DocumentTheme.Catalog
            .Select(t => new RibbonMenuItem(t.Name,
                new RibbonCommandId($"freew.theme-colors.{t.Name.ToLowerInvariant()}")))
            .Concat([RibbonMenuItem.Separator(), new RibbonMenuItem("Customize Colors...", new RibbonCommandId("freew.customize-colors"))])
            .ToArray());

    /// <summary>AV-DESIGN: Design &gt; Fonts dropdown — one item per <see cref="DocumentFontSet.Catalog"/> entry.</summary>
    private static RibbonMenu BuildThemeFontsMenu() =>
        new(DocumentFontSet.Catalog
            .Select(f => new RibbonMenuItem($"{f.Name}  ({f.HeadingFont} / {f.BodyFont})",
                new RibbonCommandId($"freew.theme-fonts.{f.Name.ToLowerInvariant()}")))
            .Concat([RibbonMenuItem.Separator(), new RibbonMenuItem("Customize Fonts...", new RibbonCommandId("freew.customize-fonts"))])
            .ToArray());

    /// <summary>AV-DESIGN: Design &gt; Paragraph Spacing dropdown — one item per spacing preset.</summary>
    private static RibbonMenu BuildParaSpacingMenu() =>
        new(DocumentParagraphSpacingSet.Catalog
            .Select(s => new RibbonMenuItem(s.Name,
                new RibbonCommandId($"freew.para-spacing.{ParaSpacingId(s.Name)}")))
            .Concat(new[]
            {
                RibbonMenuItem.Separator(),
                new RibbonMenuItem("Custom Paragraph Spacing...", new RibbonCommandId("freew.custom-paragraph-spacing")),
            })
            .ToArray());

    private static RibbonMenu BuildLineNumbersMenu() =>
        new(new RibbonMenuItem[]
        {
            new("None", new RibbonCommandId("freew.line-numbers-none")),
            new("Continuous", new RibbonCommandId("freew.line-numbers-continuous")),
            new("Restart Each Page", new RibbonCommandId("freew.line-numbers-restart-page")),
            new("Restart Each Section", new RibbonCommandId("freew.line-numbers-restart-section")),
            RibbonMenuItem.Separator(),
            new("Line Numbering Options...", new RibbonCommandId("freew.line-numbers-options")),
        });

    private static RibbonMenu BuildHyphenationMenu() =>
        new(new RibbonMenuItem[]
        {
            new("None", new RibbonCommandId("freew.hyphenation-none")),
            new("Automatic", new RibbonCommandId("freew.hyphenation-auto")),
            new("Manual", new RibbonCommandId("freew.hyphenation-manual")),
            RibbonMenuItem.Separator(),
            new("Hyphenation Options...", new RibbonCommandId("freew.hyphenation-options")),
        });

    private static RibbonMenu BuildEffectsMenu() => FreeWContextMenuPlanner.BuildEffects();

    private static RibbonMenu BuildTableStylesMenu() => FreeWContextMenuPlanner.BuildTableStyles();

    /// <summary>Normalises a spacing-set display name to a stable command-id suffix (e.g. "No Paragraph Space" → "no-paragraph-space").</summary>
    private static string ParaSpacingId(string name) => FreeWRibbonDefinitionData.ParaSpacingId(name);

    /// <summary>
    /// AV-DESIGN: Design &gt; Page Color swatch palette + No Color. Command ids are
    /// <c>freew.page-color.&lt;name&gt;</c>; "No Color" clears the background.
    /// </summary>
    private static RibbonMenu BuildPageColorMenu() =>
        new(FreeWRibbonDefinitionData.PageColors
            .Select(pc => new RibbonMenuItem(pc.Label, new RibbonCommandId(pc.CommandId)))
            .ToArray());

    /// <summary>
    /// AV-DESIGN: Design &gt; Watermark gallery — the built-in presets (CONFIDENTIAL / DRAFT / …), a custom
    /// opener, and a Remove entry. Command ids are <c>freew.watermark.&lt;preset&gt;</c>.
    /// </summary>
    private static RibbonMenu BuildWatermarkMenu() =>
        new(new RibbonMenuItem[]
        {
            new("CONFIDENTIAL", new RibbonCommandId("freew.watermark.confidential")),
            new("DO NOT COPY",  new RibbonCommandId("freew.watermark.do-not-copy")),
            new("DRAFT",        new RibbonCommandId("freew.watermark.draft")),
            new("URGENT",       new RibbonCommandId("freew.watermark.urgent")),
            RibbonMenuItem.Separator(),
            new("Custom Watermark…", new RibbonCommandId("freew.watermark.custom")),
            new("Remove Watermark",  new RibbonCommandId("freew.watermark.none")),
        });

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
            .Tab("home", FreeWRibbonText.HomeTab.Label, FreeWRibbonText.HomeTab.KeyTip, tab =>
            {
                tab.Group("clipboard", FreeWRibbonText.ClipboardGroup.Label, FreeWRibbonText.ClipboardGroup.KeyTip, 100, g =>
                {
                    g.Button("freew.cut", FreeWRibbonText.CutCommand.Label, b => b with
                    {
                        KeyTip = FreeWRibbonText.CutCommand.KeyTip
                    });
                    g.Button("freew.copy", FreeWRibbonText.CopyCommand.Label, b => b with
                    {
                        KeyTip = FreeWRibbonText.CopyCommand.KeyTip
                    });
                    g.Button("freew.paste", FreeWRibbonText.PasteCommand.Label, b => b with
                    {
                        KeyTip = FreeWRibbonText.PasteCommand.KeyTip
                    });
                    g.Button("freew.format-painter", FreeWRibbonText.FormatPainterCommand.Label, b => b with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.FormatPainter),
                        KeyTip = FreeWRibbonText.FormatPainterCommand.KeyTip
                    });
                    g.Icon("freew.paste-plain", FreeWRibbonText.PasteTextOnlyCommand.Label, RibbonCommandIconKind.Paste);
                    g.Icon("freew.paste-merge", FreeWRibbonText.PasteMergeFormattingCommand.Label, RibbonCommandIconKind.Paste);
                    g.Icon("freew.paste-special", FreeWRibbonText.PasteSpecialCommand.Label, RibbonCommandIconKind.Paste);
                });
                tab.Group("font", FreeWRibbonText.FontGroup.Label, FreeWRibbonText.FontGroup.KeyTip, 90, g =>
                {
                    g.ComboBox("freew.font-family", FreeWRibbonText.FontFamilyCommand.Label, c => c with { Items = FontFamilies, Width = 128 });
                    g.ComboBox("freew.font-size",   FreeWRibbonText.FontSizeCommand.Label, c => c with { Items = FontSizes,    Width = 64 });
                    g.Toggle("freew.bold",           FreeWRibbonText.BoldCommand.Label, b => b with { KeyTip = FreeWRibbonText.BoldCommand.KeyTip });
                    g.Toggle("freew.italic",          FreeWRibbonText.ItalicCommand.Label, b => b with { KeyTip = FreeWRibbonText.ItalicCommand.KeyTip });
                    g.Toggle("freew.underline",       FreeWRibbonText.UnderlineCommand.Label, b => b with { KeyTip = FreeWRibbonText.UnderlineCommand.KeyTip });
                    g.Toggle("freew.strikethrough",   FreeWRibbonText.StrikethroughCommand.Label);
                    g.Toggle("freew.superscript",     FreeWRibbonText.SuperscriptCompactCommand.Label);
                    g.Toggle("freew.subscript",       FreeWRibbonText.SubscriptCompactCommand.Label);
                    g.Toggle("freew.smallcaps",       FreeWRibbonText.SmallCapsCommand.Label);
                    g.Toggle("freew.allcaps",         FreeWRibbonText.AllCapsCommand.Label);
                    g.Dropdown("freew.highlight",     FreeWRibbonText.HighlightCompactCommand.Label, BuildHighlightMenu(), d => d with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Highlight)
                    });
                    g.Dropdown("freew.char-border",   FreeWRibbonText.CharacterBorderCommand.Label, BuildCharacterBorderMenu(), d => d with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border)
                    });
                    g.Dropdown("freew.char-shading",  FreeWRibbonText.CharacterShadingCommand.Label, BuildCharacterShadingMenu(), d => d with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Fill)
                    });
                    g.Button("freew.grow-font",       FreeWRibbonText.GrowFontCompactCommand.Label);
                    g.Button("freew.shrink-font",     FreeWRibbonText.ShrinkFontCompactCommand.Label);
                    g.Button("freew.clear-formatting", FreeWRibbonText.ClearFormattingCompactCommand.Label);
                    g.Dropdown("freew.font-color", FreeWRibbonText.FontColorDropdownCommand.Label, BuildFontColorMenu());
                    g.Button("freew.change-case",     FreeWRibbonText.ChangeCaseCompactCommand.Label);
                    g.Button("freew.font-dialog",     FreeWRibbonText.FontDialogCommand.Label);
                });
                tab.Group("paragraph", FreeWRibbonText.ParagraphGroup.Label, null, 80, g =>
                {
                    g.Toggle("freew.bullets",           FreeWRibbonText.BulletsCommand.Label);
                    g.Toggle("freew.numbering",         FreeWRibbonText.NumberingCommand.Label);
                    g.Dropdown("freew.multilevel-list", FreeWRibbonText.MultilevelListCommand.Label, BuildMultilevelListMenu(), d => d with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.MultilevelList)
                    });
                    g.Button("freew.indent-decrease",   "Decrease Indent", b => b with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.IndentDecrease)
                    });
                    g.Button("freew.indent-increase",   "Increase Indent", b => b with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.IndentIncrease)
                    });
                    g.Button("freew.align-left",        "Left");
                    g.Button("freew.align-center",      "Center");
                    g.Button("freew.align-right",       "Right");
                    g.Button("freew.align-justify",     "Justify");
                    g.Button("freew.sort", "Sort", b => b with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sort)
                    });
                    g.ComboBox("freew.line-spacing", "Line and Paragraph Spacing", c => c with
                    {
                        Items = new[] { "1.0", "1.15", "1.5", "2.0" },
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.LineSpacing),
                        Width = 52
                    });
                    g.Dropdown("freew.para-shading", "Shading", BuildParagraphShadingMenu(), d => d with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Fill)
                    });
                    g.Button("freew.para-border", "Borders", b => b with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border)
                    });
                    g.Button("freew.borders-shading", "Borders and Shading...", b => b with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border, RibbonCommandIconAccent.Border)
                    });
                    g.Button("freew.space-before-toggle", "Add Space Before Paragraph", b => b with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.SpaceBefore)
                    });
                    g.Button("freew.space-after-toggle", "Add Space After Paragraph", b => b with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.SpaceAfter)
                    });
                    g.Button("freew.keep-with-next", "Keep with Next", b => b with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextFunction)
                    });
                    g.Button("freew.keep-lines", "Keep Lines Together", b => b with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextFunction)
                    });
                    g.Button("freew.widow-control", "Widow/Orphan Control", b => b with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextFunction)
                    });
                    g.Button("freew.tabs-dialog", "Tabs", b => b with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Ruler)
                    });
                    g.Toggle("freew.formatting-marks", "Show Formatting Marks", t => t with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.FormattingMarks)
                    });
                    g.Button("freew.paragraph-dialog",  "Paragraph…");
                });
                tab.Group("styles", "Styles", null, 75, g =>
                {
                    // Quick-style buttons (kept from the A1 wave; now model-backed via ApplyNamedStyle).
                    g.Button("freew.style-normal",   "Normal");
                    g.Button("freew.style-heading1", "Heading 1");
                    g.Button("freew.style-heading2", "Heading 2");
                    g.Button("freew.style-heading3", "Heading 3");
                    g.Button("freew.style-title",    "Title");
                    // AV-STYLES: full built-in style gallery dropdown + clear-style.
                    g.Dropdown("freew.styles-gallery", "Styles", BuildStylesMenu());
                    g.Button("freew.style-clear", "Clear Style");
                    g.Button("freew.new-style", "New Style", b => b with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Insert)
                    });
                    g.Button("freew.manage-styles", "Manage Styles", b => b with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Effects)
                    });
                });
                tab.Group("editing", "Editing", null, 70, g =>
                {
                    g.Button("freew.undo",              "Undo");
                    g.Button("freew.redo",              "Redo");
                    g.Button("freew.find",              "Find");
                    g.Button("freew.replace",           "Replace");
                    g.Button("freew.select",            "Select");
                });
            })
            .Tab("insert", "Insert", "I", tab =>
            {
                // AV-INSERT: Insert-tab depth.
                tab.Group("pages", "Pages", null, 100, g =>
                {
                    // AV-INSERT2: Cover Page (gallery of presets) + Page Break.
                    g.Dropdown("freew.cover-page", "Cover Page", BuildCoverPageMenu());
                    g.Button("freew.blank-page", "Blank Page");
                    g.Button("freew.page-break", "Page Break");
                    g.Button("freew.horizontal-rule", "Horizontal Rule");
                });
                tab.Group("tables", "Tables", null, 98, g =>
                {
                    g.Button("freew.insert-table", "Table");
                    g.Dropdown("freew.table", "Table…", BuildTableSizeMenu());
                });
                tab.Group("illustrations", "Illustrations", null, 96, g =>
                {
                    g.Button("freew.picture",  "Picture");
                    g.Button("freew.shape",    "Shape");
                    g.Button("freew.smartart", "SmartArt");
                    g.Button("freew.chart",    "Chart");
                    g.Dropdown("freew.screenshot", "Screenshot", new RibbonMenu(new[]
                    {
                        new RibbonMenuItem("Screen Clipping", new RibbonCommandId("freew.screen-clipping")),
                    }));
                    g.Button("freew.insert-icon", "Icons");
                    g.Button("freew.text-box", "Text Box");
                });
                // AV-INSERT2: Links group — Hyperlink + Bookmark.
                tab.Group("links", "Links", null, 95, g =>
                {
                    g.Button("freew.hyperlink", "Hyperlink");
                    g.Button("freew.insert-hyperlink", "Hyperlink");
                    g.Button("freew.edit-hyperlink", "Edit Hyperlink");
                    g.Button("freew.remove-hyperlink", "Remove Hyperlink");
                    g.Button("freew.hyperlink-tooltip", "ScreenTip");
                    g.Button("freew.bookmark", "Bookmark");
                    g.Button("freew.insert-bookmark",  "Bookmark");
                    g.Button("freew.link-bookmark", "Link to Bookmark");
                    g.Button("freew.bookmark-manager", "Bookmark Manager");
                });
                tab.Group("header-footer", "Header & Footer", null, 94, g =>
                {
                    g.Button("freew.header", "Header");
                    g.Button("freew.footer", "Footer");
                    g.Dropdown("freew.page-number", "Page Number", new RibbonMenu(new[]
                    {
                        new RibbonMenuItem("Top of Page", new RibbonCommandId("freew.page-number-top")),
                        new RibbonMenuItem("Bottom of Page", new RibbonCommandId("freew.page-number-bottom")),
                        new RibbonMenuItem("Current Position", new RibbonCommandId("freew.page-number-current")),
                        RibbonMenuItem.Separator(),
                        new RibbonMenuItem("Format Page Numbers...", new RibbonCommandId("freew.page-number-format")),
                    }));
                });
                // AV-INSERT2: Text group — Quick Parts (document-property fields + snippet), Drop Cap,
                // Text from File.
                tab.Group("text", "Text", null, 93, g =>
                {
                    g.Dropdown("freew.quick-parts", "Quick Parts", BuildQuickPartsMenu());
                    g.Dropdown("freew.drop-cap",    "Drop Cap",    BuildDropCapMenu());
                    g.Button("freew.insert-file", "Text from File");
                    g.Button("freew.wordart", "WordArt");
                    g.Button("freew.datetime", "Date & Time");
                    g.Button("freew.field", "Field", b => b with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Field)
                    });
                    g.Button("freew.update-fields", "Update Fields");
                    g.Button("freew.toggle-field-codes", "Toggle Field Codes");
                    g.Button("freew.object", "Object");
                    g.Button("freew.save-quickpart", "Save Selection", b => b with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.QuickParts)
                    });
                    g.Button("freew.building-blocks-organizer", "Building Blocks Organizer", b => b with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.QuickParts)
                    });
                });
                tab.Group("symbols", FreeWRibbonText.SymbolsGroup.Label, null, 92, g =>
                {
                    g.Button("freew.symbol", FreeWRibbonText.SymbolCommand.Label);
                    // AV-INSERT2: Equation — default (E=mc²) opener + a few common OMML presets.
                    g.Dropdown("freew.equation", "Equation", BuildEquationMenu());
                });
            })
            .Tab("layout", "Layout", "L", tab =>
            {
                // AV-PAGE: page-setup group — dialog launcher + quick orientation/margins/size.
                tab.Group("page-setup", "Page Setup", null, 100, g =>
                {
                    g.Dropdown("freew.margins", "Margins", BuildMarginsMenu());
                    g.Button("freew.orientation", "Orientation");
                    g.Dropdown("freew.size", "Size", BuildPageSizeMenu());
                    g.Dropdown("freew.columns", "Columns", BuildColumnsMenu());
                    g.Dropdown("freew.breaks", "Breaks", BuildBreaksMenu());
                    g.Dropdown("freew.line-numbers", "Line Numbers", BuildLineNumbersMenu());
                    g.Dropdown("freew.hyphenation", "Hyphenation", BuildHyphenationMenu());
                    g.Toggle("freew.different-first-page", "Different First Page");
                    g.Button("freew.page-valign", "Vertical Align");
                    g.Button("freew.page-setup", "Page Setup...");
                });
                tab.Group("paragraph", FreeWRibbonText.ParagraphGroup.Label, null, 92, g =>
                {
                    g.Button("freew.indent-decrease", "Decrease Indent");
                    g.Button("freew.indent-increase", "Increase Indent");
                    g.ComboBox("freew.line-spacing", "Line and Paragraph Spacing", c => c with
                    {
                        Items = new[] { "1.0", "1.15", "1.5", "2.0" },
                        Width = 52
                    });
                    g.ComboBox("freew.indent-left", "Indent Left", c => c with
                    {
                        Items = new[] { "0", "18", "36", "54", "72" },
                        Width = 52
                    });
                    g.ComboBox("freew.indent-right", "Indent Right", c => c with
                    {
                        Items = new[] { "0", "18", "36", "54", "72" },
                        Width = 52
                    });
                    g.Button("freew.space-before-toggle", "Add Space Before Paragraph");
                    g.Button("freew.space-after-toggle", "Add Space After Paragraph");
                    g.ComboBox("freew.space-before", "Spacing Before", c => c with
                    {
                        Items = new[] { "0", "6", "12", "18", "24" },
                        Width = 52
                    });
                    g.ComboBox("freew.space-after", "Spacing After", c => c with
                    {
                        Items = new[] { "0", "6", "8", "12", "18", "24" },
                        Width = 52
                    });
                    g.Button("freew.paragraph-dialog", "Paragraph Settings");
                    g.Button("freew.tabs-dialog", "Tabs");
                });
                tab.Group("data", "Data", null, 95, g =>
                {
                    g.Button("freew.text-to-table", "Text to Table");
                    g.Button("freew.table-to-text", "Table to Text");
                });
                tab.Group("preview", "Preview", null, 90, g =>
                {
                    g.Button("freew.print-preview", "Print Preview");
                });
            })
            .Tab("design", "Design", "G", tab =>
            {
                // AV-DESIGN: Document Formatting — Themes + Colors / Fonts / Paragraph Spacing galleries.
                tab.Group("themes", "Themes", null, 110, g =>
                {
                    g.Dropdown("freew.theme", "Themes", BuildThemeMenu());
                });
                tab.Group("document-formatting", "Document Formatting", null, 100, g =>
                {
                    g.Dropdown("freew.theme-colors", "Colors", BuildThemeColorsMenu());
                    g.ComboBox("freew.style-set", "Style Sets", c => c with
                    {
                        Items = DocumentStyleSet.Catalog.Select(s => s.Name).ToArray(),
                        Width = 128
                    });
                    g.Button("freew.reset-style-set", "Reset to Default Style Set", b => b with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh)
                    });
                    g.Dropdown("freew.theme-fonts",  "Fonts",  BuildThemeFontsMenu());
                    g.Dropdown("freew.para-spacing", "Paragraph Spacing", BuildParaSpacingMenu());
                    g.Dropdown("freew.theme-effects", "Effects", BuildEffectsMenu());
                });
                // AV-DESIGN: Page Background — Watermark, Page Color, Page Borders.
                tab.Group("page-background", FreeWRibbonText.PageBackgroundGroup.Label, null, 90, g =>
                {
                    g.Dropdown("freew.watermark",  FreeWRibbonText.WatermarkCommand.Label,  BuildWatermarkMenu());
                    g.Dropdown("freew.page-color", FreeWRibbonText.PageColorCommand.Label, BuildPageColorMenu());
                    g.Button("freew.page-borders", FreeWRibbonText.PageBordersCommand.Label);
                });
            })
            .Tab("view", "View", "V", tab =>
            {
                tab.Group("views", "Views", null, 110, g =>
                {
                    g.Dropdown("freew.read-mode", "Read Mode", BuildReadModeMenu(), d => d with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.ReadMode)
                    });
                    g.Button("freew.print-layout", "Print Layout");
                    g.Button("freew.web-layout",   "Web Layout");
                    g.Toggle("freew.outline-view", "Outline");
                    g.Button("freew.draft-view",   "Draft");
                    g.Toggle("freew.paged-edit-view", "Page Edit");
                });
                tab.Group("show", "Show", null, 100, g =>
                {
                    // AV-VIEW: layout gridlines + ruler toggles (DocumentView render chrome).
                    g.Toggle("freew.ruler",             "Ruler");
                    g.Toggle("freew.gridlines",         "Gridlines");
                    g.Toggle("freew.nav-pane",          "Navigation Pane");
                    // AV-VIEW: surface the Reviewing Pane toggle on View as well (also on Review tab).
                    g.Toggle("freew.reviewing-pane",    "Reviewing Pane");
                    g.Toggle("freew.reveal-formatting", "Reveal Formatting");
                });
                tab.Group("zoom", "Zoom", null, 90, g =>
                {
                    // AV-VIEW: full Zoom dialog (presets + custom %) alongside the quick controls.
                    g.Button("freew.zoom-dialog", "Zoom");
                    g.Button("freew.zoom-in",  "Zoom In");
                    g.Button("freew.zoom-out", "Zoom Out");
                    g.Button("freew.zoom-100", "100%");
                    g.Button("freew.zoom-one-page", "One Page");
                    g.Button("freew.zoom-page-width", "Page Width");
                    g.Toggle("freew.zoom-multiple-pages", "Multiple Pages");
                    g.Toggle("freew.zoom-side-to-side", "Side to Side");
                });
                // AV-VIEW: Window group — new window, Arrange All, and split.
                tab.Group("window", "Window", null, 80, g =>
                {
                    g.Button("freew.new-window", "New Window");
                    g.Button("freew.arrange-all", "Arrange All");
                    g.Toggle("freew.split",      "Split");
                });
            })
            .Tab("review", "Review", "R", tab =>
            {
                // AV-REVIEW: Proofing group — word count dialog.
                tab.Group("proofing", "Proofing", null, 110, g =>
                {
                    g.Button("freew.statistics", "Word Count");
                    g.Toggle("freew.spellcheck-toggle", "Spelling & Grammar");
                    g.Button("freew.add-to-dictionary", "Add to Dictionary");
                    g.Button("freew.thesaurus", "Thesaurus");
                    g.Button("freew.set-proofing-language", "Set Proofing Language");
                });
                tab.Group("speech", "Speech", null, 105, g =>
                {
                    g.Toggle("freew.read-aloud", "Read Aloud");
                });
                // AV-REVIEW: Accessibility group — backed by the same report flow as Backstage safety.
                tab.Group("accessibility", "Accessibility", null, 92, g =>
                {
                    g.Button("freew.check-accessibility", "Check Accessibility");
                });
                // AV-REVIEW: Comments group — new / delete review comment.
                tab.Group("comments", "Comments", null, 100, g =>
                {
                    g.Button("freew.new-comment",    "New Comment");
                    g.Button("freew.delete-comment", "Delete");
                    g.Button("freew.previous-comment", "Previous");
                    g.Button("freew.next-comment", "Next");
                    g.Button("freew.reply-comment", "Reply");
                    g.Button("freew.resolve-comment", "Resolve");
                    g.Button("freew.show-comments", "Show Comments");
                });
                // AV-REVIEW: Tracking group — Track Changes toggle + reviewing pane.
                tab.Group("tracking", "Tracking", null, 90, g =>
                {
                    g.Toggle("freew.track-changes", "Track Changes");
                    g.Toggle("freew.track-formatting", "Track Formatting");
                    g.Toggle("freew.reviewing-pane", "Reviewing Pane");
                    g.Dropdown("freew.display-for-review", "All Markup", BuildDisplayForReviewMenu());
                    g.Dropdown("freew.show-markup", "Show Markup", BuildShowMarkupMenu());
                });
                // AV-REVIEW: Changes group — accept / reject (current + all).
                tab.Group("changes", "Changes", null, 80, g =>
                {
                    g.Button("freew.accept-this",   "Accept");
                    g.Button("freew.accept-all",    "Accept All");
                    g.Button("freew.reject-this",   "Reject");
                    g.Button("freew.reject-all",    "Reject All");
                    g.Button("freew.previous-change", "Previous");
                    g.Button("freew.next-change", "Next");
                });
                tab.Group("compare", "Compare", null, 78, g =>
                {
                    g.Button("freew.compare", "Compare");
                    g.Button("freew.combine", "Combine");
                });
                // AV-REVIEW: Protect and Inspect groups are wired through host callbacks to the existing
                // MainWindow/Backstage safety flows.
                tab.Group("protect", "Protect", null, 85, g =>
                {
                    g.Toggle("freew.mark-as-final",    "Mark as Final");
                    g.Toggle("freew.restrict-editing", "Restrict Editing");
                });
                tab.Group("inspect", "Inspect", null, 75, g =>
                {
                    g.Button("freew.inspect-document", "Inspect Document");
                });
            })
            .AddDeveloperTab(capabilities)
            .Tab("references", "References", "S", tab =>
            {
                // AV-REF: References-tab depth — TOC, footnotes/endnotes, captions, cross-ref, citations.
                tab.Group("toc", "Table of Contents", null, 110, g =>
                {
                    g.Button("freew.toc", "Table of Contents");
                    g.Button("freew.toc-refresh", "Update Table");
                });
                tab.Group("footnotes", "Footnotes", null, 100, g =>
                {
                    g.Button("freew.footnote", "Insert Footnote");
                    g.Button("freew.endnote",  "Insert Endnote");
                    g.Button("freew.next-footnote", "Next Footnote");
                    g.Button("freew.previous-footnote", "Previous Footnote");
                    g.Button("freew.next-endnote", "Next Endnote");
                    g.Button("freew.previous-endnote", "Previous Endnote");
                    g.Button("freew.show-notes", "Show Notes");
                    g.Button("freew.footnote-endnote-options", "Footnote/Endnote Options...");
                });
                tab.Group("citations", "Citations & Bibliography", null, 90, g =>
                {
                    g.Button("freew.citation",     "Insert Citation");
                    g.Button("freew.manage-sources", "Manage Sources");
                    g.ComboBox("freew.citation-style", "Style", c => c with
                    {
                        Items = FreeWRibbonDefinitionData.CitationStyleNames,
                        Width = 90
                    });
                    g.Button("freew.bibliography", "Bibliography");
                });
                tab.Group("captions", "Captions", null, 80, g =>
                {
                    g.Dropdown("freew.caption", "Insert Caption", BuildCaptionMenu());
                    g.Dropdown("freew.tof", "Insert Table of Figures", BuildTableOfFiguresMenu());
                    g.Button("freew.tof-refresh", "Update Table");
                    g.Button("freew.cross-reference", "Cross-reference");
                });
                tab.Group("index", "Index", null, 70, g =>
                {
                    g.Button("freew.index-mark", "Mark Entry");
                    g.Button("freew.index-insert", "Insert Index");
                    g.Button("freew.index-refresh", "Update Index");
                });
                tab.Group("authorities", "Table of Authorities", null, 60, g =>
                {
                    g.Button("freew.mark-citation", "Mark Citation");
                    g.Button("freew.table-of-authorities", "Insert Table of Authorities");
                    g.Button("freew.table-of-authorities-refresh", "Update Table");
                });
            })
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
