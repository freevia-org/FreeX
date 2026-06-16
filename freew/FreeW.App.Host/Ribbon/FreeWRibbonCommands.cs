using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Binds FreeW's ribbon command ids (declared in <see cref="FreeWRibbon"/>) to behavior over the
/// editing surface, implementing the shared <see cref="IRibbonCommandRegistry"/>. Formatting and
/// clipboard route through WPF's <see cref="EditingCommands"/>/<see cref="ApplicationCommands"/>
/// against the focused RichTextBox (inline edit + undo); bold/italic/underline are stateful so the
/// ribbon can reflect the selection.
/// </summary>
internal static class FreeWRibbonCommands
{
    public static RibbonCommandRegistry Build(DocumentView editor, RibbonStateStore stateStore) =>
        Build(editor, stateStore, onPrintPreview: null);

    public static RibbonCommandRegistry Build(DocumentView editor, RibbonStateStore stateStore, Action? onPrintPreview) =>
        Build(editor, stateStore, onPrintPreview, onToggleNavPane: null, isNavPaneVisible: null);

    public static RibbonCommandRegistry Build(
        DocumentView editor,
        RibbonStateStore stateStore,
        Action? onPrintPreview,
        Action? onToggleNavPane,
        Func<bool>? isNavPaneVisible)
    {
        var registry = new RibbonCommandRegistry();
        var stateful = new List<(RibbonCommandId Id, IRibbonStatefulCommand Command)>();

        void Routed(string id, RoutedCommand command) =>
            registry.Register(id, new RoutedEditCommand(editor, command));

        void Toggle(string id, RoutedCommand command, DependencyProperty property, Func<object?, bool> isOn)
        {
            var cmd = new ToggleFormatCommand(editor, command, property, isOn);
            registry.Register(id, cmd);
            stateful.Add((id, cmd));
        }

        Toggle("freew.bold", EditingCommands.ToggleBold, TextElement.FontWeightProperty,
            v => v is FontWeight w && w >= FontWeights.Bold);
        Toggle("freew.italic", EditingCommands.ToggleItalic, TextElement.FontStyleProperty,
            v => v is FontStyle s && s == FontStyles.Italic);
        Toggle("freew.underline", EditingCommands.ToggleUnderline, Inline.TextDecorationsProperty,
            v => v is TextDecorationCollection d && d.Count > 0);

        // Live ribbon state: when the caret/selection moves, recompute the toggle states and push
        // them into the shared RibbonStateStore, which the toggle buttons observe.
        editor.SelectionChanged += (_, _) =>
        {
            foreach (var (id, command) in stateful)
                stateStore.SetState(id, command.GetState());
        };

        // Home > Font: character effects. Superscript/subscript are mutually exclusive baseline
        // offsets; small caps / all caps map to WPF typography. Each is a toggle over the selection.
        registry.Register("freew.superscript", new CharacterEffectCommand(editor, CharacterEffect.Superscript));
        registry.Register("freew.subscript", new CharacterEffectCommand(editor, CharacterEffect.Subscript));
        registry.Register("freew.smallcaps", new CharacterEffectCommand(editor, CharacterEffect.SmallCaps));
        registry.Register("freew.allcaps", new CharacterEffectCommand(editor, CharacterEffect.AllCaps));

        Routed("freew.grow-font", EditingCommands.IncreaseFontSize);
        Routed("freew.shrink-font", EditingCommands.DecreaseFontSize);
        Routed("freew.align-left", EditingCommands.AlignLeft);
        Routed("freew.align-center", EditingCommands.AlignCenter);
        Routed("freew.align-right", EditingCommands.AlignRight);
        Routed("freew.bullets", EditingCommands.ToggleBullets);
        Routed("freew.numbering", EditingCommands.ToggleNumbering);
        Routed("freew.cut", ApplicationCommands.Cut);
        Routed("freew.copy", ApplicationCommands.Copy);
        Routed("freew.paste", ApplicationCommands.Paste);

        registry.Register("freew.font-family", new SelectionValueCommand(editor,
            (selection, value) => selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(value))));
        registry.Register("freew.font-size", new SelectionValueCommand(editor, (selection, value) =>
        {
            if (double.TryParse(value, out var points))
                selection.ApplyPropertyValue(TextElement.FontSizeProperty, points * 96.0 / 72.0);
        }));

        // Insert tab — Pages: prepend a cover page, or drop a horizontal rule / page break at the caret.
        // Each mutates the model through the view's undo/redo bus and re-renders.
        registry.Register("freew.cover-page", new ActionCommand(() => { editor.Focus(); editor.InsertCoverPage(); }));
        registry.Register("freew.horizontal-rule", new ActionCommand(() => { editor.Focus(); editor.InsertHorizontalRule(); }));
        registry.Register("freew.page-break", new ActionCommand(() => { editor.Focus(); editor.InsertPageBreak(); }));

