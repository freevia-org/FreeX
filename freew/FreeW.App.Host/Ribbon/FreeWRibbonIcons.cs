using System.Collections.Generic;
using Free.Shared.Ribbon;

namespace FreeW.App.Host;

/// <summary>
/// Maps FreeW's <c>freew.*</c> ribbon command ids to shared <see cref="RibbonCommandIconKind"/> glyphs.
///
/// The shared WPF renderer draws a vector glyph per control by command id, using
/// <see cref="Free.Shared.Ribbon.Wpf.RibbonIconFactory"/>. The shared geometry catalogue
/// (<c>RibbonIconDefinitions</c>) is keyed by <see cref="RibbonCommandIconKind"/>, not by FreeW's ids, so
/// this class supplies the bridge: it installs a resolver that turns e.g. <c>"freew.bold"</c> into
/// <see cref="RibbonCommandIconKind.Bold"/>. Ids without a dedicated mapping fall back to the generic
/// glyph (a consistent, non-blank icon), so every button shows something meaningful.
/// </summary>
internal static class FreeWRibbonIcons
{
    /// <summary>Installs the FreeW command-id → glyph resolver on the shared icon factory.</summary>
    public static void Install() =>
        Free.Shared.Ribbon.Wpf.RibbonIconFactory.CommandIconKindResolver = Resolve;

    public static RibbonCommandIconKind? Resolve(string commandId) =>
        Map.TryGetValue(commandId, out var kind) ? kind : null;

