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
using FreeW.Core.IO;
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
        Func<bool>? isNavPaneVisible) =>
        Build(editor, stateStore, onPrintPreview, onToggleNavPane, isNavPaneVisible,
            onToggleReadMode: null, isReadModeActive: null);

    public static RibbonCommandRegistry Build(
        DocumentView editor,
        RibbonStateStore stateStore,
        Action? onPrintPreview,
        Action? onToggleNavPane,
        Func<bool>? isNavPaneVisible,
        Action? onToggleReadMode,
        Func<bool>? isReadModeActive) =>
        Build(editor, stateStore, onPrintPreview, onToggleNavPane, isNavPaneVisible,
            onToggleReadMode, isReadModeActive, onTogglePrintLayout: null, isPrintLayoutActive: null);

    public static RibbonCommandRegistry Build(
        DocumentView editor,
        RibbonStateStore stateStore,
        Action? onPrintPreview,
        Action? onToggleNavPane,
        Func<bool>? isNavPaneVisible,
        Action? onToggleReadMode,
        Func<bool>? isReadModeActive,
        Action? onTogglePrintLayout,
        Func<bool>? isPrintLayoutActive) =>
        Build(editor, stateStore, onPrintPreview, onToggleNavPane, isNavPaneVisible,
            onToggleReadMode, isReadModeActive, onTogglePrintLayout, isPrintLayoutActive,
            onToggleOutlineView: null, isOutlineViewActive: null);

    public static RibbonCommandRegistry Build(
        DocumentView editor,
        RibbonStateStore stateStore,
        Action? onPrintPreview,
        Action? onToggleNavPane,
        Func<bool>? isNavPaneVisible,
        Action? onToggleReadMode,
        Func<bool>? isReadModeActive,
        Action? onTogglePrintLayout,
        Func<bool>? isPrintLayoutActive,
        Action? onToggleOutlineView,
        Func<bool>? isOutlineViewActive) =>
        Build(editor, stateStore, onPrintPreview, onToggleNavPane, isNavPaneVisible,
            onToggleReadMode, isReadModeActive, onTogglePrintLayout, isPrintLayoutActive,
            onToggleOutlineView, isOutlineViewActive, onZoomDialog: null);

    public static RibbonCommandRegistry Build(
        DocumentView editor,
        RibbonStateStore stateStore,
        Action? onPrintPreview,
        Action? onToggleNavPane,
        Func<bool>? isNavPaneVisible,
        Action? onToggleReadMode,
        Func<bool>? isReadModeActive,
        Action? onTogglePrintLayout,
        Func<bool>? isPrintLayoutActive,
        Action? onToggleOutlineView,
        Func<bool>? isOutlineViewActive,
        Action? onZoomDialog,
        Action? onWebLayout = null,
        Func<bool>? isWebLayoutActive = null,
        Action? onDraftView = null,
        Func<bool>? isDraftViewActive = null)
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
        // Home > Paragraph: apply multilevel/legal outline numbering (1, 1.1, 1.1.1) to the selected
        // paragraph(s); the outline definition persists to word/numbering.xml. Tab/Shift+Tab demote
        // and promote the outline depth (ListLevel) of the selected list paragraphs.
        registry.Register("freew.multilevel-list", new ActionCommand(() => editor.ApplyMultiLevelList()));
        registry.Register("freew.multilevel-demote", new ActionCommand(() => editor.ChangeListLevel(+1)));
        registry.Register("freew.multilevel-promote", new ActionCommand(() => editor.ChangeListLevel(-1)));
        Routed("freew.cut", ApplicationCommands.Cut);
        Routed("freew.copy", ApplicationCommands.Copy);
        Routed("freew.paste", ApplicationCommands.Paste);
        // Home > Clipboard: paste-special. "Paste Text Only" strips all source formatting; "Merge
        // Formatting" matches the destination. In FreeW both resolve to match-destination insertion at
        // the caret (the pasted text inherits the caret run's formatting), routed through the editor's
        // undoable InsertText path. See DocumentView.PastePlainText / PasteMergeFormatting.
        registry.Register("freew.paste-plain", new ActionCommand(() => editor.PastePlainText()));
        registry.Register("freew.paste-merge", new ActionCommand(() => editor.PasteMergeFormatting()));

        // Home > Clipboard > Format Painter: arm the painter from the current selection's run +
        // paragraph formatting; the editor stamps it onto the user's next mouse selection and disarms.
        registry.Register("freew.format-painter", new FormatPainterCommand(editor));

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
        // Insert tab — Table Tools: merge the selected cells / split a merged cell (all undoable).
        registry.Register("freew.merge-cells", new ActionCommand(() => { editor.Focus(); editor.MergeSelectedCells(); }));
        registry.Register("freew.split-cell", new ActionCommand(() => { editor.Focus(); editor.SplitCell(); }));
        // Insert tab — Table Tools: pick/clear a fill colour for the caret's cell (sets model + re-renders).
        registry.Register("freew.cell-shading", new CellShadingCommand(editor));
        // Insert tab — Table Tools: table-style toggles applied to the caret's table (sets model + re-renders).
        // Table Tools — Data: insert a computed formula field (=SUM(ABOVE) etc.) into the caret's cell.
        registry.Register("freew.table-formula", new TableFormulaCommand(editor));
        registry.Register("freew.table-header-row", new ActionCommand(() => { editor.Focus(); editor.ToggleTableHeaderRow(); }));
        registry.Register("freew.table-banded-rows", new ActionCommand(() => { editor.Focus(); editor.ToggleTableBandedRows(); }));
        registry.Register("freew.table-repeat-header", new ActionCommand(() => { editor.Focus(); editor.ToggleTableRepeatHeaderRow(); }));

        // Insert tab — Text: pick a .docx file and insert its body content at the caret (block merge).
        registry.Register("freew.insert-file", new InsertFileCommand(editor));
        // Insert tab — Illustrations: pick an image file and insert it as an inline image run.
        registry.Register("freew.picture", new InsertPictureCommand(editor));
        // Insert tab — Illustrations > Screenshot: the top-level "freew.screenshot" id only opens the
        // dropdown (no direct insert, so it isn't registered — mirroring "freew.shapes" above). "Screen
        // Clipping" drag-selects a screen region and inserts the captured PNG as an inline image through
        // the exact same InsertImage path as Insert Picture.
        registry.Register("freew.screen-clipping", new ScreenClippingCommand(editor));
        // Insert tab — Illustrations: resize the selected inline image (height scales proportionally).
        registry.Register("freew.image-size", new ImageSizeCommand(editor));
        // Insert tab — Illustrations: set the selected image's accessibility alt text (wp:docPr @descr),
        // and align the image's (image-only) paragraph left/center/right. Both mutate the model + re-render.
        registry.Register("freew.image-alt-text", new ImageAltTextCommand(editor));
        registry.Register("freew.image-align-left", new ImageAlignCommand(editor, FreeW.Core.Model.TextAlignment.Left));
        registry.Register("freew.image-align-center", new ImageAlignCommand(editor, FreeW.Core.Model.TextAlignment.Center));
        registry.Register("freew.image-align-right", new ImageAlignCommand(editor, FreeW.Core.Model.TextAlignment.Right));
        // Insert tab — Illustrations > Shapes: a small gallery of preset DrawingML shapes. Each menu item
        // inserts the matching Shape (preset geometry, or a text box carrying placeholder text) at the caret
        // via DocumentView.InsertShape. Round-trips through docx as an inline w:drawing/wps:wsp (see
        // DocxWriter/Reader). The top-level "freew.shapes" id only opens the menu (no direct insert).
        registry.Register("freew.shape-rectangle", new ActionCommand(() =>
        {
            editor.Focus();
            editor.InsertShape(FreeW.Core.Model.Shape.Preset(FreeW.Core.Model.ShapeKind.Rectangle, widthPt: 120, heightPt: 80, fillColorHex: "#DCE6F1"));
        }));
        registry.Register("freew.shape-rounded", new ActionCommand(() =>
        {
            editor.Focus();
            editor.InsertShape(FreeW.Core.Model.Shape.Preset(FreeW.Core.Model.ShapeKind.RoundedRectangle, widthPt: 120, heightPt: 80, fillColorHex: "#DCE6F1"));
        }));
        registry.Register("freew.shape-ellipse", new ActionCommand(() =>
        {
            editor.Focus();
            editor.InsertShape(FreeW.Core.Model.Shape.Preset(FreeW.Core.Model.ShapeKind.Ellipse, widthPt: 100, heightPt: 100, fillColorHex: "#DCE6F1"));
        }));
        registry.Register("freew.shape-textbox", new ActionCommand(() =>
        {
            editor.Focus();
            editor.InsertShape(FreeW.Core.Model.Shape.TextBoxWith("Text Box", widthPt: 180, heightPt: 90, fillColorHex: "#DCE6F1"));
        }));
        // Insert tab — Media: drop a sample equation / chart / WordArt / SmartArt / OLE object at the caret.
        // Each routes through the editor's undoable insert path (mirroring InsertShape) and round-trips
        // through docx (the model + IO already exist; this surfaces them in the ribbon). Sample content is a
        // starting point the user can replace.
        registry.Register("freew.equation", new ActionCommand(() =>
        {
            editor.Focus();
            editor.InsertEquation(SampleEquation());
        }));
        // Equation gallery presets (Insert > Media > Equation dropdown). Each inserts one OMML structure
        // at the caret as an editable starting point; all round-trip through the model/IO layer.
        registry.Register("freew.equation-fraction", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.Fraction("a", "b")]))));
        registry.Register("freew.equation-script", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.SubSuperscript("x", "n", "2")]))));
        registry.Register("freew.equation-radical", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.Radical("x")]))));
        registry.Register("freew.equation-nthroot", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.Radical("x", "n")]))));
        registry.Register("freew.equation-integral", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.NAry("∫", "a", "b", "f(x) dx")]))));
        registry.Register("freew.equation-summation", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.NAry("∑", "i=1", "n", "i")]))));
        registry.Register("freew.equation-product", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.NAry("∏", "i=1", "n", "i")]))));
        registry.Register("freew.equation-bracket", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.Delimiter("a, b")]))));
        registry.Register("freew.equation-matrix", new ActionCommand(() => InsertEquationPreset(editor,
            new Equation([MathRun.MatrixOf(MathMatrix.Identity2x2())]))));
        registry.Register("freew.chart", new ActionCommand(() =>
        {
            editor.Focus();
            editor.InsertChart(Chart.Create(
                ChartKind.Column,
                categories: ["Q1", "Q2", "Q3", "Q4"],
                values: [8.0, 5.0, 11.0, 7.0],
                seriesName: "Sales",
                title: "Quarterly Sales"));
        }));
        registry.Register("freew.wordart", new ActionCommand(() =>
        {
            editor.Focus();
            editor.InsertWordArt(WordArt.Create("WordArt", WordArtStyle.GradientFill));
        }));
        registry.Register("freew.smartart", new ActionCommand(() =>
        {
            editor.Focus();
            editor.InsertSmartArt(SmartArt.Create(SmartArtKind.Process, ["First", "Second", "Third"]));
        }));
        registry.Register("freew.object", new ActionCommand(() =>
        {
            editor.Focus();
            editor.InsertEmbeddedObject(SampleEmbeddedObject());
        }));
        // Insert tab — Links: prompt for a URL and apply it as a hyperlink over the selection.
        registry.Register("freew.hyperlink", new InsertHyperlinkCommand(editor));
        // Insert tab — Links: manage the hyperlink at the caret — change its URL, remove it, or set a ScreenTip.
        registry.Register("freew.edit-hyperlink", new EditHyperlinkCommand(editor));
        registry.Register("freew.remove-hyperlink", new RemoveHyperlinkCommand(editor));
        registry.Register("freew.hyperlink-tooltip", new HyperlinkTooltipCommand(editor));
        // Insert tab — References: prompt for footnote text and insert a footnote reference at the caret.
        registry.Register("freew.footnote", new InsertFootnoteCommand(editor));
        // Insert tab — References: prompt for endnote text and insert an endnote reference at the caret.
        registry.Register("freew.endnote", new InsertEndnoteCommand(editor));
        // Insert tab — References: generate a Table of Contents from the heading outline at the caret,
        // and rebuild it in place (remove the prior TOC region + re-insert). Both route through the bus.
        registry.Register("freew.toc", new ActionCommand(() => { editor.Focus(); editor.InsertTableOfContents(); }));
        registry.Register("freew.toc-refresh", new ActionCommand(() => { editor.Focus(); editor.RefreshTableOfContents(); }));
        // Insert tab — References: insert an in-text citation (pick an existing source or add a new one),
        // and insert a bibliography built from the document's sources at the caret (reversible).
        registry.Register("freew.citation", new InsertCitationCommand(editor));
        registry.Register("freew.bibliography", new ActionCommand(() => { editor.Focus(); editor.InsertBibliography(); }));
        // Insert tab — References: select the active citation/bibliography style (APA / MLA / Chicago) used
        // by the citation + bibliography commands. The combo box delivers its label via the "value" param.
        registry.Register("freew.citation-style", new CitationStyleCommand(editor));
        // Insert tab — References: insert a numbered figure/table caption under the caret's block.
        registry.Register("freew.caption", new InsertCaptionCommand(editor));
        // Insert tab — References: insert a cross-reference (heading/bookmark/caption/footnote) at the caret.
        registry.Register("freew.cross-reference", new InsertCrossReferenceCommand(editor));
        // Insert tab — References: mark the selection (or a prompted term) for the document index, and
        // insert an alphabetical index built from the marked terms at the caret (reversibly via the bus).
        registry.Register("freew.index-mark", new MarkIndexEntryCommand(editor));
        registry.Register("freew.index-insert", new ActionCommand(() => { editor.Focus(); editor.InsertIndex(); }));
        // Insert tab — References: generate a Table of Figures from the document's figure captions at the
        // caret, and rebuild it in place (remove the prior region + re-insert). Both route through the bus.
        registry.Register("freew.tof", new ActionCommand(() => { editor.Focus(); editor.InsertTableOfFigures(); }));
        registry.Register("freew.tof-refresh", new ActionCommand(() => { editor.Focus(); editor.RefreshTableOfFigures(); }));
        // Insert tab — References: mark the selection as a legal citation (a hidden TA field), and insert /
        // rebuild a Table of Authorities built from those marks, grouped by category (reversibly via the bus).
        registry.Register("freew.mark-citation", new MarkCitationCommand(editor));
        registry.Register("freew.table-of-authorities", new ActionCommand(() => { editor.Focus(); editor.InsertTableOfAuthorities(); }));
        registry.Register("freew.table-of-authorities-refresh", new ActionCommand(() => { editor.Focus(); editor.RefreshTableOfAuthorities(); }));
        // Insert tab — Links: name the caret's paragraph as a bookmark target (an invisible marker).
        registry.Register("freew.bookmark", new InsertBookmarkCommand(editor));
        // Insert tab — Links: apply an internal link (to an existing bookmark) over the selection.
        registry.Register("freew.link-bookmark", new LinkToBookmarkCommand(editor));
        // Insert tab — Links: open the Bookmark Manager (list bookmarks with Go To + Delete).
        registry.Register("freew.bookmark-manager", new BookmarkManagerCommand(editor));

        // Insert tab — Quick Parts (AutoText): a shared snippet library persisted under FreeW's data
        // folder. "Save Selection" captures the selection's text and stores it under a prompted name;
        // "Insert Quick Part" picks a saved snippet and drops its text at the caret (reversibly).
        var quickParts = QuickPartLibrary.Load();
        registry.Register("freew.save-quickpart", new SaveQuickPartCommand(editor, quickParts));
        registry.Register("freew.insert-quickpart", new InsertQuickPartCommand(editor, quickParts));
        // "Building Blocks Organizer" opens a manager over that same library: list + preview, Insert, Delete.
        registry.Register("freew.building-blocks-organizer", new BuildingBlocksOrganizerCommand(editor, quickParts));

        // Insert tab — Controls: insert a content control (w:sdt) around the selection. The plain-text
        // control wraps the selection (or a placeholder) as an editable region; the checkbox control
        // drops a toggleable ☐/☒ checkbox. Both round-trip through docx as a w:sdt.
        registry.Register("freew.cc-text", new ActionCommand(() => { editor.Focus(); editor.InsertPlainTextControl(); }));
        registry.Register("freew.cc-richtext", new ActionCommand(() => { editor.Focus(); editor.InsertRichTextControl(); }));
        registry.Register("freew.cc-checkbox", new ActionCommand(() => { editor.Focus(); editor.InsertCheckBoxControl(); }));
        registry.Register("freew.cc-date", new ActionCommand(() => { editor.Focus(); editor.InsertDatePickerControl(); }));
        registry.Register("freew.cc-dropdown", new ActionCommand(() => { editor.Focus(); editor.InsertDropDownListControl(); }));
        registry.Register("freew.cc-combo", new ActionCommand(() => { editor.Focus(); editor.InsertComboBoxControl(); }));

        // Review tab — Comments: prompt for comment text and attach it over the current selection.
        registry.Register("freew.new-comment", new NewCommentCommand(editor));
        // Review tab — Comments: reply to / resolve the comment thread covering the caret (modern threaded
        // comments). Reply prompts for text and appends a child comment; Resolve toggles the thread's done flag.
        registry.Register("freew.reply-comment", new ReplyCommentCommand(editor));
        registry.Register("freew.resolve-comment", new ResolveCommentCommand(editor));

        // Review tab — Proofing: open the read-only Word Count / Statistics dialog. Commits pending
        // edits first so the counts reflect the current text, then computes from the model.
        registry.Register("freew.statistics", new StatisticsCommand(editor));

        // Review tab — Proofing: custom dictionary + spelling options. The custom dictionary is a
        // word-per-line .lex file persisted under FreeW's data folder; its Uri is registered with the
        // editor's WPF spell checker so those words stop being flagged. "Add to Dictionary" takes the
        // misspelled word at the caret, adds it to the dictionary (+ persists), and re-reads the file so
        // it is no longer underlined. "Spell Check" is a stateful toggle over SpellCheck.IsEnabled.
        var customDictionary = CustomDictionaryStore.Load();
        editor.RegisterCustomDictionary(customDictionary.EnsureFileExists());
        registry.Register("freew.add-to-dictionary", new AddToDictionaryCommand(editor, customDictionary));
        var spellCheckToggle = new SpellCheckToggleCommand(editor);
        registry.Register("freew.spellcheck-toggle", spellCheckToggle);
        stateful.Add(("freew.spellcheck-toggle", spellCheckToggle));

        // Review tab — Speech > Read Aloud: a stateful toggle over an in-box text-to-speech read-through
        // (System.Speech via SystemSpeechEngine, behind the model's ISpeechEngine so the controller stays
        // testable). Toggling ON commits pending edits, maps the caret to the matching speakable segment,
        // and starts reading from there to the end of the document; toggling OFF stops. The checked state
        // reflects whether a read-through is active (so the ribbon shows it at a glance), and the controller
        // pushes its state back into the store when reading finishes on its own. Construction is robust on a
        // machine with no installed voice (the engine degrades to a no-op rather than crashing).
        var readAloud = new ReadAloudToggleCommand(editor);
        readAloud.StateChanged += () => stateStore.SetState("freew.read-aloud", readAloud.GetState());
        registry.Register("freew.read-aloud", readAloud);
        stateful.Add(("freew.read-aloud", readAloud));

        // Review tab — Tracking: toggle Track Changes mode (stateful so the ribbon reflects it). When
        // ON, marking the current selection as a tracked insertion/deletion is offered; turning it on
        // with a non-empty selection marks that selection as an insertion (a pragmatic stand-in for live
        // keystroke tracking). Accept All / Reject All resolve every tracked change on the model.
        registry.Register("freew.track-changes", new TrackChangesToggleCommand(editor));
        registry.Register("freew.accept-all", new ActionCommand(() => { editor.Focus(); editor.AcceptAllRevisions(); }));
        registry.Register("freew.reject-all", new ActionCommand(() => { editor.Focus(); editor.RejectAllRevisions(); }));

        // Review tab — Protect: Restrict Editing. A stateful toggle over document protection: turning it
        // on locks the document read-only (RichTextBox IsReadOnly) and emits word/settings.xml's
        // w:documentProtection on save; turning it off clears protection. The toggle reflects whether
        // the document is currently protected.
        var restrictEditing = new RestrictEditingToggleCommand(editor);
        registry.Register("freew.restrict-editing", restrictEditing);
        stateful.Add(("freew.restrict-editing", restrictEditing));

        // Review tab — Compare: open a second .docx and load a comparison of the current document against
        // it as tracked changes (insertions/deletions relative to the opened "original").
        registry.Register("freew.compare", new CompareDocumentsCommand(editor));

        // Review tab — Inspect Document: report the metadata the document carries (comments, tracked
        // changes, document properties, bookmarks) via the pure DocumentInspector, and let the user
        // selectively remove categories. Applied removals mutate editor.Model in place and re-render.
        registry.Register("freew.inspect-document", new InspectDocumentCommand(editor));

        // Review tab — Inspect > Check Accessibility: commit pending edits, run the pure AccessibilityChecker
        // over the model, and show the report (issues grouped by severity) in a read-only modal. Read-only.
        registry.Register("freew.check-accessibility", new CheckAccessibilityCommand(editor));

        // Insert tab — Header & Footer: prompt for header/footer text, or drop a page-number field
        // into the footer. These edit the model's Header/Footer directly (saved into docx + printed).
        registry.Register("freew.header", new HeaderFooterCommand(editor, isFooter: false));
        registry.Register("freew.footer", new HeaderFooterCommand(editor, isFooter: true));
        registry.Register("freew.page-number", new InsertPageNumberCommand(editor));
        registry.Register("freew.field", new InsertFieldCommand(editor));

        // Insert tab — Symbols: pick a glyph from a grid, or a formatted current date/time string, and
        // insert it at the caret as ordinary text (flows through the normal edit/undo path).
        registry.Register("freew.symbol", new InsertSymbolCommand(editor));
        registry.Register("freew.datetime", new InsertDateTimeCommand(editor));

        // Home > Font > Text Colour / Highlight: pick a colour from a small palette and apply it to
        // the selection (foreground reuses TextElement.Foreground; highlight uses TextElement.Background).
        registry.Register("freew.font-color", new ColorPickCommand(editor, isHighlight: false));
        registry.Register("freew.highlight", new ColorPickCommand(editor, isHighlight: true));

        // Home > Font: clear all character formatting in the selection (reset every run to the document
        // default, keeping text). Insert > Pages: apply a drop cap (enlarged leading letter) to the
        // caret's paragraph. Both route through the view's undo/redo bus and re-render.
        registry.Register("freew.clear-formatting", new ActionCommand(() => editor.ClearFormatting()));
        registry.Register("freew.drop-cap", new ActionCommand(() => editor.ApplyDropCap()));

        // Home > Font > Change Case: open a small menu to pick a target case (UPPERCASE / lowercase /
        // Sentence case / Capitalize Each Word / tOGGLE cASE) and recase the selection's text via the
        // pure ChangeCase helper. The replacement flows through the editor's normal edit/undo path.
        registry.Register("freew.change-case", new ChangeCaseCommand(editor));

        // Home > Paragraph: set line spacing (a multiplier on the default font size) over the selection,
        // and toggle Add/Remove Space Before/After. All route through the view's undo/redo bus.
        registry.Register("freew.line-spacing", new LineSpacingCommand(editor));
        registry.Register("freew.space-before-toggle", new ActionCommand(() => editor.ToggleSpaceBefore()));
        registry.Register("freew.space-after-toggle", new ActionCommand(() => editor.ToggleSpaceAfter()));

        // Home > Paragraph: increase/decrease the left indent by one 0.5in step over the selection, and
        // open the Paragraph dialog to set left/right/first-line (incl. hanging) indents. All reversible.
        registry.Register("freew.indent-increase", new ActionCommand(() => { editor.Focus(); editor.IncreaseIndent(); }));
        registry.Register("freew.indent-decrease", new ActionCommand(() => { editor.Focus(); editor.DecreaseIndent(); }));
        registry.Register("freew.paragraph-dialog", new ParagraphIndentCommand(editor));
        registry.Register("freew.tabs-dialog", new TabsCommand(editor));

        // Home > Paragraph: toggle a box border on the selected paragraph(s), and pick/clear shading.
        registry.Register("freew.para-border", new ActionCommand(() => editor.ToggleParagraphBorder()));
        registry.Register("freew.para-shading", new ParagraphShadingCommand(editor));
        // Home / Design > Borders and Shading…: the full dialog (paragraph border, page border, shading).
        registry.Register("freew.borders-shading", new BordersAndShadingCommand(editor));

        // Home > Paragraph (Line and Page Breaks): flow-control toggles over the selected paragraph(s).
        // Each flips its pPr flag (keepNext/keepLines/widowControl) reversibly through the undo/redo bus.
        registry.Register("freew.keep-with-next", new ActionCommand(() => { editor.Focus(); editor.ToggleKeepWithNext(); }));
        registry.Register("freew.keep-lines", new ActionCommand(() => { editor.Focus(); editor.ToggleKeepLinesTogether(); }));
        registry.Register("freew.widow-control", new ActionCommand(() => { editor.Focus(); editor.ToggleWidowControl(); }));

        // Layout > Sort: open a small dialog (A→Z / Z→A + case-sensitive option) and sort the selected
        // paragraphs in place through the view's undo/redo bus.
        registry.Register("freew.sort", new SortCommand(editor));

        // Layout > Table conversions: turn the selected paragraphs into a table (splitting on a chosen
        // delimiter) and turn the caret's table back into delimited paragraphs. Both route through the bus.
        registry.Register("freew.text-to-table", new TextToTableCommand(editor));
        registry.Register("freew.table-to-text", new TableToTextCommand(editor));

        registry.Register("freew.style-normal", new ApplyStyleCommand(editor, 11, bold: false, colorHex: null));
        registry.Register("freew.style-heading1", new ApplyStyleCommand(editor, 16, bold: true, colorHex: "#2F5496"));
        registry.Register("freew.style-title", new ApplyStyleCommand(editor, 28, bold: true, colorHex: null));

        // Home > Styles: the styles dropdown. Picking an entry sets the selected paragraph(s)' StyleId
        // (reversible via the bus), then re-renders so the style's run/paragraph formatting resolves.
        registry.Register("freew.style", new ApplyParagraphStyleCommand(editor));

        // Home > Styles: New Style opens a dialog capturing name + formatting + based-on, creates a custom
        // DocumentStyle via the pure StyleManager and applies it to the selection. Manage Styles lets the
        // user modify or delete the catalog's styles (built-ins are guarded against deletion).
        registry.Register("freew.new-style", new NewStyleCommand(editor));
        registry.Register("freew.manage-styles", new ManageStylesCommand(editor));

        // Design > Document Formatting: the Themes dropdown. Picking a theme name applies that built-in
        // colour/font scheme to the document's style catalog (rewriting heading/title colours + fonts and
        // the body face) and re-renders so the change is visible at once.
        registry.Register("freew.theme", new ApplyThemeCommand(editor));

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
        // Columns: open the Columns dialog (One/Two/Three/Left/Right + spacing/line-between/More), applying
        // the chosen column layout to PageSettings and re-rendering so the flow shows at once.
        registry.Register("freew.columns", new ColumnsCommand(editor));
        // Page Setup: the unified Margins / Paper / Layout dialog (Word's Layout > Page Setup launcher). The
        // "Custom Margins…" / "More Paper Sizes…" entry points open the same dialog on the Margins / Paper tab.
        registry.Register("freew.page-setup", new PageSetupCommand(editor, PageSetupDialog.Tab.Margins));
        registry.Register("freew.custom-margins", new PageSetupCommand(editor, PageSetupDialog.Tab.Margins));
        registry.Register("freew.more-paper-sizes", new PageSetupCommand(editor, PageSetupDialog.Tab.Paper));
        // Line Numbers: cycle None -> Continuous -> RestartEachPage -> None (shown in print preview).
        registry.Register("freew.line-numbers", new LineNumberCommand(editor));

        // Page setup polish — all mutate PageSettings via ApplyPageSettings (commit + re-render) and
        // round-trip through docx save.
        //  - Hyphenation: a dropdown (None / Automatic / Manual / Options…). The split-button default action
        //    (freew.hyphenation) toggles automatic hyphenation; the menu items set an explicit mode, and the
        //    Options item opens the Hyphenation Options dialog. Automatic hyphenation inserts soft hyphens in
        //    the live document (settings.xml w:autoHyphenation + zone/limit/caps sub-options).
        //  - Page Vertical Alignment: cycle Top -> Center -> Justified (-> Bottom) (sectPr w:vAlign).
        //  - Different First Page: toggle a distinct first-page header/footer (sectPr w:titlePg).
        registry.Register("freew.hyphenation", new HyphenationCommand(editor));
        registry.Register("freew.hyphenation-none", new HyphenationModeCommand(editor, auto: false));
        registry.Register("freew.hyphenation-auto", new HyphenationModeCommand(editor, auto: true));
        registry.Register("freew.hyphenation-manual", new HyphenationManualCommand(editor));
        registry.Register("freew.hyphenation-options", new HyphenationOptionsCommand(editor));
        registry.Register("freew.page-valign", new PageVerticalAlignmentCommand(editor));
        registry.Register("freew.different-first-page", new DifferentFirstPageCommand(editor));

        // Layout tab — Page Background: "Page Border" opens the full Borders and Shading dialog (Word's
        // Page Borders button), and Watermark sets/clears the page watermark. Both ultimately mutate
        // PageSettings via ApplyPageSettings (commit + re-render) and round-trip through docx save.
        registry.Register("freew.page-border", new BordersAndShadingCommand(editor));
        registry.Register("freew.watermark", new WatermarkCommand(editor));

        // Design tab — Page Background: pick the whole-page background colour (Word's Page Color). Opens a
        // swatch palette + No Color + More Colours… and sets the model's page BackgroundColorHex (which
        // already round-trips as w:background in docx); the editor recolours the page sheet immediately.
        registry.Register("freew.page-color", new PageColorCommand(editor));

        // Layout tab — open the modeless print-preview window (paginated, page-settings-aware).
        if (onPrintPreview is not null)
            registry.Register("freew.print-preview", new ActionCommand(onPrintPreview));

        // View tab — toggle the navigation pane (heading outline). Stateful so the ribbon's toggle
        // button reflects whether the pane is currently shown.
        if (onToggleNavPane is not null && isNavPaneVisible is not null)
            registry.Register("freew.nav-pane", new ToggleActionCommand(onToggleNavPane, isNavPaneVisible));

        // View tab — toggle read mode (distraction-free view). Stateful so the ribbon's toggle button
        // reflects whether the chrome-light reading column is currently active.
        if (onToggleReadMode is not null && isReadModeActive is not null)
            registry.Register("freew.read-mode", new ToggleActionCommand(onToggleReadMode, isReadModeActive));

        // View tab — toggle Print Layout (Word-style page view) vs the plain/continuous view. Stateful so
        // the ribbon's toggle button reflects whether the page presentation is currently active. Default
        // on (the Word default); the host seeds the checked state to match.
        if (onTogglePrintLayout is not null && isPrintLayoutActive is not null)
            registry.Register("freew.print-layout", new ToggleActionCommand(onTogglePrintLayout, isPrintLayoutActive));

        // View tab — toggle Outline view (the heading-structured outline surface with the Outlining
        // mini-toolbar) vs the normal editing surface. Stateful so the ribbon's toggle button reflects
        // whether the outline view is currently active.
        if (onToggleOutlineView is not null && isOutlineViewActive is not null)
            registry.Register("freew.outline-view", new ToggleActionCommand(onToggleOutlineView, isOutlineViewActive));

        // View tab — switch to Web Layout (a continuous, full-width view with no page chrome, text wrapping
        // to the window like a web page) and Draft (a simplified continuous view for fast editing). Both are
        // mutually exclusive with Print Layout / Outline; the host owns the exclusivity and the stateful
        // checked-state, so these are ToggleActionCommands reflecting which view mode is active.
        if (onWebLayout is not null && isWebLayoutActive is not null)
            registry.Register("freew.web-layout", new ToggleActionCommand(onWebLayout, isWebLayoutActive));
        if (onDraftView is not null && isDraftViewActive is not null)
            registry.Register("freew.draft-view", new ToggleActionCommand(onDraftView, isDraftViewActive));

        // View tab — open Word's Zoom dialog (presets / page fits / custom %). The host computes the
        // page-relative fit factors from the live viewport and applies the chosen factor to the editor.
        if (onZoomDialog is not null)
            registry.Register("freew.zoom-dialog", new ActionCommand(onZoomDialog));

        // View tab — Show Formatting Marks: a stateful toggle over the editor's display-only pilcrow /
        // space-dot / tab-arrow overlay. The marks are drawn as a non-editable adorner computed from the
        // document's text geometry, so they never enter the model/text; executing flips the overlay and
        // (being in `stateful`) pushes the new state into the shared store so the ribbon button reflects it.
        var formattingMarks = new ToggleActionCommand(() => editor.ToggleFormattingMarks(), () => editor.ShowFormattingMarks);
        registry.Register("freew.formatting-marks", formattingMarks);
        stateful.Add(("freew.formatting-marks", formattingMarks));

        // Mailings tab — a simple mail merge. Field placeholders are the literal text «FieldName»
        // (ordinary run text, so they round-trip through docx as plain text). The four commands share a
        // MailMergeSession: "Set Data" captures the CSV/typed records; "Insert Merge Field" drops a
        // «Name» placeholder at the caret; "Preview Record" loads MergeRecord(template, row) into the
        // editor with next/prev (restoring the template when exited); "Finish & Merge" concatenates every
        // merged record into one document.
        var mergeSession = new MailMergeSession();
        registry.Register("freew.merge-data", new SetMergeDataCommand(editor, mergeSession));
        registry.Register("freew.merge-field", new InsertMergeFieldCommand(editor));
        registry.Register("freew.merge-preview", new PreviewMergeRecordCommand(editor, mergeSession));
        registry.Register("freew.merge-finish", new FinishMergeCommand(editor, mergeSession));

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

    // Home > Clipboard > Format Painter: arm the painter from the current selection (capture its run +
    // paragraph formatting), then let the editor stamp it onto the user's next mouse selection and
    // disarm — the classic capture-then-apply-to-next gesture. Clicking again while armed cancels it.
    private sealed class FormatPainterCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.ArmFormatPainter();
        }
    }

    // Home > Font > Change Case: show a small menu of the five cases and recase the current selection's
    // text through the editor (pure ChangeCase + undoable selection replacement). A no-op with an empty
    // selection — the user is told to select text first.
    private sealed class ChangeCaseCommand(DocumentView editor) : IRibbonCommand
    {
        private static readonly (string Label, CaseKind Kind)[] Choices =
        [
            ("UPPERCASE", CaseKind.Upper),
            ("lowercase", CaseKind.Lower),
            ("Sentence case", CaseKind.Sentence),
            ("Capitalize Each Word", CaseKind.Capitalize),
            ("tOGGLE cASE", CaseKind.Toggle),
        ];

        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (editor.Selection.IsEmpty)
            {
                MessageBox.Show(Window.GetWindow(editor), "Select some text first, then choose Change Case.",
                    "FreeW", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (ShowPicker(Window.GetWindow(editor)) is { } kind)
            {
                editor.Focus();
                editor.ChangeSelectionCase(kind);
            }
        }

        private static CaseKind? ShowPicker(Window? owner)
        {
            CaseKind? result = null;
            var window = new Window
            {
                Title = "Change Case",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner is null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(8), Width = 200 };
            foreach (var (label, kind) in Choices)
            {
                var button = new Button
                {
                    Content = label,
                    Margin = new Thickness(0, 2, 0, 2),
                    Padding = new Thickness(8, 4, 8, 4),
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };
                button.Click += (_, _) => { result = kind; window.Close(); };
                panel.Children.Add(button);
            }

            window.Content = panel;
            window.ShowDialog();
            return result;
        }
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

    // Home > Paragraph > Paragraph…: open the indent dialog seeded with the first selected paragraph's
    // current left/right/first-line indents, and apply the chosen values to every selected paragraph
    // through the view (reversible via the bus). A negative first-line value is a hanging indent.
    private sealed class ParagraphIndentCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var (left, right, firstLine) = editor.CurrentParagraphIndents();
            if (ParagraphIndentDialog.Prompt(Window.GetWindow(editor), left, right, firstLine) is { } chosen)
            {
                editor.Focus();
                editor.SetParagraphIndents(chosen.Left, chosen.Right, chosen.FirstLine);
            }
        }
    }

    // Home > Paragraph > Tabs…: open the Tabs dialog seeded with the first selected paragraph's current
    // tab stops, and apply the edited stop list to every selected paragraph through the view (reversible
    // via the bus). The stops round-trip to docx via the existing w:tabs writer.
    private sealed class TabsCommand(DocumentView editor) : IRibbonCommand
    {
        // Word's classic default tab-stop spacing (0.5in). The real value lives in word/settings.xml
        // (w:defaultTabStop), which FreeW preserves verbatim but does not model; shown read-only for reference.
        private const double DefaultTabStopPt = 36;

        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var current = editor.CurrentParagraphFormatting.TabStops;
            if (TabsDialog.Prompt(Window.GetWindow(editor), current, DefaultTabStopPt) is { } chosen)
            {
                editor.Focus();
                editor.SetParagraphTabStops(chosen);
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

    // Home > Styles: New Style. Opens a dialog capturing a name + a few formatting options + a based-on
    // style, then creates a custom DocumentStyle via the pure StyleManager and applies it to the
    // selection through the same reversible StyleId path the styles dropdown uses. Newly created styles
    // appear in the Style dropdown after reopening the document (the ribbon combo's item list is built
    // once from the immutable definition); the create + immediate apply is the must-have and works now.
    private sealed class NewStyleCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var catalog = StyleNamesById(editor.Model);
            var def = StyleDialog.AskNew(owner, catalog, editor.CurrentParagraphStyleId);
            if (def is null)
                return;

            var created = StyleManager.CreateStyle(editor.Model, def.Name, def.BasedOnId, def.Run, def.Paragraph);
            editor.Focus();
            editor.SetParagraphStyle(created.Id);
        }
    }

    // Home > Styles: Manage Styles. Lists the document's styles; the selected one can be modified (name is
    // fixed, formatting/based-on editable), deleted (built-ins are refused by StyleManager), or applied to
    // the selection. Pragmatic by design — the pure StyleManager carries the rules; this is the surface.
    private sealed class ManageStylesCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);

            while (true)
            {
                var action = ManageStylesDialog.Ask(owner, editor.Model, editor.CurrentParagraphStyleId);
                if (action is null)
                    return;

                switch (action)
                {
                    case ManageStyleAction.Apply apply:
                        editor.Focus();
                        editor.SetParagraphStyle(apply.StyleId);
                        return;

                    case ManageStyleAction.Delete del:
                        StyleManager.DeleteStyle(editor.Model, del.StyleId);
                        editor.RefreshStyles();
                        continue; // reopen the list so the user sees the removal

                    case ManageStyleAction.Modify mod:
                        if (!editor.Model.Styles.TryGetValue(mod.StyleId, out var existing))
                            continue;
                        var def = StyleDialog.AskModify(owner, StyleNamesById(editor.Model), existing);
                        if (def is null)
                            continue;
                        StyleManager.ModifyStyle(editor.Model, mod.StyleId,
                            run: def.Run, para: def.Paragraph, basedOnId: def.BasedOnId,
                            clearBasedOn: def.BasedOnId is null);
                        editor.RefreshStyles();
                        continue;
                }
            }
        }
    }

    // The document's style catalog as id -> display name, for the dialogs' based-on / style lists.
    private static IReadOnlyDictionary<string, string> StyleNamesById(TextDocument model) =>
        model.Styles.ToDictionary(kv => kv.Key, kv => kv.Value.Name);

    // Design > Document Formatting: apply a built-in document theme. The dropdown's value is a theme
    // name (e.g. "Slate"); this resolves it to a DocumentTheme in the catalog and asks the view to
    // rewrite the style catalog + re-render so the new heading colours/fonts and body face show at once.
    private sealed class ApplyThemeCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!context.Parameters.TryGetValue("value", out var raw) || raw is not string value || value.Length == 0)
                return;
            if (DocumentTheme.FindByName(value) is not { } theme)
                return;

            editor.Focus();
            editor.ApplyTheme(theme);
        }
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
    // Table Tools — Data > Formula (Word's Table > Data > Formula): insert a computed formula field into the
    // caret's cell. Requires the caret to be inside a table; otherwise warns and does nothing. Seeds a
    // default formula (=SUM(ABOVE) or =SUM(LEFT)) by looking at where numbers sit relative to the cell, opens
    // the Formula dialog, and inserts/recomputes the field.
    private sealed class TableFormulaCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var location = editor.CaretTableCell();
            if (location is null)
            {
                DialogMessageHelper.ShowWarning(owner!, "The cursor must be inside a table cell to insert a formula.", "Formula");
                return;
            }

            var (table, rowIndex, columnIndex) = location.Value;
            var formula = TableFormulaDialog.Prompt(owner, DefaultFormula(table, rowIndex, columnIndex));
            if (formula is null)
                return; // cancelled — leave the model untouched

            editor.Focus();
            editor.InsertTableFormula(formula);
        }

        // Word's default: =SUM(ABOVE) when numeric cells sit above the formula cell; otherwise =SUM(LEFT)
        // when numbers sit to the left; falling back to =SUM(ABOVE).
        private static string DefaultFormula(FreeW.Core.Model.Table table, int rowIndex, int columnIndex)
        {
            if (HasNumberAbove(table, rowIndex, columnIndex))
                return "=SUM(ABOVE)";
            if (HasNumberLeft(table, rowIndex, columnIndex))
                return "=SUM(LEFT)";
            return "=SUM(ABOVE)";
        }

        private static bool HasNumberAbove(FreeW.Core.Model.Table table, int rowIndex, int columnIndex)
        {
            for (var r = rowIndex - 1; r >= 0; r--)
            {
                var cells = table.Rows[r].Cells;
                if (columnIndex < cells.Count && TableFormulaEvaluator.TryParseCellNumber(cells[columnIndex].PlainText, out _))
                    return true;
            }
            return false;
        }

        private static bool HasNumberLeft(FreeW.Core.Model.Table table, int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || rowIndex >= table.Rows.Count)
                return false;
            var cells = table.Rows[rowIndex].Cells;
            for (var c = columnIndex - 1; c >= 0; c--)
            {
                if (c < cells.Count && TableFormulaEvaluator.TryParseCellNumber(cells[c].PlainText, out _))
                    return true;
            }
            return false;
        }
    }

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

    // Home / Design > Borders and Shading…: opens the full dialog (paragraph border, page border, shading)
    // seeded with the current paragraph's border/shading and the page border. Applies the chosen paragraph
    // border/shading through DocumentView (the undo/redo bus) and the page border through ApplyPageSettings;
    // everything round-trips through the existing w:pBdr / w:pgBorders / w:shd writers.
    private sealed class BordersAndShadingCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var result = BordersAndShadingDialog.Prompt(
                Window.GetWindow(editor), editor.CurrentParagraphFormatting, editor.Model.Page.PageBorder);
            if (result is null)
                return;

            editor.SetParagraphBorder(result.ParagraphBorder);
            editor.SetParagraphShading(result.ShadingHex, result.ShadingPattern);
            editor.ApplyPageSettings(page => page.PageBorder = result.PageBorder);
        }
    }

    // Opens the Columns dialog (One/Two/Three/Left/Right presets + custom count, spacing, line-between) and
    // applies the chosen layout to PageSettings. Routes through ApplyPageSettings so the editor commits
    // pending edits, mutates the page columns, and re-renders the multi-column flow immediately. Equal
    // presets clear any explicit per-column widths; the Left/Right presets set them (w:cols/@w:equalWidth).
    private sealed class ColumnsCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var result = ColumnsDialog.Prompt(Window.GetWindow(editor), editor.Model.Page);
            if (result is null)
                return;

            editor.ApplyPageSettings(page =>
            {
                page.ColumnCount = result.Count;
                page.ColumnSpacingPt = result.SpacingPt;
                page.ColumnsLineBetween = result.LineBetween;
                page.ColumnWidthsPt = result.WidthsPt;
            });
        }
    }

    // Opens the unified Page Setup dialog (Margins / Paper / Layout tabs) and applies the chosen geometry,
    // orientation, gutter, mirror-margins, paper size, header/footer distance, vertical alignment and the
    // different-first-page / odd-even toggles to PageSettings via ApplyPageSettings — the same single
    // commit + re-render path the other page-setup commands use, round-tripping through the existing w:sectPr /
    // settings.xml writers. The "Custom Margins…" / "More Paper Sizes…" entry points open the same dialog on the
    // Margins / Paper tab. The dialog's Line Numbers… / Borders… launchers defer to FreeW's existing Line
    // Numbers cycle and Borders and Shading dialog respectively, opened after the page settings are applied.
    private sealed class PageSetupCommand(DocumentView editor, PageSetupDialog.Tab initialTab) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var outcome = PageSetupDialog.Prompt(Window.GetWindow(editor), editor.Model.Page, initialTab: initialTab);
            if (outcome is not { } o)
                return;

            var settings = o.Settings;
            editor.ApplyPageSettings(page =>
            {
                page.MarginTopPt = settings.MarginTopPt;
                page.MarginBottomPt = settings.MarginBottomPt;
                page.MarginLeftPt = settings.MarginLeftPt;
                page.MarginRightPt = settings.MarginRightPt;
                page.GutterPt = settings.GutterPt;
                page.Landscape = settings.Landscape;
                page.MirrorMargins = settings.MirrorMargins;
                page.WidthPt = settings.WidthPt;
                page.HeightPt = settings.HeightPt;
                page.DifferentFirstPage = settings.DifferentFirstPage;
                page.DifferentOddEvenPages = settings.DifferentOddEvenPages;
                page.HeaderDistancePt = settings.HeaderDistancePt;
                page.FooterDistancePt = settings.FooterDistancePt;
                page.VerticalAlignment = settings.VerticalAlignment;
            });

            // Defer to the existing features for the Layout-tab launchers, so a single source of truth drives
            // line numbering and page/paragraph borders.
            if (o.LineNumbers)
                new LineNumberCommand(editor).Execute(context);
            else if (o.Borders)
                new BordersAndShadingCommand(editor).Execute(context);
        }
    }

    // Cycles page line numbering None -> Continuous -> RestartEachPage -> None. Routes through
    // ApplyPageSettings so the editor commits pending edits, mutates PageSettings, and re-renders;
    // the numbers themselves surface in the print preview / print output.
    private sealed class LineNumberCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyPageSettings(page => page.LineNumberMode = page.LineNumberMode switch
            {
                LineNumberMode.None => LineNumberMode.Continuous,
                LineNumberMode.Continuous => LineNumberMode.RestartEachPage,
                _ => LineNumberMode.None
            });
    }

    // Toggles automatic hyphenation (settings.xml w:autoHyphenation). Routes through ApplyPageSettings so
    // the editor commits pending edits, mutates PageSettings.AutoHyphenation, and re-renders.
    private sealed class HyphenationCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyPageSettings(page => page.AutoHyphenation = !page.AutoHyphenation);
    }

    // Hyphenation dropdown — None / Automatic: sets the document's automatic-hyphenation flag explicitly
    // (Word's Hyphenation > None / Automatic). Routes through ApplyPageSettings (commit + re-render) so the
    // soft-hyphen rendering shows at once and the flag round-trips through settings.xml.
    private sealed class HyphenationModeCommand(DocumentView editor, bool auto) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyPageSettings(page => page.AutoHyphenation = auto);
    }

    // Hyphenation dropdown — Manual: a simpler pass that proposes hyphenation points for long words. FreeW's
    // editor uses the same pure Hyphenator the automatic mode does; "Manual" turns hyphenation on (so the
    // proposed soft-hyphen break points render) and reports how many words it found break candidates for, so
    // the user can see the pass ran. (Word's interactive per-break confirmation UI is out of scope.)
    private sealed class HyphenationManualCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.CommitToModel();
            var candidates = CountHyphenationCandidates(editor.Model);
            editor.ApplyPageSettings(page => page.AutoHyphenation = true);

            var owner = Window.GetWindow(editor);
            if (owner is not null)
                DialogMessageHelper.ShowInfo(owner,
                    candidates == 0
                        ? "Manual hyphenation found no long words to hyphenate."
                        : $"Manual hyphenation proposed break points for {candidates} word(s). They will hyphenate at line ends.",
                    "Hyphenation");
        }

        // Count distinct word occurrences in the live document that the pure Hyphenator would break.
        private static int CountHyphenationCandidates(TextDocument model)
        {
            var count = 0;
            foreach (var block in model.Blocks)
                if (block is FreeW.Core.Model.Paragraph { Formatting.SuppressAutoHyphens: false } paragraph)
                    foreach (var run in paragraph.Runs)
                        foreach (var token in run.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                            if (Hyphenator.BreakPoints(token.Trim('(', ')', ',', '.', ';', ':', '"', '\'')).Count > 0)
                                count++;
            return count;
        }
    }

    // Hyphenation dropdown — Hyphenation Options…: opens the dialog (auto toggle, zone, consecutive-hyphen
    // limit, hyphenate-caps) and applies the chosen settings to PageSettings via ApplyPageSettings so they
    // round-trip through settings.xml and the live rendering updates.
    private sealed class HyphenationOptionsCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var result = HyphenationOptionsDialog.Prompt(owner, editor.Model.Page);
            if (result is null)
                return;

            editor.ApplyPageSettings(page =>
            {
                page.AutoHyphenation = result.AutoHyphenation;
                page.HyphenationZonePt = result.ZonePt;
                page.ConsecutiveHyphenLimit = result.ConsecutiveLimit;
                page.DoNotHyphenateCaps = !result.HyphenateCaps;
            });
        }
    }

    // Cycles page vertical alignment Top -> Center -> Justified -> Top (sectPr w:vAlign). Routes through
    // ApplyPageSettings so the editor commits pending edits, mutates PageSettings, and re-renders.
    private sealed class PageVerticalAlignmentCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyPageSettings(page => page.VerticalAlignment = page.VerticalAlignment switch
            {
                PageVerticalAlignment.Top => PageVerticalAlignment.Center,
                PageVerticalAlignment.Center => PageVerticalAlignment.Justified,
                _ => PageVerticalAlignment.Top
            });
    }

    // Toggles "different first page" (sectPr w:titlePg). Routes through ApplyPageSettings so the editor
    // commits pending edits, mutates PageSettings.DifferentFirstPage, and re-renders.
    private sealed class DifferentFirstPageCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) =>
            editor.ApplyPageSettings(page => page.DifferentFirstPage = !page.DifferentFirstPage);
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

    // Insert > Text > Text from File: pick a .docx, read it, and merge its body into the document at the caret.
    private sealed class InsertFileCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Word Documents (*.docx)|*.docx|All files (*.*)|*.*",
                Title = "Insert Text from File"
            };
            if (dialog.ShowDialog(Window.GetWindow(editor)) != true)
                return;

            try
            {
                var source = DocxReader.Read(dialog.FileName);
                editor.Focus();
                editor.InsertDocument(source);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(editor), $"Could not insert the file:\n{ex.Message}",
                    "FreeW", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

    // Insert > Illustrations > Screenshot > Screen Clipping: hide FreeW, let the user drag-select a screen
    // region (ScreenClipOverlay), capture it to PNG (ScreenshotCapture), restore FreeW, and insert the clip
    // as an inline image through the same DocumentView.InsertImage path Insert Picture uses. Escape / an
    // empty drag cancels and inserts nothing (mirroring Word).
    private sealed class ScreenClippingCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var window = Window.GetWindow(editor);
            var previousState = window?.WindowState ?? WindowState.Normal;
            try
            {
                // Briefly hide FreeW so it isn't part of the captured region (Word does the same).
                if (window is not null)
                {
                    window.WindowState = WindowState.Minimized;
                    // Let the minimize animation settle before the overlay/capture so we grab the desktop,
                    // not a half-faded FreeW frame.
                    window.Dispatcher.Invoke(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }

                var region = ScreenClipOverlay.PromptForRegion();

                if (window is not null)
                {
                    window.WindowState = previousState;
                    window.Activate();
                }

                if (region is not { } captured)
                    return;

                var pngBytes = ScreenshotCapture.CaptureRegionPng(captured);
                if (pngBytes is null)
                    return;

                var image = ScreenshotCapture.PngToInlineImage(pngBytes);
                editor.Focus();
                editor.InsertImage(image);
            }
            catch (Exception ex)
            {
                if (window is not null && window.WindowState == WindowState.Minimized)
                    window.WindowState = previousState;
                MessageBox.Show(window, $"Could not capture the screen clip:\n{ex.Message}",
                    "FreeW", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

    // Insert > Illustrations > Alt Text: prompt for the selected image's accessibility description
    // (seeded from its current alt text) and store it on the model image. A blank entry clears it; the
    // text round-trips through docx as wp:docPr/@descr and surfaces as the image tooltip/automation name.
    private sealed class ImageAltTextCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var image = editor.SelectedImage();
            if (image is null)
            {
                MessageBox.Show(Window.GetWindow(editor), "Select an image first, then choose Alt Text.",
                    "FreeW", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var text = TextPrompt.Ask(Window.GetWindow(editor), "Alt Text", "Description:", image.AltText ?? string.Empty);
            // A null result is a cancel (leave unchanged); an empty/blank string clears the alt text.
            if (text is not null)
                editor.SetSelectedImageAltText(text);
        }
    }

    // Insert > Illustrations > Align Left/Center/Right: set the alignment of the selected image's
    // (image-only) paragraph, reusing the existing ParagraphFormatting.Alignment round-trip. No-op when
    // no image is selected.
    private sealed class ImageAlignCommand(DocumentView editor, FreeW.Core.Model.TextAlignment alignment) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (editor.SelectedImage() is null)
            {
                MessageBox.Show(Window.GetWindow(editor), "Select an image first, then choose an image alignment.",
                    "FreeW", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            editor.SetSelectedImageAlignment(alignment);
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

    // Insert > Links > Edit Hyperlink: prompt for a new URL (seeded from the caret link's current URL),
    // then re-target the hyperlink at the caret. A no-op when the caret is not on a link.
    private sealed class EditHyperlinkCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (!editor.IsCaretOnHyperlink())
                return;
            var seed = editor.HyperlinkUrlAtCaret() is { Length: > 0 } current ? current : "https://";
            var url = HyperlinkPrompt.Ask(Window.GetWindow(editor), seed, "Edit Hyperlink", "Address:");
            if (!string.IsNullOrWhiteSpace(url))
                editor.EditHyperlink(url!.Trim());
        }
    }

    // Insert > Links > Remove Hyperlink: strip the hyperlink at the caret, leaving its text. No-op off a link.
    private sealed class RemoveHyperlinkCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.RemoveHyperlink();
        }
    }

    // Insert > Links > ScreenTip: prompt for a ScreenTip (seeded from the current one) and set it on the
    // hyperlink at the caret. A blank entry clears the ScreenTip. No-op when the caret is not on a link.
    private sealed class HyperlinkTooltipCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (!editor.IsCaretOnHyperlink())
                return;
            var seed = editor.HyperlinkTooltipAtCaret() ?? string.Empty;
            var tip = HyperlinkPrompt.Ask(Window.GetWindow(editor), seed, "Set ScreenTip", "ScreenTip:");
            // A null result is a cancel (leave unchanged); an empty/blank string clears the ScreenTip.
            if (tip is not null)
                editor.SetHyperlinkTooltip(tip);
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

    // Insert > References > Endnote: prompt for the endnote text, then insert an endnote reference
    // at the caret. The view allocates the next id, stores the content and drops a superscript marker.
    private sealed class InsertEndnoteCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var text = TextPrompt.Ask(Window.GetWindow(editor), "Insert Endnote", "Endnote text:", string.Empty);
            if (string.IsNullOrWhiteSpace(text))
                return; // cancelled or empty — nothing to anchor an endnote to
            editor.Focus();
            editor.InsertEndnote(text.Trim());
        }
    }

    // Insert > References > Citation: insert an in-text citation at the caret. If the document already
    // has sources, the user picks one (or chooses "Add New Source…"); otherwise they go straight to the
    // new-source form. A new source is appended to the model, then its in-text citation is inserted.
    private sealed class InsertCitationCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var sources = editor.Sources;

            Source? chosen;
            if (sources.Count > 0)
            {
                var pick = SourcePicker.Ask(owner, sources);
                if (pick is null)
                    return; // cancelled
                chosen = pick.AddNew ? PromptForNewSource(owner) : pick.Source;
            }
            else
            {
                chosen = PromptForNewSource(owner);
            }

            if (chosen is null)
                return; // cancelled or nothing entered

            editor.Focus();
            editor.InsertCitation(chosen);
        }

        // Show the new-source form, append the captured source to the model, and return it (or null if
        // the user cancelled or left every field blank — nothing worth citing).
        private Source? PromptForNewSource(Window? owner)
        {
            var entry = NewSourceDialog.Ask(owner);
            if (entry is null)
                return null;
            if (entry.Author.Length == 0 && entry.Title.Length == 0 && entry.Year.Length == 0)
                return null;
            return editor.AddSource(entry.Tag, entry.Author, entry.Title, entry.Year, entry.Publisher);
        }
    }

    // Insert > References > Caption: pick a label (Figure/Table — defaulting to Table when the caret is
    // in a table, else Figure), prompt for the caption text, then insert a numbered caption under the
    // caret's block. The view computes the next ordinal by counting existing captions of that label.
    private sealed class InsertCaptionCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var defaultLabel = editor.IsCaretInTable() ? CaptionLabel.Table : CaptionLabel.Figure;

            var label = CaptionLabelPicker.Ask(owner, defaultLabel);
            if (label is null)
                return; // cancelled

            var text = TextPrompt.Ask(owner, "Insert Caption", "Caption text (optional):", string.Empty);
            if (text is null)
                return; // cancelled — leave the model untouched

            editor.Focus();
            editor.InsertCaption(label.Value, text.Trim());
        }
    }

    // A tiny modal dialog choosing the caption label (Figure or Table), seeded with a default. Returns
    // the chosen label, or null if cancelled.
    private static class CaptionLabelPicker
    {
        public static CaptionLabel? Ask(Window? owner, CaptionLabel defaultLabel)
        {
            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 240,
                MinHeight = 60,
                Margin = new Thickness(0, 0, 0, 12)
            };
            list.Items.Add(CaptionLabel.Figure);
            list.Items.Add(CaptionLabel.Table);
            list.SelectedItem = defaultLabel;

            CaptionLabel? result = null;
            var dialog = new Window
            {
                Title = "Insert Caption",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            void Choose()
            {
                if (list.SelectedItem is CaptionLabel chosen)
                {
                    result = chosen;
                    dialog.DialogResult = true;
                }
            }
            ok.Click += (_, _) => Choose();
            list.MouseDoubleClick += (_, _) => Choose();

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Label:", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(list);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? result : null;
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

            var author = CommentAuthor.Resolve(editor);
            editor.Focus();
            editor.InsertComment(text.Trim(), author, CommentAuthor.DeriveInitials(author));
        }
    }

    // The author/initials a new comment or reply is stamped with: the document's Author property, falling
    // back to the OS user, with initials derived from it. Shared by New Comment + Reply so the two stamp
    // the same identity. Kept tiny + static so it carries no editor state.
    private static class CommentAuthor
    {
        public static string Resolve(DocumentView editor)
        {
            var author = editor.Model.Properties.Author;
            if (string.IsNullOrWhiteSpace(author))
                author = Environment.UserName;
            return author?.Trim() ?? string.Empty;
        }

        // Initials = the first letter of each whitespace-separated word, upper-cased (max 3).
        public static string DeriveInitials(string author)
        {
            var parts = author.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var initials = string.Concat(parts.Take(3).Select(p => char.ToUpperInvariant(p[0])));
            return initials.Length > 0 ? initials : "?";
        }
    }

    // Review > Comments > Reply: prompt for reply text and append it to the comment thread covering the
    // caret/selection. Warns when the caret is not inside a comment. The reply is stamped with the same
    // author/initials a new comment uses.
    private sealed class ReplyCommentCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var text = TextPrompt.Ask(Window.GetWindow(editor), "Reply", "Reply:", string.Empty);
            if (string.IsNullOrWhiteSpace(text))
                return; // cancelled or empty

            var author = CommentAuthor.Resolve(editor);
            editor.Focus();
            if (!editor.ReplyToCommentAtCaret(text.Trim(), author, CommentAuthor.DeriveInitials(author)))
                DialogMessageHelper.ShowWarning(Window.GetWindow(editor)!,
                    "Place the cursor inside a comment, then choose Reply.", "Reply");
        }
    }

    // Review > Comments > Resolve: toggle the resolved (done) state of the comment thread covering the
    // caret/selection (resolved ranges render muted). Warns when the caret is not inside a comment.
    private sealed class ResolveCommentCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (editor.ToggleResolveCommentAtCaret() is null)
                DialogMessageHelper.ShowWarning(Window.GetWindow(editor)!,
                    "Place the cursor inside a comment, then choose Resolve.", "Resolve");
        }
    }

    // Review > Proofing > Word Count: commit pending edits, compute the document statistics with the
    // pure DocumentStatistics helper, and show them in a read-only modal.
    private sealed class StatisticsCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.CommitToModel();
            var stats = DocumentStatistics.Compute(editor.Model);
            var dialog = new StatisticsDialog(Window.GetWindow(editor)!, stats);
            dialog.ShowDialog();
        }
    }

    // Review > Inspect > Check Accessibility: commit pending edits, run the pure AccessibilityChecker over
    // the model, and show the report in a read-only modal (issues grouped by severity). Read-only.
    private sealed class CheckAccessibilityCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.CommitToModel();
            var report = AccessibilityChecker.Check(editor.Model);
            var dialog = new AccessibilityReportDialog(Window.GetWindow(editor)!, report);
            dialog.ShowDialog();
        }
    }

    // Focuses the editor and drops an equation-gallery preset at the caret via the editor's undoable
    // insert path (same path as the default Equation button). Used by the Insert > Equation dropdown.
    private static void InsertEquationPreset(DocumentView editor, Equation equation)
    {
        editor.Focus();
        editor.InsertEquation(equation);
    }

    // A sample equation ("E = mc^2") built from explicit math fragments so its linear form renders the
    // superscript. Used by the Insert > Media > Equation ribbon button as a starting point.
    private static Equation SampleEquation()
    {
        var equation = new Equation();
        equation.Runs.Add(MathRun.PlainText("E = m"));
        equation.Runs.Add(MathRun.Superscript("c", "2"));
        return equation;
    }

    // A sample embedded OLE object for the Insert > Media > Object ribbon button: a small "Package"-ProgID
    // payload (a generic embedded package — Word's default for an unknown embedded file). Iconless; the
    // editor renders a labelled placeholder in its place. A starting point the user can replace.
    private static EmbeddedObject SampleEmbeddedObject() =>
        EmbeddedObject.Create(
            System.Text.Encoding.UTF8.GetBytes("FreeW embedded object placeholder."),
            progId: "Package");

    // Review > Proofing > Add to Dictionary: take the misspelled word the caret currently sits on, add
    // it to FreeW's custom dictionary (persisted to the .lex file under the data folder), and re-read the
    // dictionary so the word stops being flagged. When the caret is not on a spelling error, tell the
    // user to click into a flagged (red-underlined) word first. A no-op for a word already present.
    private sealed class AddToDictionaryCommand(DocumentView editor, CustomDictionaryStore dictionary) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var word = editor.MisspelledWordAtCaret();
            if (string.IsNullOrEmpty(word))
            {
                MessageBox.Show(Window.GetWindow(editor),
                    "Click into a misspelled (red-underlined) word first, then choose Add to Dictionary.",
                    "FreeW", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Add + persist; only refresh the live spell-check when the word was newly added (a word
            // already in the dictionary needs no reload).
            if (dictionary.Add(word))
                editor.RefreshCustomDictionary();
        }
    }

    // Review > Proofing > Spell Check: a stateful toggle over the editor's built-in spell checking
    // (SpellCheck.IsEnabled). Executing flips the red-squiggle checking on/off; the checked state
    // reflects whether checking is currently on so the ribbon button shows it at a glance.
    private sealed class SpellCheckToggleCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.ToggleSpellCheck();
        }

        public RibbonCommandState GetState() => new(IsEnabled: true, IsChecked: editor.SpellCheckEnabled);
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

    // Review > Protect > Restrict Editing: a stateful toggle over document protection. Executing flips
    // the document between unprotected and read-only (the common restrict-editing gesture): turning it
    // ON makes the RichTextBox read-only and emits word/settings.xml's w:documentProtection on save;
    // turning it OFF clears protection and restores editing. The checked state reflects whether the
    // document is currently protected, so the ribbon button shows the lock state at a glance.
    private sealed class RestrictEditingToggleCommand(DocumentView editor) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            editor.ToggleReadOnlyProtection();
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: editor.Model.Protection.IsProtected);
    }

    // Review > Speech > Read Aloud: a stateful toggle that starts/stops an in-box text-to-speech
    // read-through of the document from the caret to the end. The pure ReadAloudController owns the
    // play/stop state machine and segment extraction; the host wires it to a SystemSpeechEngine
    // (System.Speech) and maps the caret to the start segment. The engine is created lazily on first use so
    // construction is cheap, and the engine itself is robust when no voice is installed (degrades to a
    // no-op). The toggle is checked while a read-through is active; the controller raises StateChanged when
    // reading finishes on its own so the ribbon button clears.
    private sealed class ReadAloudToggleCommand : IRibbonStatefulCommand
    {
        private readonly DocumentView _editor;
        private SystemSpeechEngine? _engine;
        private ReadAloudController? _controller;

        // Re-raised to the registry so the ribbon state store refreshes when reading starts/stops — both on
        // user toggle and when the read-through completes on its own.
        public event Action? StateChanged;

        public ReadAloudToggleCommand(DocumentView editor) => _editor = editor;

        public void Execute(RibbonCommandContext context)
        {
            var controller = EnsureController();
            if (controller.IsActive)
            {
                controller.Stop();
                return;
            }

            // Commit pending edits and read from the caret's paragraph to the end (Word's behaviour).
            var start = _editor.ReadAloudStartSegmentIndex();
            controller.Start(_editor.Model, start);
        }

        public RibbonCommandState GetState() =>
            new(IsEnabled: true, IsChecked: _controller?.IsActive ?? false);

        private ReadAloudController EnsureController()
        {
            if (_controller is not null)
                return _controller;

            _engine = new SystemSpeechEngine();
            _controller = new ReadAloudController(_engine);
            _controller.StateChanged += () => StateChanged?.Invoke();
            return _controller;
        }
    }

    // Review > Compare: prompt the user to open a second .docx, read it, and load a comparison of the
    // current document against it into the editor. The opened document is treated as the "original" and
    // the current document as the "revised"; differences load as tracked insertions/deletions (rendered
    // with the existing track-changes styling). The author comes from the document Author property
    // (falling back to the OS user); the revision date is stamped at compare time (UI side, not the pure
    // helper). Pending edits are committed first so the comparison reflects the on-screen text.
    private sealed class CompareDocumentsCommand(DocumentView editor) : IRibbonCommand
    {
        private const string Filter = "Word documents (*.docx)|*.docx|All files (*.*)|*.*";

        public void Execute(RibbonCommandContext context)
        {
            var owner = Window.GetWindow(editor);
            var dialog = new OpenFileDialog
            {
                Filter = Filter,
                DefaultExt = ".docx",
                Title = "Compare With Document"
            };
            if (dialog.ShowDialog(owner) != true)
                return;

            try
            {
                editor.CommitToModel();
                var original = DocxReader.Read(dialog.FileName);
                var revised = editor.Model;

                var author = revised.Properties.Author;
                if (string.IsNullOrWhiteSpace(author))
                    author = Environment.UserName;
                author = author?.Trim() ?? string.Empty;

                var dateXml = DateTimeOffset.UtcNow.ToString(
                    "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

                var compared = DocumentCompare.Compare(original, revised, author, dateXml);
                editor.LoadModel(compared);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, $"Could not compare the documents:\n{ex.Message}",
                    "FreeW", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // Review > Inspect Document: commit pending edits, run the pure DocumentInspector over the model, and
    // open the inspector dialog reporting what was found. If the user ticks categories and clicks Remove,
    // apply the matching removal ops to editor.Model (mutating in place) and re-render the cleaned document.
    private sealed class InspectDocumentCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.CommitToModel();
            var result = DocumentInspector.Inspect(editor.Model);
            var choice = DocumentInspectorDialog.Show(Window.GetWindow(editor), result);
            if (choice is null)
                return; // cancelled or nothing selected

            editor.ApplyInspectorRemovals(choice.Comments, choice.Revisions, choice.Properties, choice.Bookmarks);
        }
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

    // Insert > Links > Bookmark Manager: open the modal Bookmark Manager listing the document's
    // bookmarks with Go To (scroll/caret via BringBlockIntoView) and Delete (clear the marker).
    private sealed class BookmarkManagerCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            BookmarkManagerDialog.Show(Window.GetWindow(editor), editor);
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

    // Insert > References > Cross-reference: pick a reference type (Heading/Bookmark/Caption/Footnote)
    // and a target, then insert it. Anchored targets (bookmarks, or headings/captions that carry a
    // bookmark) are inserted as a clickable internal link; the rest as plain reference text.
    private sealed class InsertCrossReferenceCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);

            var pick = CrossReferenceDialog.Prompt(owner, editor.Model);
            if (pick is null)
                return; // cancelled or nothing to reference

            editor.InsertCrossReference(pick.Type, pick.Target, pick.InsertAs, pick.Hyperlink);
        }
    }

    // Insert > References > Mark Entry: mark a term for the document index. Seeds from the current
    // selection's text (the usual "select then mark" gesture) and lets the user confirm or edit the term;
    // with no selection the prompt starts blank. The view appends the term to the model's index entries
    // (ignoring blanks/duplicates). The matching index is built later by Insert Index.
    private sealed class MarkIndexEntryCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var seed = editor.Selection.Text?.Trim() ?? string.Empty;
            var term = TextPrompt.Ask(Window.GetWindow(editor), "Mark Index Entry", "Index term:", seed);
            if (string.IsNullOrWhiteSpace(term))
                return; // cancelled or empty — nothing to mark
            editor.MarkIndexEntry(term.Trim());
        }
    }

    // Insert > References > Mark Citation: mark the selection (seeding the long form) as a legal citation
    // for a Table of Authorities. Opens a small dialog to pick the category and confirm the long/short
    // forms, then drops a hidden TA field at the caret (the visible table is built later by Table of
    // Authorities). Cancelling or an empty long form marks nothing.
    private sealed class MarkCitationCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var seed = editor.Selection.Text?.Trim() ?? string.Empty;
            var citation = MarkCitationDialog.Ask(Window.GetWindow(editor), seed);
            if (citation is null)
                return; // cancelled or empty — nothing to mark
            editor.MarkCitation(citation);
        }
    }

    // A small modal form capturing a citation's category, long form and short form. Returns the citation,
    // or null if cancelled (or if the long form is left blank).
    private static class MarkCitationDialog
    {
        public static Citation? Ask(Window? owner, string seedLong)
        {
            var category = new System.Windows.Controls.ComboBox { MinWidth = 320, Margin = new Thickness(0, 0, 0, 10) };
            foreach (var value in System.Enum.GetValues<CitationCategory>())
                category.Items.Add(new CategoryItem(value));
            category.SelectedIndex = 0;

            var longForm = new System.Windows.Controls.TextBox { MinWidth = 320, Margin = new Thickness(0, 0, 0, 10), Text = seedLong };
            var shortForm = new System.Windows.Controls.TextBox { MinWidth = 320, Margin = new Thickness(0, 0, 0, 10) };

            Citation? result = null;
            var dialog = new Window
            {
                Title = "Mark Citation",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "Mark", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) =>
            {
                var longText = longForm.Text.Trim();
                if (longText.Length == 0)
                    return; // nothing to mark — keep the dialog open
                var chosen = (category.SelectedItem as CategoryItem)?.Value ?? CitationCategory.Cases;
                result = new Citation(longText, chosen, shortForm.Text.Trim());
                dialog.DialogResult = true;
            };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0)
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Category:", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(category);
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Selected text (long citation):", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(longForm);
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Short citation (optional):", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(shortForm);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            longForm.Focus();
            longForm.SelectAll();
            return dialog.ShowDialog() == true ? result : null;
        }

        // Wraps a CitationCategory so the combo shows Word's friendly heading text (e.g. "Other Authorities").
        private sealed record CategoryItem(CitationCategory Value)
        {
            public override string ToString() => TableOfAuthorities.CategoryHeading(Value);
        }
    }

    // Insert > Quick Parts > Save Selection to Quick Parts: capture the current selection's text, prompt
    // for an entry name, and store it in the shared library (persisted under FreeW's data folder). An
    // empty selection or a blank/cancelled name is a no-op. Saving under an existing name overwrites it.
    private sealed class SaveQuickPartCommand(DocumentView editor, QuickPartLibrary library) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var text = editor.Selection.Text;
            if (string.IsNullOrEmpty(text))
            {
                MessageBox.Show(Window.GetWindow(editor),
                    "Select some text first, then choose Save Selection to Quick Parts.",
                    "FreeW", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var name = TextPrompt.Ask(Window.GetWindow(editor), "Save to Quick Parts", "Name:", string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                return; // cancelled or blank — nothing to store under

            library.Save(QuickPart.FromText(name.Trim(), text));
            editor.Focus();
        }
    }

    // Insert > Quick Parts > Insert Quick Part: pick a saved snippet from the library and insert its text
    // at the caret (through the editor's normal edit/undo path, so it is reversible). Reports when the
    // library is empty so the user knows to save one first.
    private sealed class InsertQuickPartCommand(DocumentView editor, QuickPartLibrary library) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (library.IsEmpty)
            {
                MessageBox.Show(Window.GetWindow(editor),
                    "No Quick Parts saved yet. Select some text and choose Save Selection to Quick Parts first.",
                    "FreeW", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var chosen = QuickPartPicker.Ask(Window.GetWindow(editor), library.Names);
            if (chosen is null)
                return; // cancelled

            var part = library.Get(chosen);
            if (part is null)
                return; // removed between listing and picking — nothing to insert

            editor.Focus();
            editor.InsertText(part.Text);
        }
    }

    // Insert > Quick Parts > Building Blocks Organizer: open a modal organizer over the shared snippet
    // library, listing every saved building block (name + gallery/category) with a preview, and offering
    // Insert (drops the block at the caret) and Delete (removes it from the persisted library).
    private sealed class BuildingBlocksOrganizerCommand(DocumentView editor, QuickPartLibrary library) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            BuildingBlocksOrganizerDialog.Show(Window.GetWindow(editor), editor, library);
        }
    }

    // A tiny modal dialog to pick one of the saved Quick Part names. Returns the chosen name, or null if
    // cancelled. Mirrors BookmarkPicker.
    private static class QuickPartPicker
    {
        public static string? Ask(Window? owner, IReadOnlyList<string> names)
        {
            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 280,
                MinHeight = 120,
                Margin = new Thickness(0, 0, 0, 12)
            };
            foreach (var name in names)
                list.Items.Add(name);
            list.SelectedIndex = 0;

            string? result = null;
            var dialog = new Window
            {
                Title = "Insert Quick Part",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "Insert", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
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
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Quick Part:", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(list);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // The outcome of the SourcePicker: either an existing source was chosen, or "Add New Source…" was.
    private sealed record SourcePick(Source? Source, bool AddNew);

    // A tiny modal dialog to pick one of the document's existing sources, or to choose "Add New Source…".
    // Returns the pick, or null if cancelled.
    private static class SourcePicker
    {
        public static SourcePick? Ask(Window? owner, IReadOnlyList<Source> sources)
        {
            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 320,
                MinHeight = 140,
                Margin = new Thickness(0, 0, 0, 12)
            };
            foreach (var source in sources)
                list.Items.Add(DescribeSource(source));
            list.SelectedIndex = 0;

            SourcePick? result = null;
            var dialog = new Window
            {
                Title = "Insert Citation",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "Insert", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            var addNew = new System.Windows.Controls.Button { Content = "Add New Source…", MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };

            void Choose()
            {
                if (list.SelectedIndex >= 0 && list.SelectedIndex < sources.Count)
                {
                    result = new SourcePick(sources[list.SelectedIndex], AddNew: false);
                    dialog.DialogResult = true;
                }
            }

            ok.Click += (_, _) => Choose();
            list.MouseDoubleClick += (_, _) => Choose();
            addNew.Click += (_, _) => { result = new SourcePick(null, AddNew: true); dialog.DialogResult = true; };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(addNew);
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Source:", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(list);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? result : null;
        }

        // A short human-readable label for the picker list: "Author (Year) — Title", degrading gracefully.
        private static string DescribeSource(Source source)
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(source.Author))
                parts.Add(source.Author.Trim());
            if (!string.IsNullOrWhiteSpace(source.Year))
                parts.Add($"({source.Year.Trim()})");
            var head = string.Join(" ", parts);
            if (!string.IsNullOrWhiteSpace(source.Title))
                head = head.Length > 0 ? $"{head} — {source.Title.Trim()}" : source.Title.Trim();
            if (head.Length == 0)
                head = string.IsNullOrWhiteSpace(source.Tag) ? "(untitled source)" : source.Tag.Trim();
            return head;
        }
    }

    // The fields captured by the NewSourceDialog (all trimmed; publisher may be empty).
    private sealed record SourceEntry(string Tag, string Author, string Title, string Year, string Publisher);

    // A small modal form capturing a new source's tag/author/title/year/publisher. Returns the entry, or
    // null if cancelled.
    private static class NewSourceDialog
    {
        public static SourceEntry? Ask(Window? owner)
        {
            var tag = NewField();
            var author = NewField();
            var title = NewField();
            var year = NewField();
            var publisher = NewField();

            SourceEntry? result = null;
            var dialog = new Window
            {
                Title = "Add New Source",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            ok.Click += (_, _) =>
            {
                result = new SourceEntry(
                    tag.Text.Trim(), author.Text.Trim(), title.Text.Trim(), year.Text.Trim(), publisher.Text.Trim());
                dialog.DialogResult = true;
            };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0)
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            AddRow(panel, "Tag (short id):", tag);
            AddRow(panel, "Author:", author);
            AddRow(panel, "Title:", title);
            AddRow(panel, "Year:", year);
            AddRow(panel, "Publisher (optional):", publisher);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            author.Focus();
            return dialog.ShowDialog() == true ? result : null;
        }

        private static System.Windows.Controls.TextBox NewField() =>
            new() { MinWidth = 320, Margin = new Thickness(0, 0, 0, 10) };

        private static void AddRow(System.Windows.Controls.Panel panel, string label, System.Windows.Controls.TextBox box)
        {
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(box);
        }
    }

    // Mailings: the shared mail-merge state across the four Mailings commands. Holds the data source
    // and, while previewing, the original template document plus the current record index so previewing
    // can step through records and restore the template when the preview ends.
    private sealed class MailMergeSession
    {
        public MergeData? Data { get; set; }

        // Non-null only while a preview is active: the document that was in the editor before the first
        // Preview, so leaving the preview restores it (the user's editable template).
        public TextDocument? Template { get; set; }

        public int CurrentIndex { get; set; }

        public bool IsPreviewing => Template is not null;
    }

    // Mailings > Insert Merge Field: prompt for a field name and insert the placeholder «Name» at the
    // caret as ordinary text (through the editor's normal edit/undo path, so it is reversible). The
    // guillemets are added automatically; a name the user already wrapped in « » is accepted as-is.
    private sealed class InsertMergeFieldCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var name = TextPrompt.Ask(Window.GetWindow(editor), "Insert Merge Field", "Field name:", string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                return; // cancelled or blank — nothing to insert

            var trimmed = name.Trim().Trim(MailMerge.FieldOpen, MailMerge.FieldClose).Trim();
            if (trimmed.Length == 0)
                return;

            editor.Focus();
            editor.InsertText($"{MailMerge.FieldOpen}{trimmed}{MailMerge.FieldClose}");
        }
    }

    // Mailings > Set Data: open a dialog to paste/type CSV (first line = headers). The parsed MergeData
    // is stored on the session. If the document already has merge fields, they are shown as a hint so the
    // user knows which columns to provide.
    private sealed class SetMergeDataCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.CommitToModel();
            var fields = MailMerge.FieldNames(editor.Model);
            var seed = session.Data is { } data ? DescribeAsCsv(data) : SeedFromFields(fields);

            var csv = MergeDataDialog.Ask(Window.GetWindow(editor), fields, seed);
            if (csv is null)
                return; // cancelled

            var parsed = MergeData.FromCsv(csv);
            session.Data = parsed;
            session.Template = null; // any in-progress preview is invalidated by new data
            session.CurrentIndex = 0;

            MessageBox.Show(Window.GetWindow(editor),
                $"Loaded {parsed.Count} record(s) with {parsed.Header.Count} field(s).",
                "Mail Merge", MessageBoxButton.OK, MessageBoxImage.Information);
            editor.Focus();
        }

        // Suggest a header line from the document's discovered fields so the user can fill rows in.
        private static string SeedFromFields(IReadOnlyList<string> fields) =>
            fields.Count == 0 ? string.Empty : string.Join(",", fields);

        // Render the current data back to CSV so re-opening the dialog shows what was entered.
        private static string DescribeAsCsv(MergeData data)
        {
            var lines = new List<string> { string.Join(",", data.Header.Select(CsvCell)) };
            foreach (var row in data.Rows)
                lines.Add(string.Join(",", data.Header.Select(h => CsvCell(row.TryGetValue(h, out var v) ? v : string.Empty))));
            return string.Join(Environment.NewLine, lines);
        }

        private static string CsvCell(string value) =>
            value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
    }

    // Mailings > Preview Record: load MergeRecord(template, currentRow) into the editor so the user sees
    // a real record. The original (template) document is stashed on first preview so stepping to the next
    // record re-renders from the template, and leaving the preview restores it. With no data, prompts the
    // user to Set Data first.
    private sealed class PreviewMergeRecordCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (session.Data is not { Count: > 0 } data)
            {
                MessageBox.Show(Window.GetWindow(editor),
                    "Set the merge data first (Mailings ▸ Set Data), then preview a record.",
                    "Mail Merge", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // On first preview, capture the editable template and immediately show record 0; subsequent
            // previews reuse the template and resume at the last viewed record.
            if (!session.IsPreviewing)
            {
                editor.CommitToModel();
                session.Template = editor.Model;
                session.CurrentIndex = 0;
            }

            var template = session.Template!;
            var index = Math.Clamp(session.CurrentIndex, 0, data.Count - 1);
            session.CurrentIndex = index;
            editor.LoadModel(MailMerge.MergeRecord(template, data.Rows[index]));

            var action = PreviewNavigationDialog.Ask(Window.GetWindow(editor), index, data.Count);
            switch (action.Kind)
            {
                case PreviewAction.Move:
                    index = Math.Clamp(action.TargetIndex, 0, data.Count - 1);
                    session.CurrentIndex = index;
                    editor.LoadModel(MailMerge.MergeRecord(template, data.Rows[index]));
                    break;
                case PreviewAction.Done:
                    // Restore the editable template so the user can keep editing fields.
                    editor.LoadModel(template);
                    session.Template = null;
                    break;
                case PreviewAction.Cancel:
                    // Leave whatever is currently shown; do not change the session.
                    break;
            }

            editor.Focus();
        }
    }

    // Mailings > Finish & Merge: produce the merged documents and load the concatenation of every record
    // into the editor as a single document (records separated by a page break), so the result is visible
    // and saveable. This replaces the editor's content; the template is no longer needed afterwards.
    private sealed class FinishMergeCommand(DocumentView editor, MailMergeSession session) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (session.Data is not { Count: > 0 } data)
            {
                MessageBox.Show(Window.GetWindow(editor),
                    "Set the merge data first (Mailings ▸ Set Data), then Finish & Merge.",
                    "Mail Merge", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Use the stashed template if previewing; otherwise the current editor content is the template.
            TextDocument template;
            if (session.IsPreviewing)
            {
                template = session.Template!;
            }
            else
            {
                editor.CommitToModel();
                template = editor.Model;
            }

            var merged = MailMerge.MergeAll(template, data);
            var combined = Concatenate(merged);

            editor.LoadModel(combined);
            session.Template = null;
            session.CurrentIndex = 0;

            MessageBox.Show(Window.GetWindow(editor),
                $"Merged {merged.Count} record(s) into a single document.",
                "Mail Merge", MessageBoxButton.OK, MessageBoxImage.Information);
            editor.Focus();
        }

        // Concatenate the per-record documents into one, starting each record (after the first) on a new
        // page. The first record's page settings / styles / header / footer carry the combined document.
        private static TextDocument Concatenate(IReadOnlyList<TextDocument> docs)
        {
            if (docs.Count == 0)
                return TextDocument.CreateEmpty();

            var first = docs[0];
            for (var d = 1; d < docs.Count; d++)
            {
                var blocks = docs[d].Blocks;
                // Force a page break before each subsequent record's first paragraph (Word's "Start each
                // record on a new page"). Falls back to a dedicated break paragraph if the record leads
                // with a non-paragraph block (e.g. a table).
                if (blocks.Count > 0 && blocks[0] is FreeW.Core.Model.Paragraph lead)
                {
                    lead.Formatting = lead.Formatting with { PageBreakBefore = true };
                }
                else
                {
                    first.Blocks.Add(DocumentOps.CreatePageBreak());
                }

                foreach (var block in blocks)
                    first.Blocks.Add(block);
            }
            return first;
        }
    }

    // The user's choice from the preview navigation dialog.
    private enum PreviewAction { Move, Done, Cancel }

    private readonly record struct PreviewChoice(PreviewAction Kind, int TargetIndex);

    // A small modeless-feeling modal that shows the current record and offers Previous / Next / Done.
    // Returns a Move (to a new index), Done (end preview, restore template), or Cancel (no change).
    private static class PreviewNavigationDialog
    {
        public static PreviewChoice Ask(Window? owner, int index, int count)
        {
            var result = new PreviewChoice(PreviewAction.Cancel, index);
            var dialog = new Window
            {
                Title = "Preview Results",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var label = new System.Windows.Controls.TextBlock
            {
                Text = $"Record {index + 1} of {count}",
                Margin = new Thickness(0, 0, 0, 12)
            };

            var prev = new System.Windows.Controls.Button { Content = "◀ Previous", MinWidth = 88, Margin = new Thickness(0, 0, 8, 0), IsEnabled = index > 0 };
            var next = new System.Windows.Controls.Button { Content = "Next ▶", MinWidth = 88, Margin = new Thickness(0, 0, 8, 0), IsEnabled = index < count - 1 };
            var done = new System.Windows.Controls.Button { Content = "Done", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };

            prev.Click += (_, _) => { result = new PreviewChoice(PreviewAction.Move, index - 1); dialog.DialogResult = true; };
            next.Click += (_, _) => { result = new PreviewChoice(PreviewAction.Move, index + 1); dialog.DialogResult = true; };
            done.Click += (_, _) => { result = new PreviewChoice(PreviewAction.Done, index); dialog.DialogResult = true; };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(prev);
            buttons.Children.Add(next);
            buttons.Children.Add(done);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16), MinWidth = 320 };
            panel.Children.Add(label);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            if (dialog.ShowDialog() == true)
                return result;
            return new PreviewChoice(PreviewAction.Cancel, index);
        }
    }

    // A dialog to enter the mail-merge data as CSV (first line = headers). Shows the document's discovered
    // merge fields as a hint. Returns the CSV text, or null if cancelled.
    private static class MergeDataDialog
    {
        public static string? Ask(Window? owner, IReadOnlyList<string> fields, string seed)
        {
            var hint = fields.Count > 0
                ? "Fields in this document: " + string.Join(", ", fields)
                : "Tip: the first line is the header row of field names.";

            var box = new System.Windows.Controls.TextBox
            {
                Text = seed,
                AcceptsReturn = true,
                AcceptsTab = false,
                MinWidth = 420,
                MinHeight = 160,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(0, 0, 0, 12)
            };

            string? result = null;
            var dialog = new Window
            {
                Title = "Mail Merge Data",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.CanResize,
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
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Paste or type CSV (first line = field names):", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(box);
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = hint, Margin = new Thickness(0, 0, 0, 12), Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(buttons);
            dialog.Content = panel;

            box.Focus();
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

    // Layout > Watermark: prompt for the page watermark text (seeded with the current one). An empty
    // result clears the watermark. Delegates to the view, which mutates PageSettings and re-renders.
    private sealed class WatermarkCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var seed = editor.Model.Page.Watermark ?? string.Empty;
            var text = TextPrompt.Ask(Window.GetWindow(editor), "Watermark", "Watermark text (empty to remove):", seed);
            if (text is null)
                return; // cancelled — leave the model untouched

            editor.SetWatermark(text);
            editor.Focus();
        }
    }

    // Design > Page Background > Page Color (Word's Page Color): pick the whole-page background colour from
    // a theme-style swatch palette, clear it with "No Color", or open "More Colours…" to type a hex value.
    // The chosen value sets the model's page BackgroundColorHex through DocumentView.SetPageColor (commit +
    // re-render via ApplyPageSettings); it already round-trips as w:background in docx. Mirrors the swatch
    // picker used by Cell Shading / Paragraph Shading.
    private sealed class PageColorCommand(DocumentView editor) : IRibbonCommand
    {
        // Word's "Theme Colors" top row plus standard colours — a sensible page-tint palette.
        private static readonly string[] Palette =
        [
            "#FFFFFF", "#F2F2F2", "#DDD9C3", "#C6D9F1", "#DBE5F1", "#F2DCDB",
            "#EBF1DE", "#E5E0EC", "#FDE9D9", "#FFF2CC", "#DEEBF7", "#E2EFDA",
            "#FCE4D6", "#D9E1F2", "#FFFFCC", "#E2F0D9", "#000000", "#1F1F1F",
        ];

        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var owner = Window.GetWindow(editor);
            var (chosen, hex) = ShowPicker(owner);
            if (!chosen)
                return; // cancelled — leave the model untouched
            editor.Focus();
            editor.SetPageColor(hex); // null clears back to the default white sheet
        }

        private (bool Chosen, string? Hex) ShowPicker(Window? owner)
        {
            var chosen = false;
            string? hex = null;
            var window = new Window
            {
                Title = "Page Color",
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

            var noColor = new Button
            {
                Content = "No Color",
                Margin = new Thickness(2, 6, 2, 0),
                Padding = new Thickness(8, 2, 8, 2)
            };
            noColor.Click += (_, _) => { chosen = true; hex = null; window.Close(); };
            panel.Children.Add(noColor);

            var more = new Button
            {
                Content = "More Colors…",
                Margin = new Thickness(2, 4, 2, 0),
                Padding = new Thickness(8, 2, 8, 2)
            };
            more.Click += (_, _) =>
            {
                var seed = editor.Model.Page.BackgroundColorHex ?? "#";
                var typed = TextPrompt.Ask(window, "More Colors", "Hex colour (e.g. #FFCC00):", seed);
                if (typed is null)
                    return; // stay on the palette
                var normalized = NormalizeHex(typed);
                if (normalized is null)
                {
                    DialogMessageHelper.ShowWarning(window, "Enter a colour as a 6-digit hex value, e.g. #FFCC00.", "Page Color");
                    return;
                }
                chosen = true; hex = normalized; window.Close();
            };
            panel.Children.Add(more);

            window.Content = panel;
            window.ShowDialog();
            return (chosen, hex);
        }

        // Accept "#RRGGBB" / "RRGGBB" (case-insensitive); return a normalised "#RRGGBB" or null if invalid.
        private static string? NormalizeHex(string raw)
        {
            var value = raw.Trim().TrimStart('#');
            if (value.Length != 6)
                return null;
            foreach (var c in value)
            {
                if (!Uri.IsHexDigit(c))
                    return null;
            }
            return "#" + value.ToUpperInvariant();
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

    // Insert > Field: open a small picker listing the document field kinds (Date, Time, File Name,
    // Author, Number of Pages, Page Number) and drop the chosen field run at the caret.
    private sealed class InsertFieldCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var kind = FieldPickerDialog.Ask(Window.GetWindow(editor));
            if (kind is not { } chosen)
                return; // cancelled
            editor.InsertField(chosen);
        }
    }

    // A small modal dialog listing the insertable document field kinds. Returns the chosen
    // RunFieldKind, or null if cancelled.
    private static class FieldPickerDialog
    {
        private sealed record Choice(string Label, RunFieldKind Kind);

        public static RunFieldKind? Ask(Window? owner)
        {
            var choices = new[]
            {
                new Choice("Date", RunFieldKind.Date),
                new Choice("Time", RunFieldKind.Time),
                new Choice("File Name", RunFieldKind.FileName),
                new Choice("Author", RunFieldKind.Author),
                new Choice("Number of Pages", RunFieldKind.NumPages),
                new Choice("Page Number", RunFieldKind.PageNumber),
            };

            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 240,
                MinHeight = 140,
                Margin = new Thickness(0, 0, 0, 12)
            };
            foreach (var choice in choices)
                list.Items.Add(choice.Label);
            list.SelectedIndex = 0;

            RunFieldKind? result = null;
            var dialog = new Window
            {
                Title = "Insert Field",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            void Commit()
            {
                if (list.SelectedIndex >= 0)
                    result = choices[list.SelectedIndex].Kind;
                dialog.DialogResult = true;
            }
            ok.Click += (_, _) => Commit();
            list.MouseDoubleClick += (_, _) => Commit();

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16), MinWidth = 240 };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Choose a field to insert:", Margin = new Thickness(0, 0, 0, 8) });
            panel.Children.Add(list);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? result : null;
        }
    }

    // Home > Paragraph > Sort: open the Sort dialog (type + order + case + header-row) and sort either
    // the rows of the table at the caret (by the caret's column, matching Word) or the selected
    // paragraphs. The view routes the reorder through its undo/redo bus and re-renders.
    private sealed class SortCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var inTable = editor.IsCaretInTable();
            var choice = SortDialog.Prompt(Window.GetWindow(editor), forTable: inTable);
            if (choice is null)
                return; // cancelled

            editor.Focus();
            var c = choice.Value;
            if (inTable)
                editor.SortCaretTableRows(c.Kind, c.Ascending, c.CaseSensitive, c.HasHeaderRow);
            else
                editor.SortSelectedParagraphs(c.Kind, c.Ascending, c.CaseSensitive, c.HasHeaderRow);
        }
    }

    // Layout > Convert Text to Table: ask for a delimiter, then turn the selected paragraphs into a
    // table (splitting each paragraph on that delimiter). The view routes the change through its bus.
    private sealed class TextToTableCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (DelimiterDialog.Ask(Window.GetWindow(editor), "Convert Text to Table") is not { } delimiter)
                return; // cancelled
            editor.Focus();
            editor.ConvertSelectionToTable(delimiter);
        }
    }

    // Layout > Convert Table to Text: ask for a delimiter, then turn the caret's table into delimited
    // paragraphs (one per row). The view routes the change through its bus.
    private sealed class TableToTextCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (DelimiterDialog.Ask(Window.GetWindow(editor), "Convert Table to Text") is not { } delimiter)
                return; // cancelled
            editor.Focus();
            editor.ConvertTableToText(delimiter);
        }
    }

    // A small modal dialog choosing the cell delimiter for text/table conversion: Tab, Comma, or
    // Semicolon. Returns the chosen delimiter character, or null if cancelled.
    private static class DelimiterDialog
    {
        private sealed record Choice(string Label, char Delimiter);

        public static char? Ask(Window? owner, string title)
        {
            var choices = new[]
            {
                new Choice("Tab", '\t'),
                new Choice("Comma  ,", ','),
                new Choice("Semicolon  ;", ';'),
            };

            var list = new System.Windows.Controls.ListBox
            {
                MinWidth = 240,
                MinHeight = 90,
                Margin = new Thickness(0, 0, 0, 12)
            };
            foreach (var choice in choices)
                list.Items.Add(choice.Label);
            list.SelectedIndex = 0;

            char? result = null;
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
            void Commit()
            {
                var index = list.SelectedIndex;
                if (index >= 0 && index < choices.Length)
                {
                    result = choices[index].Delimiter;
                    dialog.DialogResult = true;
                }
            }
            ok.Click += (_, _) => Commit();
            list.MouseDoubleClick += (_, _) => Commit();

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Separate cells at:", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(list);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? result : null;
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

    // A tiny modal dialog asking for a single line of text (a URL, a ScreenTip, …). Returns the entered
    // text, or null if cancelled. Title/label default to the insert-link wording for existing callers.
    private static class HyperlinkPrompt
    {
        public static string? Ask(Window? owner, string seed, string title = "Insert Link", string label = "Address:")
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

    // References > Citation Style: set the editor's active citation style from the combo box label
    // ("APA"/"MLA"/"Chicago"/"IEEE"). The style is stored on the document (TextDocument.BibliographyStyle via
    // DocumentView.ActiveCitationStyle) so it persists and reformats subsequently inserted in-text citations
    // and bibliographies. Unrecognised labels leave the current style unchanged.
    private sealed class CitationStyleCommand(DocumentView editor) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!context.Parameters.TryGetValue("value", out var raw) || raw is not string value)
                return;

            editor.ActiveCitationStyle = Citations.ParseStyle(value, editor.ActiveCitationStyle);
        }
    }

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