        // Insert tab — insert a small 2x2 table at the caret (routes through the undo/redo bus).
        registry.Register("freew.table", new InsertTableCommand(editor, rows: 2, columns: 2));
        // Insert tab — Table Tools: structural edits to the table containing the caret (all undoable).
        registry.Register("freew.table-insert-row", new ActionCommand(() => { editor.Focus(); editor.InsertTableRow(); }));
        registry.Register("freew.table-delete-row", new ActionCommand(() => { editor.Focus(); editor.DeleteTableRow(); }));
        registry.Register("freew.table-insert-col", new ActionCommand(() => { editor.Focus(); editor.InsertTableColumn(); }));
        registry.Register("freew.table-delete-col", new ActionCommand(() => { editor.Focus(); editor.DeleteTableColumn(); }));
        // Insert tab — Table Tools: pick/clear a fill colour for the caret's cell (sets model + re-renders).
        registry.Register("freew.cell-shading", new CellShadingCommand(editor));

        // Insert tab — Illustrations: pick an image file and insert it as an inline image run.
        registry.Register("freew.picture", new InsertPictureCommand(editor));
        // Insert tab — Illustrations: resize the selected inline image (height scales proportionally).
        registry.Register("freew.image-size", new ImageSizeCommand(editor));
        // Insert tab — Links: prompt for a URL and apply it as a hyperlink over the selection.
        registry.Register("freew.hyperlink", new InsertHyperlinkCommand(editor));
        // Insert tab — References: prompt for footnote text and insert a footnote reference at the caret.
        registry.Register("freew.footnote", new InsertFootnoteCommand(editor));
        // Insert tab — References: generate a Table of Contents from the heading outline at the caret,
        // and rebuild it in place (remove the prior TOC region + re-insert). Both route through the bus.
        registry.Register("freew.toc", new ActionCommand(() => { editor.Focus(); editor.InsertTableOfContents(); }));
        registry.Register("freew.toc-refresh", new ActionCommand(() => { editor.Focus(); editor.RefreshTableOfContents(); }));
        // Insert tab — Links: name the caret's paragraph as a bookmark target (an invisible marker).
        registry.Register("freew.bookmark", new InsertBookmarkCommand(editor));
        // Insert tab — Links: apply an internal link (to an existing bookmark) over the selection.
        registry.Register("freew.link-bookmark", new LinkToBookmarkCommand(editor));

        // Review tab — Comments: prompt for comment text and attach it over the current selection.
        registry.Register("freew.new-comment", new NewCommentCommand(editor));

        // Review tab — Tracking: toggle Track Changes mode (stateful so the ribbon reflects it). When
        // ON, marking the current selection as a tracked insertion/deletion is offered; turning it on
        // with a non-empty selection marks that selection as an insertion (a pragmatic stand-in for live
        // keystroke tracking). Accept All / Reject All resolve every tracked change on the model.
        registry.Register("freew.track-changes", new TrackChangesToggleCommand(editor));
        registry.Register("freew.accept-all", new ActionCommand(() => { editor.Focus(); editor.AcceptAllRevisions(); }));
        registry.Register("freew.reject-all", new ActionCommand(() => { editor.Focus(); editor.RejectAllRevisions(); }));

        // Insert tab — Header & Footer: prompt for header/footer text, or drop a page-number field
        // into the footer. These edit the model's Header/Footer directly (saved into docx + printed).
        registry.Register("freew.header", new HeaderFooterCommand(editor, isFooter: false));
        registry.Register("freew.footer", new HeaderFooterCommand(editor, isFooter: true));
        registry.Register("freew.page-number", new InsertPageNumberCommand(editor));

        // Insert tab — Symbols: pick a glyph from a grid, or a formatted current date/time string, and
        // insert it at the caret as ordinary text (flows through the normal edit/undo path).
        registry.Register("freew.symbol", new InsertSymbolCommand(editor));
        registry.Register("freew.datetime", new InsertDateTimeCommand(editor));

        // Home > Font > Text Colour / Highlight: pick a colour from a small palette and apply it to
        // the selection (foreground reuses TextElement.Foreground; highlight uses TextElement.Background).
        registry.Register("freew.font-color", new ColorPickCommand(editor, isHighlight: false));
        registry.Register("freew.highlight", new ColorPickCommand(editor, isHighlight: true));

