using System.Collections.Generic;
using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Host;

/// <summary>
/// Installs FreeW's app-local SVG loader and supplies geometry fallbacks for menu or legacy command ids
/// that are not controls in the current WPF ribbon definition. Control icons come directly from definition
/// metadata and must not be repeated here.
/// </summary>
internal static class FreeWRibbonIcons
{
    /// <summary>Installs FreeW's SVG loader and legacy command-id to glyph resolver.</summary>
    public static void Install()
    {
        Free.Shared.Ribbon.Wpf.RibbonIconFactory.CommandIconElementResolver = RibbonIconFactory.TryCreateCommandIcon;
        Free.Shared.Ribbon.Wpf.RibbonIconFactory.CommandIconKindResolver = Resolve;
    }

    public static RibbonCommandIconKind? Resolve(string commandId) =>
        FallbackMap.TryGetValue(commandId, out var kind) ? kind : null;

    internal static IReadOnlyDictionary<string, RibbonCommandIconKind> Fallbacks => FallbackMap;

    private static readonly IReadOnlyDictionary<string, RibbonCommandIconKind> FallbackMap =
        new Dictionary<string, RibbonCommandIconKind>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["freew.multilevel-demote"] = RibbonCommandIconKind.IndentIncrease,
            ["freew.multilevel-promote"] = RibbonCommandIconKind.IndentDecrease,

            ["freew.image-wrap-inline"] = RibbonCommandIconKind.Wrap,
            ["freew.image-wrap-square"] = RibbonCommandIconKind.Wrap,
            ["freew.image-wrap-tight"] = RibbonCommandIconKind.Wrap,
            ["freew.image-wrap-top-bottom"] = RibbonCommandIconKind.Wrap,
            ["freew.image-wrap-behind"] = RibbonCommandIconKind.Wrap,
            ["freew.image-wrap-front"] = RibbonCommandIconKind.Wrap,
            ["freew.shape-rectangle"] = RibbonCommandIconKind.Rectangle,
            ["freew.shape-rounded"] = RibbonCommandIconKind.Rectangle,
            ["freew.shape-ellipse"] = RibbonCommandIconKind.Ellipse,
            ["freew.screen-clipping"] = RibbonCommandIconKind.Picture,