    private static readonly IReadOnlyDictionary<string, RibbonCommandIconKind> Map =
        new Dictionary<string, RibbonCommandIconKind>(System.StringComparer.OrdinalIgnoreCase)
        {
            // Clipboard
            ["freew.paste"] = RibbonCommandIconKind.Paste,
            ["freew.paste-plain"] = RibbonCommandIconKind.Paste,
            ["freew.paste-merge"] = RibbonCommandIconKind.Merge,
            ["freew.cut"] = RibbonCommandIconKind.Cut,
            ["freew.copy"] = RibbonCommandIconKind.Copy,
            ["freew.format-painter"] = RibbonCommandIconKind.FormatPainter,

            // Font
            ["freew.font-family"] = RibbonCommandIconKind.Font,
            ["freew.font-size"] = RibbonCommandIconKind.Size,
            ["freew.bold"] = RibbonCommandIconKind.Bold,
            ["freew.italic"] = RibbonCommandIconKind.Italic,
            ["freew.underline"] = RibbonCommandIconKind.Underline,
            ["freew.superscript"] = RibbonCommandIconKind.TextFunction,
            ["freew.subscript"] = RibbonCommandIconKind.TextFunction,
            ["freew.smallcaps"] = RibbonCommandIconKind.Font,
            ["freew.allcaps"] = RibbonCommandIconKind.Font,
            ["freew.change-case"] = RibbonCommandIconKind.Font,
            ["freew.font-color"] = RibbonCommandIconKind.Color,
            ["freew.highlight"] = RibbonCommandIconKind.Fill,
            ["freew.grow-font"] = RibbonCommandIconKind.ArrowUp,
            ["freew.shrink-font"] = RibbonCommandIconKind.ArrowDown,
            ["freew.clear-formatting"] = RibbonCommandIconKind.Clear,
            ["freew.strikethrough"] = RibbonCommandIconKind.Strikethrough,

            // Paragraph
            ["freew.bullets"] = RibbonCommandIconKind.List,
            ["freew.numbering"] = RibbonCommandIconKind.List,
            ["freew.multilevel-list"] = RibbonCommandIconKind.List,
            ["freew.align-left"] = RibbonCommandIconKind.Align,
            ["freew.align-center"] = RibbonCommandIconKind.Align,
            ["freew.align-right"] = RibbonCommandIconKind.Align,
            ["freew.align-justify"] = RibbonCommandIconKind.Align,
            ["freew.line-spacing"] = RibbonCommandIconKind.Wrap,
            ["freew.space-before-toggle"] = RibbonCommandIconKind.Wrap,
            ["freew.space-after-toggle"] = RibbonCommandIconKind.Wrap,
            ["freew.indent-increase"] = RibbonCommandIconKind.ArrowRight,
            ["freew.indent-decrease"] = RibbonCommandIconKind.ArrowLeft,
            ["freew.paragraph-dialog"] = RibbonCommandIconKind.Wrap,
            ["freew.para-border"] = RibbonCommandIconKind.Border,
            ["freew.para-shading"] = RibbonCommandIconKind.Fill,
            ["freew.keep-with-next"] = RibbonCommandIconKind.Wrap,
            ["freew.keep-lines"] = RibbonCommandIconKind.Wrap,
            ["freew.widow-control"] = RibbonCommandIconKind.Wrap,

            // Styles
            ["freew.style"] = RibbonCommandIconKind.Font,
            ["freew.style-normal"] = RibbonCommandIconKind.Font,
            ["freew.style-heading1"] = RibbonCommandIconKind.Font,
            ["freew.style-title"] = RibbonCommandIconKind.Font,
            ["freew.new-style"] = RibbonCommandIconKind.Insert,
            ["freew.manage-styles"] = RibbonCommandIconKind.Effects,

            // Insert: pages
            ["freew.cover-page"] = RibbonCommandIconKind.Page,
            ["freew.blank-page"] = RibbonCommandIconKind.Page,
            ["freew.horizontal-rule"] = RibbonCommandIconKind.Line,
            ["freew.page-break"] = RibbonCommandIconKind.PageBreak,
            ["freew.drop-cap"] = RibbonCommandIconKind.TextBox,

            // Insert: tables
            ["freew.table"] = RibbonCommandIconKind.Table,
            ["freew.table-insert-row"] = RibbonCommandIconKind.Table,
            ["freew.table-delete-row"] = RibbonCommandIconKind.Table,
            ["freew.table-insert-col"] = RibbonCommandIconKind.Table,
            ["freew.table-delete-col"] = RibbonCommandIconKind.Table,
            ["freew.cell-shading"] = RibbonCommandIconKind.Fill,
            ["freew.merge-cells"] = RibbonCommandIconKind.Merge,
            ["freew.split-cell"] = RibbonCommandIconKind.Grid,
            ["freew.table-header-row"] = RibbonCommandIconKind.Table,
            ["freew.table-banded-rows"] = RibbonCommandIconKind.Table,
            ["freew.table-repeat-header"] = RibbonCommandIconKind.Table,

            // Insert: illustrations
            ["freew.picture"] = RibbonCommandIconKind.Picture,
            ["freew.image-size"] = RibbonCommandIconKind.Size,
            ["freew.image-alt-text"] = RibbonCommandIconKind.Accessibility,
            ["freew.image-align-left"] = RibbonCommandIconKind.Align,
            ["freew.image-align-center"] = RibbonCommandIconKind.Align,
            ["freew.image-align-right"] = RibbonCommandIconKind.Align,
            ["freew.shapes"] = RibbonCommandIconKind.Rectangle,
            ["freew.shape-rectangle"] = RibbonCommandIconKind.Rectangle,
            ["freew.shape-rounded"] = RibbonCommandIconKind.Rectangle,
            ["freew.shape-ellipse"] = RibbonCommandIconKind.Ellipse,
            ["freew.shape-textbox"] = RibbonCommandIconKind.TextBox,

            // Insert: media (equation / chart / WordArt / SmartArt / OLE object)
            ["freew.equation"] = RibbonCommandIconKind.Function,
            ["freew.chart"] = RibbonCommandIconKind.Table,
            ["freew.wordart"] = RibbonCommandIconKind.TextBox,
            ["freew.smartart"] = RibbonCommandIconKind.Group,
            ["freew.object"] = RibbonCommandIconKind.Grid,

            // Insert: links
            ["freew.hyperlink"] = RibbonCommandIconKind.Link,
            ["freew.edit-hyperlink"] = RibbonCommandIconKind.Link,
            ["freew.remove-hyperlink"] = RibbonCommandIconKind.Link,
            ["freew.hyperlink-tooltip"] = RibbonCommandIconKind.Comment,
            ["freew.bookmark"] = RibbonCommandIconKind.Pin,
            ["freew.link-bookmark"] = RibbonCommandIconKind.Link,
            ["freew.bookmark-manager"] = RibbonCommandIconKind.Pin,

            // Insert: quick parts
            ["freew.save-quickpart"] = RibbonCommandIconKind.Save,
            ["freew.insert-quickpart"] = RibbonCommandIconKind.Insert,
            ["freew.insert-file"] = RibbonCommandIconKind.Page,

            // Insert: references
            ["freew.footnote"] = RibbonCommandIconKind.Insert,
            ["freew.endnote"] = RibbonCommandIconKind.Insert,
            ["freew.toc"] = RibbonCommandIconKind.List,
            ["freew.toc-refresh"] = RibbonCommandIconKind.Refresh,
            ["freew.citation"] = RibbonCommandIconKind.Book,
            ["freew.citation-style"] = RibbonCommandIconKind.Book,
            ["freew.bibliography"] = RibbonCommandIconKind.Book,
            ["freew.caption"] = RibbonCommandIconKind.Label,
            ["freew.cross-reference"] = RibbonCommandIconKind.Link,
            ["freew.index-mark"] = RibbonCommandIconKind.Pin,
            ["freew.index-insert"] = RibbonCommandIconKind.List,
            ["freew.tof"] = RibbonCommandIconKind.List,
            ["freew.tof-refresh"] = RibbonCommandIconKind.Refresh,

            // Insert: controls
            ["freew.cc-text"] = RibbonCommandIconKind.TextBox,
            ["freew.cc-checkbox"] = RibbonCommandIconKind.Grid,

            // Insert: header & footer
            ["freew.header"] = RibbonCommandIconKind.HeaderFooter,
            ["freew.footer"] = RibbonCommandIconKind.HeaderFooter,
            ["freew.page-number"] = RibbonCommandIconKind.Number,

            // Insert: symbols
            ["freew.symbol"] = RibbonCommandIconKind.Symbol,
            ["freew.datetime"] = RibbonCommandIconKind.Date,
            ["freew.field"] = RibbonCommandIconKind.Function,

            // Layout: page setup
            ["freew.margins"] = RibbonCommandIconKind.Margins,
            ["freew.orientation"] = RibbonCommandIconKind.Orientation,
            ["freew.size"] = RibbonCommandIconKind.Size,
            ["freew.columns"] = RibbonCommandIconKind.TextColumns,
            ["freew.line-numbers"] = RibbonCommandIconKind.Number,
            ["freew.hyphenation"] = RibbonCommandIconKind.MinusSign,
            ["freew.page-valign"] = RibbonCommandIconKind.Align,
            ["freew.different-first-page"] = RibbonCommandIconKind.Page,

            // Layout: page background
            ["freew.page-border"] = RibbonCommandIconKind.Border,
            ["freew.watermark"] = RibbonCommandIconKind.Picture,

            // Layout: preview
            ["freew.print-preview"] = RibbonCommandIconKind.Print,

            // Layout: data
            ["freew.sort"] = RibbonCommandIconKind.Sort,
            ["freew.text-to-table"] = RibbonCommandIconKind.Table,
            ["freew.table-to-text"] = RibbonCommandIconKind.TextBox,

            // Design
            ["freew.theme"] = RibbonCommandIconKind.Theme,

            // View
            ["freew.nav-pane"] = RibbonCommandIconKind.View,
            ["freew.formatting-marks"] = RibbonCommandIconKind.Wrap,
            ["freew.read-mode"] = RibbonCommandIconKind.Book,

            // Mailings
            ["freew.merge-data"] = RibbonCommandIconKind.GetData,
            ["freew.merge-field"] = RibbonCommandIconKind.Insert,
            ["freew.merge-preview"] = RibbonCommandIconKind.Search,
            ["freew.merge-finish"] = RibbonCommandIconKind.Share,

            // Review
            ["freew.statistics"] = RibbonCommandIconKind.Function,
            ["freew.spellcheck-toggle"] = RibbonCommandIconKind.Spelling,
            ["freew.add-to-dictionary"] = RibbonCommandIconKind.Book,
            ["freew.new-comment"] = RibbonCommandIconKind.Comment,
            ["freew.track-changes"] = RibbonCommandIconKind.Wrap,
            ["freew.accept-all"] = RibbonCommandIconKind.Spelling,
            ["freew.reject-all"] = RibbonCommandIconKind.Delete,
            ["freew.restrict-editing"] = RibbonCommandIconKind.Protect,
            ["freew.compare"] = RibbonCommandIconKind.Group,
            ["freew.inspect-document"] = RibbonCommandIconKind.Search,
            ["freew.check-accessibility"] = RibbonCommandIconKind.Accessibility,
        };
}