        // Home > Paragraph: set line spacing (a multiplier on the default font size) over the selection,
        // and toggle Add/Remove Space Before/After. All route through the view's undo/redo bus.
        registry.Register("freew.line-spacing", new LineSpacingCommand(editor));
        registry.Register("freew.space-before-toggle", new ActionCommand(() => editor.ToggleSpaceBefore()));
        registry.Register("freew.space-after-toggle", new ActionCommand(() => editor.ToggleSpaceAfter()));

        // Home > Paragraph: toggle a box border on the selected paragraph(s), and pick/clear shading.
        registry.Register("freew.para-border", new ActionCommand(() => editor.ToggleParagraphBorder()));
        registry.Register("freew.para-shading", new ParagraphShadingCommand(editor));

        registry.Register("freew.style-normal", new ApplyStyleCommand(editor, 11, bold: false, colorHex: null));
        registry.Register("freew.style-heading1", new ApplyStyleCommand(editor, 16, bold: true, colorHex: "#2F5496"));
        registry.Register("freew.style-title", new ApplyStyleCommand(editor, 28, bold: true, colorHex: null));

        // Home > Styles: the styles dropdown. Picking an entry sets the selected paragraph(s)' StyleId
        // (reversible via the bus), then re-renders so the style's run/paragraph formatting resolves.
        registry.Register("freew.style", new ApplyParagraphStyleCommand(editor));

        // Layout tab — page settings (applied to the model; honoured by docx save + print).
        registry.Register("freew.orientation", new PageCommand(editor, page =>
        {
            (page.WidthPt, page.HeightPt) = (page.HeightPt, page.WidthPt);
            page.Landscape = !page.Landscape;
        }));
        registry.Register("freew.margins", new PageCommand(editor, page =>
        {
            var narrow = page.MarginLeftPt > 54;
            var margin = narrow ? 36.0 : 72.0;
            page.MarginLeftPt = page.MarginRightPt = page.MarginTopPt = page.MarginBottomPt = margin;
        }));
        registry.Register("freew.size", new PageCommand(editor, page =>
        {
            var isLetter = Math.Abs(page.WidthPt - 612) < 1 && Math.Abs(page.HeightPt - 792) < 1;
            (page.WidthPt, page.HeightPt) = isLetter ? (595.0, 842.0) : (612.0, 792.0); // toggle Letter <-> A4
        }));
        // Columns: cycle 1 -> 2 -> 3 -> 1 equal-width columns, re-rendering so the layout shows at once.
        registry.Register("freew.columns", new ColumnCountCommand(editor));

        // Layout tab — open the modeless print-preview window (paginated, page-settings-aware).
        if (onPrintPreview is not null)
            registry.Register("freew.print-preview", new ActionCommand(onPrintPreview));

        // View tab — toggle the navigation pane (heading outline). Stateful so the ribbon's toggle
        // button reflects whether the pane is currently shown.
        if (onToggleNavPane is not null && isNavPaneVisible is not null)
            registry.Register("freew.nav-pane", new ToggleActionCommand(onToggleNavPane, isNavPaneVisible));