            ["freew.shape-change-rectangle"] = RibbonCommandIconKind.Rectangle,
            ["freew.shape-change-rounded"] = RibbonCommandIconKind.Rectangle,
            ["freew.shape-change-ellipse"] = RibbonCommandIconKind.Ellipse,
            ["freew.shape-fill-no-fill"] = RibbonCommandIconKind.Fill,
            ["freew.shape-outline-no-outline"] = RibbonCommandIconKind.Border,
            ["freew.shape-outline-solid"] = RibbonCommandIconKind.Border,
            ["freew.shape-outline-dash"] = RibbonCommandIconKind.Border,
            ["freew.shape-outline-dot"] = RibbonCommandIconKind.Border,
            ["freew.shape-text-horizontal"] = RibbonCommandIconKind.TextBox,
            ["freew.shape-text-rotate90"] = RibbonCommandIconKind.Rotate,
            ["freew.shape-text-rotate270"] = RibbonCommandIconKind.Rotate,
            ["freew.shape-fill-gradient-blue"] = RibbonCommandIconKind.Fill,
            ["freew.shape-fill-gradient-orange"] = RibbonCommandIconKind.Fill,
            ["freew.shape-fill-pattern-diag"] = RibbonCommandIconKind.Fill,
            ["freew.shape-effects-none"] = RibbonCommandIconKind.Effects,
            ["freew.shape-effect-shadow"] = RibbonCommandIconKind.Effects,
            ["freew.shape-effect-glow"] = RibbonCommandIconKind.Effects,
            ["freew.shape-effect-soft-edge"] = RibbonCommandIconKind.Effects,
            ["freew.shape-effect-reflection"] = RibbonCommandIconKind.Effects,
            ["freew.shape-effect-bevel"] = RibbonCommandIconKind.Effects,
            ["freew.wordart-style-fill-blue"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-style-gradient"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-style-outline"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-style-shadow"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-style-fill-gold"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-style-fill-white"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-style-grad-multi"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-style-chrome-one"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-style-chrome-two"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-style-shadow-orange"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-style-glow-blue"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-style-glow-gold"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-style-reflection"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-style-bevel"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-style-pattern"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-warp-none"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-warp-arch-up"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-warp-arch-down"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-warp-circle"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-warp-button"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-warp-wave1"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-warp-wave2"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-warp-inflate"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-warp-deflate"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-warp-inflate-bottom"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-warp-chevron-up"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-warp-chevron-down"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-warp-fade-right"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-warp-fade-left"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-warp-slant-up"] = RibbonCommandIconKind.WordArt,
            ["freew.wordart-warp-slant-down"] = RibbonCommandIconKind.WordArt,

            ["freew.chart-type-bar"] = RibbonCommandIconKind.ChartColumn,
            ["freew.chart-type-line"] = RibbonCommandIconKind.ChartColumn,
            ["freew.chart-type-pie"] = RibbonCommandIconKind.ChartColumn,
            ["freew.chart-type-scatter"] = RibbonCommandIconKind.ChartColumn,
            ["freew.chart-type-area"] = RibbonCommandIconKind.ChartColumn,
            ["freew.chart-type-doughnut"] = RibbonCommandIconKind.ChartColumn,

            [EquationPresetCatalog.Get(EquationPresetKind.Fraction).CommandId] = RibbonCommandIconKind.Equation,
            [EquationPresetCatalog.Get(EquationPresetKind.Script).CommandId] = RibbonCommandIconKind.Equation,
            [EquationPresetCatalog.Get(EquationPresetKind.Radical).CommandId] = RibbonCommandIconKind.Equation,
            [EquationPresetCatalog.Get(EquationPresetKind.NthRoot).CommandId] = RibbonCommandIconKind.Equation,
            [EquationPresetCatalog.Get(EquationPresetKind.Integral).CommandId] = RibbonCommandIconKind.Equation,
            [EquationPresetCatalog.Get(EquationPresetKind.Summation).CommandId] = RibbonCommandIconKind.Equation,
            [EquationPresetCatalog.Get(EquationPresetKind.Product).CommandId] = RibbonCommandIconKind.Equation,
            [EquationPresetCatalog.Get(EquationPresetKind.Accent).CommandId] = RibbonCommandIconKind.Equation,
            [EquationPresetCatalog.Get(EquationPresetKind.Bar).CommandId] = RibbonCommandIconKind.Equation,
            [EquationPresetCatalog.Get(EquationPresetKind.Bracket).CommandId] = RibbonCommandIconKind.Equation,
            [EquationPresetCatalog.Get(EquationPresetKind.Matrix).CommandId] = RibbonCommandIconKind.Equation,
            [EquationPresetCatalog.Get(EquationPresetKind.Function).CommandId] = RibbonCommandIconKind.Equation,
            [EquationPresetCatalog.Get(EquationPresetKind.GroupCharacter).CommandId] = RibbonCommandIconKind.Equation,

            ["freew.previous-footnote"] = RibbonCommandIconKind.Footnote,
            ["freew.next-endnote"] = RibbonCommandIconKind.Endnote,
            ["freew.previous-endnote"] = RibbonCommandIconKind.Endnote,

            ["freew.hf-edit-even-header"]       = RibbonCommandIconKind.Header,
            ["freew.hf-edit-even-footer"]       = RibbonCommandIconKind.Footer,
            ["freew.hf-edit-first-header"]      = RibbonCommandIconKind.Header,
            ["freew.hf-edit-first-footer"]      = RibbonCommandIconKind.Footer,
            ["freew.hf-header-from-top"]        = RibbonCommandIconKind.Margins,
            ["freew.hf-footer-from-bottom"]     = RibbonCommandIconKind.Margins,
            ["freew.hf-insert-page-number-footer"] = RibbonCommandIconKind.PageNumber,


            ["freew.read-mode-column-narrow"]  = RibbonCommandIconKind.ReadMode,
            ["freew.read-mode-column-default"] = RibbonCommandIconKind.ReadMode,
            ["freew.read-mode-column-wide"]    = RibbonCommandIconKind.ReadMode,
            ["freew.read-mode-color-none"]    = RibbonCommandIconKind.ReadMode,
            ["freew.read-mode-color-sepia"]   = RibbonCommandIconKind.ReadMode,
            ["freew.read-mode-color-inverse"] = RibbonCommandIconKind.ReadMode,

            ["freew.start-mail-merge-letters"] = RibbonCommandIconKind.Envelope,
            ["freew.start-mail-merge-directory"] = RibbonCommandIconKind.Labels,
            ["freew.start-mail-merge-normal"] = RibbonCommandIconKind.Page,
            ["freew.merge-next-record"] = RibbonCommandIconKind.Next,
            ["freew.merge-record-number"] = RibbonCommandIconKind.Field,
            ["freew.merge-sequence-number"] = RibbonCommandIconKind.Field,
            ["freew.merge-rule-if"] = RibbonCommandIconKind.Field,
            ["freew.merge-rule-skip-record-if"] = RibbonCommandIconKind.Field,
            ["freew.merge-rule-next-record-if"] = RibbonCommandIconKind.Next,
            ["freew.merge-rule-fill-in"] = RibbonCommandIconKind.Field,
            ["freew.merge-rule-ask"] = RibbonCommandIconKind.Field,
            ["freew.merge-rule-set"] = RibbonCommandIconKind.Field,
            ["freew.merge-rule-ref"] = RibbonCommandIconKind.Field,

            ["freew.accept-all"] = RibbonCommandIconKind.AcceptChange,
            ["freew.reject-all"] = RibbonCommandIconKind.RejectChange,
            ["freew.display-for-review-all-markup"] = RibbonCommandIconKind.History,
            ["freew.show-markup-insertions-deletions"] = RibbonCommandIconKind.History,
            ["freew.show-markup-comments"] = RibbonCommandIconKind.Comment,

            ["freew.show-markup-balloons"] = RibbonCommandIconKind.Comment,
        };
}