        return registry;
    }

    // The four Home > Font character effects wired by CharacterEffectCommand.
    private enum CharacterEffect { Superscript, Subscript, SmallCaps, AllCaps }

    // Home > Font: apply a character effect to the selection as a toggle. Superscript/subscript set
    // Inline.BaselineAlignment (and shrink the font, mirroring DocumentView's render); small/all caps
    // set Typography.Capitals. Applying an effect that is already present clears it. These properties
    // are exactly what DocumentView.ReadRunFormatting reads back, so the effect round-trips to docx.
    private sealed class CharacterEffectCommand(DocumentView editor, CharacterEffect effect) : IRibbonCommand
    {
        private const double SuperSubScale = 0.65;

        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var selection = editor.Selection;
            switch (effect)
            {
                case CharacterEffect.Superscript:
                case CharacterEffect.Subscript:
                    ToggleBaseline(selection,
                        effect == CharacterEffect.Superscript ? BaselineAlignment.Superscript : BaselineAlignment.Subscript);
                    break;
                case CharacterEffect.SmallCaps:
                    ToggleCapitals(selection, FontCapitals.SmallCaps);
                    break;
                case CharacterEffect.AllCaps:
                    ToggleCapitals(selection, FontCapitals.AllSmallCaps);
                    break;
            }
        }

        private static void ToggleBaseline(TextSelection selection, BaselineAlignment target)
        {
            var current = selection.GetPropertyValue(Inline.BaselineAlignmentProperty);
            var alreadyOn = current is BaselineAlignment b && b == target;
            if (alreadyOn)
            {
                // Clearing: restore baseline and undo the shrink so the original size returns.
                selection.ApplyPropertyValue(Inline.BaselineAlignmentProperty, BaselineAlignment.Baseline);
                ScaleFontSize(selection, 1 / SuperSubScale);
            }
            else
            {
                // If switching from the other offset, the shrink is already applied — don't shrink twice.
                if (current is not BaselineAlignment cur ||
                    (cur != BaselineAlignment.Superscript && cur != BaselineAlignment.Subscript))
                {
                    ScaleFontSize(selection, SuperSubScale);
                }
                selection.ApplyPropertyValue(Inline.BaselineAlignmentProperty, target);
            }
        }

        private static void ScaleFontSize(TextSelection selection, double factor)
        {
            var value = selection.GetPropertyValue(TextElement.FontSizeProperty);
            if (value is double size && size > 0)
                selection.ApplyPropertyValue(TextElement.FontSizeProperty, size * factor);
        }

        private static void ToggleCapitals(TextSelection selection, FontCapitals target)
        {
            var current = selection.GetPropertyValue(Typography.CapitalsProperty);
            var alreadyOn = current is FontCapitals c && c == target;
            selection.ApplyPropertyValue(Typography.CapitalsProperty,
                alreadyOn ? FontCapitals.Normal : target);
        }
    }

    // A parameterless ribbon command that runs a host-supplied action (e.g. opening a window).
    private sealed class ActionCommand(Action action) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => action();
    }

    // A stateful toggle command: executing runs the host action (e.g. show/hide a panel) and its
    // checked-ness is read back from a host predicate, so the ribbon toggle reflects the live state.
    private sealed class ToggleActionCommand(Action toggle, Func<bool> isChecked) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) => toggle();

        public RibbonCommandState GetState() => new(IsEnabled: true, IsChecked: isChecked());
    }

    // Home > Paragraph > Line Spacing: parse the chosen multiplier (e.g. "1.5") and apply it to every
    // paragraph spanned by the selection. The view routes the change through its undo/redo bus.
    private sealed class LineSpacingCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!context.Parameters.TryGetValue("value", out var raw) || raw is not string value)
                return;
            if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var multiplier) && multiplier > 0)
            {
                editor.Focus();
                editor.SetLineSpacing(multiplier);
            }
        }
    }

    // Applies a named paragraph style's formatting (size/weight/colour) to the current selection.
    private sealed class ApplyStyleCommand(DocumentView editor, double sizePt, bool bold, string? colorHex) : IRibbonCommand
    {
        private const double PxPerPoint = 96.0 / 72.0;

        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var selection = editor.Selection;
            selection.ApplyPropertyValue(TextElement.FontSizeProperty, sizePt * PxPerPoint);
            selection.ApplyPropertyValue(TextElement.FontWeightProperty, bold ? FontWeights.Bold : FontWeights.Normal);
            var brush = colorHex is null ? Brushes.Black : new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
            selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
        }
    }

    // Home > Styles: apply a real paragraph style. The styles dropdown's value is a display name
    // (e.g. "Heading 1"); this maps it to the matching style id in the model's catalog and sets the
    // selected paragraph(s)' StyleId through the view's undo/redo bus (re-rendered to resolve formatting).
    private sealed class ApplyParagraphStyleCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!context.Parameters.TryGetValue("value", out var raw) || raw is not string value || value.Length == 0)
                return;

            var styleId = ResolveStyleId(editor.Model, value);
            if (styleId is null)
                return;

            editor.Focus();
            editor.SetParagraphStyle(styleId);
        }

        // Match the chosen combo entry to a style in the document by id first, then by display name
        // (case-insensitive, ignoring spaces) so "Heading 1" resolves to the "Heading1" style id.
        private static string? ResolveStyleId(TextDocument model, string choice)
        {
            if (model.Styles.ContainsKey(choice))
                return choice;
            foreach (var style in model.Styles.Values)
            {
                if (string.Equals(style.Name, choice, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Compact(style.Id), Compact(choice), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Compact(style.Name), Compact(choice), StringComparison.OrdinalIgnoreCase))
                    return style.Id;
            }
            return null;
        }

        private static string Compact(string value) => value.Replace(" ", string.Empty);
    }

    // Home > Font: pick a colour from a small fixed palette and apply it to the selection. When
    // isHighlight is false it sets the text foreground; when true it sets the text background
    // (highlight). "Automatic"/"No Color" clears the property back to its inherited value.
    private sealed class ColorPickCommand(DocumentView editor, bool isHighlight) : IRibbonCommand
    {
        private static readonly string[] Palette =
        [
            "#000000", "#404040", "#7F7F7F", "#C00000", "#FF0000", "#FFC000",
            "#FFFF00", "#92D050", "#00B050", "#00B0F0", "#0070C0", "#2F5496",
            "#7030A0", "#FFFFFF",
        ];

        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var chosen = ShowPicker(owner);
            if (chosen is null)
                return;

            var property = isHighlight ? TextElement.BackgroundProperty : TextElement.ForegroundProperty;
            editor.Focus();
            if (chosen == ColorChoice.Clear)
                // Clear the override: foreground falls back to black, highlight to no background.
                editor.Selection.ApplyPropertyValue(property, isHighlight ? null! : Brushes.Black);
            else
                editor.Selection.ApplyPropertyValue(property,
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString(chosen.Hex)));
        }

        private sealed record ColorChoice(string Hex)
        {
            public static readonly ColorChoice Clear = new(string.Empty);
        }

        private ColorChoice? ShowPicker(Window? owner)
        {
            ColorChoice? result = null;
            var window = new Window
            {
                Title = isHighlight ? "Highlight Colour" : "Text Colour",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner is null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(8) };
            var grid = new WrapPanel { Width = 7 * 26 };
            foreach (var hex in Palette)
            {
                var swatch = new Button
                {
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                    BorderThickness = new Thickness(1),
                    ToolTip = hex
                };
                swatch.Click += (_, _) => { result = new ColorChoice(hex); window.Close(); };
                grid.Children.Add(swatch);
            }
            panel.Children.Add(grid);

            var clear = new Button
            {
                Content = isHighlight ? "No Color" : "Automatic",
                Margin = new Thickness(2, 6, 2, 0),
                Padding = new Thickness(8, 2, 8, 2)
            };
            clear.Click += (_, _) => { result = ColorChoice.Clear; window.Close(); };
            panel.Children.Add(clear);

            window.Content = panel;
            window.ShowDialog();
            return result;
        }
    }

    // Home > Paragraph > Shading: pick a fill colour from a small palette and apply it to the
    // selected paragraph(s); "No Color" clears shading. Mirrors ColorPickCommand's swatch picker.
    private sealed class ParagraphShadingCommand(DocumentView editor) : IRibbonCommand
    {
        private static readonly string[] Palette =
        [
            "#FFFF00", "#92D050", "#00B0F0", "#FFC000", "#FF0000", "#D9D9D9",
            "#A6A6A6", "#FFF2CC", "#DEEBF7", "#E2EFDA", "#FCE4D6", "#EDEDED",
        ];

        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var (chosen, hex) = ShowPicker(owner);
            if (!chosen)
                return;
            editor.ToggleParagraphShading(hex);
        }

        private (bool Chosen, string? Hex) ShowPicker(Window? owner)
        {
            var chosen = false;
            string? hex = null;
            var window = new Window
            {
                Title = "Paragraph Shading",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner is null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(8) };
            var grid = new WrapPanel { Width = 6 * 26 };
            foreach (var swatchHex in Palette)
            {
                var swatch = new Button
                {
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(swatchHex)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                    BorderThickness = new Thickness(1),
                    ToolTip = swatchHex
                };
                swatch.Click += (_, _) => { chosen = true; hex = swatchHex; window.Close(); };
                grid.Children.Add(swatch);
            }
            panel.Children.Add(grid);

            var clear = new Button
            {
                Content = "No Color",
                Margin = new Thickness(2, 6, 2, 0),
                Padding = new Thickness(8, 2, 8, 2)
            };
            clear.Click += (_, _) => { chosen = true; hex = null; window.Close(); };
            panel.Children.Add(clear);

            window.Content = panel;
            window.ShowDialog();
            return (chosen, hex);
        }
    }

    // Insert > Table Tools > Cell Shading: pick a fill colour from a small palette and apply it to the
    // caret's table cell; "No Color" clears shading. Mirrors ParagraphShadingCommand's swatch picker.
    private sealed class CellShadingCommand(DocumentView editor) : IRibbonCommand
    {
        private static readonly string[] Palette =
        [
            "#FFFF00", "#92D050", "#00B0F0", "#FFC000", "#FF0000", "#D9D9D9",
            "#A6A6A6", "#FFF2CC", "#DEEBF7", "#E2EFDA", "#FCE4D6", "#EDEDED",
        ];

        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var (chosen, hex) = ShowPicker(owner);
            if (!chosen)
                return;
            editor.SetCaretCellShading(hex);
        }

        private (bool Chosen, string? Hex) ShowPicker(Window? owner)
        {
            var chosen = false;
            string? hex = null;
            var window = new Window
            {
                Title = "Cell Shading",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner is null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(8) };
            var grid = new WrapPanel { Width = 6 * 26 };
            foreach (var swatchHex in Palette)
            {
                var swatch = new Button
                {
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(swatchHex)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                    BorderThickness = new Thickness(1),
                    ToolTip = swatchHex
                };
                swatch.Click += (_, _) => { chosen = true; hex = swatchHex; window.Close(); };
                grid.Children.Add(swatch);
            }
            panel.Children.Add(grid);

            var clear = new Button
            {
                Content = "No Color",
                Margin = new Thickness(2, 6, 2, 0),
                Padding = new Thickness(8, 2, 8, 2)
            };
            clear.Click += (_, _) => { chosen = true; hex = null; window.Close(); };
            panel.Children.Add(clear);

            window.Content = panel;
            window.ShowDialog();
            return (chosen, hex);
        }
    }

    private sealed class PageCommand(DocumentView editor, Action<PageSettings> apply) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => apply(editor.Model.Page);
    }

    // Cycles the page through 1 -> 2 -> 3 -> 1 equal-width columns. Routes through ApplyPageSettings so
    // the editor commits pending edits, mutates PageSettings.ColumnCount, and re-renders immediately.
    private sealed class ColumnCountCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyPageSettings(page => page.ColumnCount = page.ColumnCount >= 3 ? 1 : page.ColumnCount + 1);
    }

    // Inserts a table at the caret. Delegates to the view, which routes through the undo/redo bus.
    private sealed class InsertTableCommand(DocumentView editor, int rows, int columns) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.InsertTable(rows, columns);
        }
    }

    // Insert > Illustrations > Picture: pick an image, normalise to PNG, insert as an inline image run.
    private sealed class InsertPictureCommand(DocumentView editor) : IRibbonCommand
    {
        private const double PxPerPoint = 96.0 / 72.0;
        private const double MaxWidthPt = 400;

        public void Execute(RibbonCommandContext context)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*",
                Title = "Insert Picture"
            };
            if (dialog.ShowDialog(Window.GetWindow(editor)) != true)
                return;

            try
            {
                var image = LoadAsInlineImage(dialog.FileName);
                editor.Focus();
                editor.InsertImage(image);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(editor), $"Could not insert the image:\n{ex.Message}",
                    "FreeW", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Decode any supported format and re-encode to PNG so the docx writer only ever emits PNG.
        private static InlineImage LoadAsInlineImage(string path)
        {
            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.UriSource = new Uri(path);
            source.EndInit();
            source.Freeze();

            using var buffer = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(buffer);

            // Convert device-independent pixels to points, capping the width so large photos fit.
            var widthPt = source.PixelWidth / PxPerPoint;
            var heightPt = source.PixelHeight / PxPerPoint;
            if (widthPt > MaxWidthPt && widthPt > 0)
            {
                heightPt *= MaxWidthPt / widthPt;
                widthPt = MaxWidthPt;
            }
            return new InlineImage(buffer.ToArray(), widthPt, heightPt);
        }
    }

    // Insert > Illustrations > Image Size: prompt for a new width; the view scales height proportionally.
    private sealed class ImageSizeCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                MessageBox.Show(Window.GetWindow(editor), "Select an image first, then choose Image Size.",
                    "FreeW", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (ImageSizeDialog.Prompt(Window.GetWindow(editor), image.WidthPt) is { } widthPt)
                editor.SetSelectedImageSize(widthPt);
        }
    }

    // Insert > Links > Link: prompt for a URL, then apply it as a hyperlink over the selection.
    private sealed class InsertHyperlinkCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var seed = editor.Selection.Text is { Length: > 0 } text && Uri.IsWellFormedUriString(text, UriKind.Absolute)
                ? text
                : "https://";
            var url = HyperlinkPrompt.Ask(Window.GetWindow(editor), seed);
            if (!string.IsNullOrWhiteSpace(url))
                editor.ApplyHyperlink(url!.Trim());
        }
    }

    // Insert > Symbols > Symbol: show a glyph grid and insert the chosen glyph at the caret as text.
    private sealed class InsertSymbolCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var glyph = SymbolPickerDialog.Prompt(Window.GetWindow(editor));
            if (!string.IsNullOrEmpty(glyph))
                editor.InsertText(glyph);
        }
    }

    // Insert > Symbols > Date & Time: list formatted current date/time strings; insert the chosen one.
    private sealed class InsertDateTimeCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var text = DateTimeDialog.Prompt(Window.GetWindow(editor));
            if (!string.IsNullOrEmpty(text))
                editor.InsertText(text);
        }
    }

    // Insert > References > Footnote: prompt for the footnote text, then insert a footnote reference
    // at the caret. The view allocates the next id, stores the content and drops a superscript marker.
    private sealed class InsertFootnoteCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var text = TextPrompt.Ask(Window.GetWindow(editor), "Insert Footnote", "Footnote text:", string.Empty);
            if (string.IsNullOrWhiteSpace(text))
                return; // cancelled or empty — nothing to anchor a footnote to
            editor.Focus();
            editor.InsertFootnote(text.Trim());
        }
    }

    // Review > Comments > New Comment: prompt for the comment text, then attach it over the current
    // selection. The author comes from the document's Author property (falling back to the OS user),
    // with initials derived from it; the view marks the selected runs and stores the comment.
    private sealed class NewCommentCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var text = TextPrompt.Ask(Window.GetWindow(editor), "New Comment", "Comment:", string.Empty);
            if (string.IsNullOrWhiteSpace(text))
                return; // cancelled or empty — nothing to attach

            var author = editor.Model.Properties.Author;
            if (string.IsNullOrWhiteSpace(author))
                author = Environment.UserName;
            author = author?.Trim() ?? string.Empty;

            editor.Focus();
            editor.InsertComment(text.Trim(), author, DeriveInitials(author));
        }

        // Initials = the first letter of each whitespace-separated word, upper-cased (max 3).
        private static string DeriveInitials(string author)
        {
            var parts = author.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var initials = string.Concat(parts.Take(3).Select(p => char.ToUpperInvariant(p[0])));
            return initials.Length > 0 ? initials : "?";
        }
    }

    // Review > Tracking > Track Changes: a stateful toggle over the editor's Track Changes mode. Live
    // keystroke tracking is out of scope in a RichTextBox, so as a pragmatic gesture, turning the toggle
    // ON with a non-empty selection marks that selection as a tracked insertion (so the feature does
    // something visible and the round-trip is exercisable from the UI). The author comes from the
    // document Author property (falling back to the OS user); the date is stamped at mark time.
    private sealed class TrackChangesToggleCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.TrackChangesEnabled = !editor.TrackChangesEnabled;

            // When switching ON over a non-empty selection, mark it as an insertion as a stand-in for
            // live tracking. This keeps the toggle useful without brittle per-keystroke interception.
            if (editor.TrackChangesEnabled && !editor.Selection.IsEmpty)
            {
                var author = editor.Model.Properties.Author;
                if (string.IsNullOrWhiteSpace(author))
                    author = Environment.UserName;
                author = author?.Trim() ?? string.Empty;

                var dateXml = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
                editor.MarkSelectionAsRevision(RevisionKind.Inserted, author, dateXml);
            }
        }

        public RibbonCommandState GetState() => new(IsEnabled: true, IsChecked: editor.TrackChangesEnabled);
    }

    // Insert > Links > Bookmark: name the caret's paragraph as a bookmark target. Seeds the prompt
    // with any existing bookmark on that paragraph; an empty entry clears it.
    private sealed class InsertBookmarkCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var name = TextPrompt.Ask(Window.GetWindow(editor), "Bookmark",
                "Bookmark name (leave blank to remove):", string.Empty);
            if (name is null)
                return; // cancelled — leave the model untouched
            editor.SetBookmarkAtCaret(name);
        }
    }

    // Insert > Links > Link to Bookmark: pick an existing bookmark and link the selection to it. If no
    // bookmarks exist yet, tell the user to create one first.
    private sealed class LinkToBookmarkCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var bookmarks = editor.BookmarkNames();
            if (bookmarks.Count == 0)
            {
                MessageBox.Show(Window.GetWindow(editor),
                    "No bookmarks exist yet. Add a bookmark first (Insert › Bookmark), then link to it.",
                    "FreeW", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var chosen = BookmarkPicker.Ask(Window.GetWindow(editor), bookmarks);
            if (!string.IsNullOrWhiteSpace(chosen))
                editor.ApplyInternalLink(chosen!);
        }
    }

    // A tiny modal dialog to pick one of the document's bookmark names. Returns the chosen name, or
    // null if cancelled.
    private static class BookmarkPicker
    {
        public static string? Ask(Window? owner, IReadOnlyList<string> bookmarks)
        {
            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 280,
                MinHeight = 120,
                Margin = new Thickness(0, 0, 0, 12)
            };
            foreach (var name in bookmarks)
                list.Items.Add(name);
            list.SelectedIndex = 0;

            string? result = null;
            var dialog = new Window
            {
                Title = "Link to Bookmark",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) => { result = list.SelectedItem as string; dialog.DialogResult = true; };
            list.MouseDoubleClick += (_, _) => { result = list.SelectedItem as string; dialog.DialogResult = true; };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Bookmark:", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(list);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Insert > Header & Footer: prompt for the header/footer text and store it on the model. An empty
    // entry clears the header/footer. A page-number field already present is preserved by re-appending.
    private sealed class HeaderFooterCommand(DocumentView editor, bool isFooter) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var model = editor.Model;
            var existing = isFooter ? model.Footer : model.Header;
            var seed = existing?.PlainText ?? string.Empty;
            var label = isFooter ? "Footer" : "Header";

            var text = TextPrompt.Ask(Window.GetWindow(editor), $"Edit {label}", $"{label} text:", seed);
            if (text is null)
                return; // cancelled — leave the model untouched

            var hadPageNumber = existing?.Paragraphs.SelectMany(p => p.Runs)
                .Any(r => r.FieldKind == RunFieldKind.PageNumber) ?? false;

            HeaderFooter? value;
            if (text.Length == 0 && !hadPageNumber)
            {
                value = null;
            }
            else
            {
                value = new HeaderFooter();
                var paragraph = new FreeW.Core.Model.Paragraph();
                if (text.Length > 0)
                    paragraph.Runs.Add(new FreeW.Core.Model.Run(text));
                if (hadPageNumber)
                {
                    if (paragraph.Runs.Count > 0)
                        paragraph.Runs.Add(new FreeW.Core.Model.Run("  "));
                    paragraph.Runs.Add(FreeW.Core.Model.Run.PageNumberField());
                }
                value.Paragraphs.Add(paragraph);
            }

            if (isFooter)
                model.Footer = value;
            else
                model.Header = value;

            editor.Focus();
        }
    }

    // Insert > Header & Footer > Page Number: drop a centered page-number field into the footer.
    private sealed class InsertPageNumberCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var model = editor.Model;
            var footer = model.Footer ?? new HeaderFooter();

            var alreadyPresent = footer.Paragraphs.SelectMany(p => p.Runs)
                .Any(r => r.FieldKind == RunFieldKind.PageNumber);
            if (!alreadyPresent)
            {
                var paragraph = new FreeW.Core.Model.Paragraph
                {
                    Formatting = ParagraphFormatting.Default with { Alignment = FreeW.Core.Model.TextAlignment.Center }
                };
                paragraph.Runs.Add(new FreeW.Core.Model.Run("Page "));
                paragraph.Runs.Add(FreeW.Core.Model.Run.PageNumberField());
                footer.Paragraphs.Add(paragraph);
            }

            model.Footer = footer;
            editor.Focus();
        }
    }

    // A tiny modal text-entry dialog. Returns the entered text (possibly empty), or null if cancelled.
    private static class TextPrompt
    {
        public static string? Ask(Window? owner, string title, string label, string seed)
        {
            var box = new System.Windows.Controls.TextBox
            {
                Text = seed,
                MinWidth = 360,
                Margin = new Thickness(0, 0, 0, 12)
            };
            box.SelectAll();

            string? result = null;
            var dialog = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) => { result = box.Text; dialog.DialogResult = true; };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(box);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            box.Focus();
            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // A tiny modal dialog asking for a URL. Returns the entered text, or null if cancelled.
    private static class HyperlinkPrompt
    {
        public static string? Ask(Window? owner, string seed)
        {
            var box = new System.Windows.Controls.TextBox
            {
                Text = seed,
                MinWidth = 360,
                Margin = new Thickness(0, 0, 0, 12)
            };
            box.SelectAll();

            string? result = null;
            var dialog = new Window
            {
                Title = "Insert Link",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) => { result = box.Text; dialog.DialogResult = true; };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Address:", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(box);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            box.Focus();
            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Applies a value chosen from a ribbon combo (font family/size) to the current selection.
    private sealed class SelectionValueCommand(DocumentView editor, Action<TextSelection, string> apply) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (context.Parameters.TryGetValue("value", out var raw) && raw is string value && value.Length > 0)
            {
                editor.Focus();
                apply(editor.Selection, value);
            }
        }
    }

    private sealed class RoutedEditCommand(DocumentView editor, RoutedCommand command) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (command.CanExecute(null, editor))
                command.Execute(null, editor);
        }
    }

    private sealed class ToggleFormatCommand(
        DocumentView editor,
        RoutedCommand command,
        DependencyProperty property,
        Func<object?, bool> isOn) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (command.CanExecute(null, editor))
                command.Execute(null, editor);
        }

        public RibbonCommandState GetState()
        {
            var value = editor.Selection.GetPropertyValue(property);
            return new RibbonCommandState(IsEnabled: true, IsChecked: value != DependencyProperty.UnsetValue && isOn(value));
        }
    }
}
