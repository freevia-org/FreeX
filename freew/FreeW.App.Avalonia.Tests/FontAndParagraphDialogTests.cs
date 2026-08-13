using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using Free.Shared.Ribbon;
using static FreeW.App.Avalonia.Tests.DialogWorkflowResultFactory;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Headless tests for the Font and Paragraph dialog apply logic.
///
/// The dialogs themselves are modal Avalonia windows (can't click headlessly),
/// but their apply methods (<see cref="FontDialog.ApplyResult"/> and
/// <see cref="ParagraphDialog.ApplyResult"/>) are static and fully testable
/// without showing a window.
///
/// Launcher commands are also verified to resolve in the ribbon registry.
/// </summary>
public sealed class FontAndParagraphDialogTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TextDocument MakeDoc(string text = "Hello world")
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));
        return doc;
    }

    private static FreeWRibbonHostExecutionPorts NoopCallbacks() =>
        new(
            Open: () => { },
            Save: () => { },
            Cut: () => { },
            Copy: () => { },
            Paste: () => { },
            Backstage: () => { },
            NewDocument: () => { },
            ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { },
            ToggleRevealFormatting: () => { },
            OpenFindReplaceDialog: () => { },
            SetPrintLayout: () => { },
            SetWebLayout: () => { },
            SetDraftView: () => { },
            OpenFontDialog: () => { },
            OpenParagraphDialog: () => { },
            OpenPageSetupDialog: () => { },
            ToggleOrientation:   () => { },
            ApplyMarginPreset:   _ => { },
            ApplyPaperSize:      _ => { },
            InsertPicture:       () => { },
            OpenWordCountDialog: () => { },
            ApplyZoom: (_, _) => { });

    // ── Launcher command ids resolve in the registry ──────────────────────────

    [Fact]
    public void Font_dialog_launcher_command_is_registered()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());
        registry.TryGet(new RibbonCommandId("freew.font-dialog"), out _)
            .Should().BeTrue("freew.font-dialog must be registered in the ribbon command registry");
    }

    [Fact]
    public void Paragraph_dialog_launcher_command_is_registered()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());
        registry.TryGet(new RibbonCommandId("freew.paragraph-dialog"), out _)
            .Should().BeTrue("freew.paragraph-dialog must be registered in the ribbon command registry");
    }

    [Fact]
    public void Ribbon_definition_contains_font_dialog_button()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var allIds = definition.Tabs
            .SelectMany(t => t.Groups)
            .SelectMany(g => g.Controls)
            .Select(c => c switch
            {
                RibbonButton b => b.CommandId.Value,
                _ => null,
            })
            .Where(id => id is not null)
            .ToList();

        allIds.Should().Contain("freew.font-dialog",
            "the Font group in the Home tab must have a 'Font…' button");
    }

    [Fact]
    public void Ribbon_definition_contains_paragraph_dialog_button()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var allIds = definition.Tabs
            .SelectMany(t => t.Groups)
            .SelectMany(g => g.Controls)
            .Select(c => c switch
            {
                RibbonButton b => b.CommandId.Value,
                _ => null,
            })
            .Where(id => id is not null)
            .ToList();

        allIds.Should().Contain("freew.paragraph-dialog",
            "the Paragraph group in the Home tab must have a 'Paragraph…' button");
    }

    // ── FontDialog.ApplyResult: apply changes to the editor model ────────────

    [Fact]
    public void FontDialog_apply_sets_bold_when_result_bold_differs_from_original()
    {
        var doc = MakeDoc("Test");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        var original = RunFormatting.Default; // Bold = false
        var result = FontResult(
            Family: null, SizePt: null,
            Bold: true, Italic: false, Underline: false, Strikethrough: false,
            VerticalAlign: VerticalAlign.Baseline,
            SmallCaps: false, AllCaps: false,
            ColorHex: null, HighlightHex: null);

        FontDialog.ApplyResult(view, result, original);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(r => r.Formatting.Bold)
            .Should().BeTrue("ApplyResult should toggle bold on when result.Bold = true and original.Bold = false");
    }

    [Fact]
    public void FontDialog_apply_sets_font_size_when_changed()
    {
        var doc = MakeDoc("Size test");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        var original = RunFormatting.Default; // FontSizePt = null
        var result = FontResult(
            Family: null, SizePt: 14.0,
            Bold: false, Italic: false, Underline: false, Strikethrough: false,
            VerticalAlign: VerticalAlign.Baseline,
            SmallCaps: false, AllCaps: false,
            ColorHex: null, HighlightHex: null);

        FontDialog.ApplyResult(view, result, original);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(r => r.Formatting.FontSizePt == 14.0)
            .Should().BeTrue("ApplyResult should set font size to 14pt");
    }

    [Fact]
    public void FontDialog_apply_sets_italic_and_underline()
    {
        var doc = MakeDoc("IU test");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        var original = RunFormatting.Default;
        var result = FontResult(
            Family: null, SizePt: null,
            Bold: false, Italic: true, Underline: true, Strikethrough: false,
            VerticalAlign: VerticalAlign.Baseline,
            SmallCaps: false, AllCaps: false,
            ColorHex: null, HighlightHex: null);

        FontDialog.ApplyResult(view, result, original);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(r => r.Formatting.Italic && r.Formatting.Underline)
            .Should().BeTrue("ApplyResult should set italic and underline");
    }

    [Fact]
    public void FontDialog_apply_sets_superscript()
    {
        var doc = MakeDoc("Super");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        var original = RunFormatting.Default; // Baseline
        var result = FontResult(
            Family: null, SizePt: null,
            Bold: false, Italic: false, Underline: false, Strikethrough: false,
            VerticalAlign: VerticalAlign.Superscript,
            SmallCaps: false, AllCaps: false,
            ColorHex: null, HighlightHex: null);

        FontDialog.ApplyResult(view, result, original);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(r => r.Formatting.VerticalAlign == VerticalAlign.Superscript)
            .Should().BeTrue("ApplyResult should set superscript vertical align");
    }

    [Fact]
    public void FontDialog_apply_sets_subscript()
    {
        var doc = MakeDoc("Sub");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        var original = RunFormatting.Default; // Baseline
        var result = FontResult(
            Family: null, SizePt: null,
            Bold: false, Italic: false, Underline: false, Strikethrough: false,
            VerticalAlign: VerticalAlign.Subscript,
            SmallCaps: false, AllCaps: false,
            ColorHex: null, HighlightHex: null);

        FontDialog.ApplyResult(view, result, original);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(r => r.Formatting.VerticalAlign == VerticalAlign.Subscript)
            .Should().BeTrue("ApplyResult should set subscript vertical align");
    }

    [Fact]
    public void FontDialog_apply_sets_font_color()
    {
        var doc = MakeDoc("Color");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        var original = RunFormatting.Default; // ColorHex = null
        var result = FontResult(
            Family: null, SizePt: null,
            Bold: false, Italic: false, Underline: false, Strikethrough: false,
            VerticalAlign: VerticalAlign.Baseline,
            SmallCaps: false, AllCaps: false,
            ColorHex: "#FF0000", HighlightHex: null);

        FontDialog.ApplyResult(view, result, original);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(r => r.Formatting.ColorHex == "#FF0000")
            .Should().BeTrue("ApplyResult should set font color to #FF0000");
    }

    [Fact]
    public void FontDialog_apply_sets_strikethrough()
    {
        var doc = MakeDoc("Strike");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        var original = RunFormatting.Default;
        var result = FontResult(
            Family: null, SizePt: null,
            Bold: false, Italic: false, Underline: false, Strikethrough: true,
            VerticalAlign: VerticalAlign.Baseline,
            SmallCaps: false, AllCaps: false,
            ColorHex: null, HighlightHex: null);

        FontDialog.ApplyResult(view, result, original);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(r => r.Formatting.Strikethrough)
            .Should().BeTrue("ApplyResult should set strikethrough");
    }

    [Fact]
    public void FontDialog_apply_sets_double_strikethrough_independently()
    {
        var doc = MakeDoc("Double strike");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        var original = RunFormatting.Default;
        var result = FontResult(
            Family: null, SizePt: null,
            Bold: false, Italic: false, Underline: false, Strikethrough: false,
            VerticalAlign: VerticalAlign.Baseline,
            SmallCaps: false, AllCaps: false,
            ColorHex: null, HighlightHex: null,
            DoubleStrikethrough: true);

        FontDialog.ApplyResult(view, result, original);

        var formatting = ((Paragraph)view.Document.Blocks[0]).Runs.Single().Formatting;
        formatting.DoubleStrikethrough.Should().BeTrue();
        formatting.Strikethrough.Should().BeFalse();
    }

    [Fact]
    public void FontDialog_selection_state_reports_mixed_double_strikethrough()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("double", RunFormatting.Default with { DoubleStrikethrough = true }));
        paragraph.Runs.Add(new Run(" plain"));
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.SelectAll();

        view.GetSelectionFormatting().DoubleStrikethroughIndeterminate.Should().BeTrue();
    }

    [Fact]
    public void FontDialog_apply_sets_hidden_without_setting_web_hidden()
    {
        var doc = MakeDoc("Hide this");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        var original = RunFormatting.Default;
        var result = FontResult(
            Family: null, SizePt: null,
            Bold: false, Italic: false, Underline: false, Strikethrough: false,
            VerticalAlign: VerticalAlign.Baseline,
            SmallCaps: false, AllCaps: false,
            ColorHex: null, HighlightHex: null,
            Hidden: true);

        FontDialog.ApplyResult(view, result, original);

        var formatting = ((Paragraph)view.Document.Blocks[0]).Runs.Single().Formatting;
        formatting.Hidden.Should().BeTrue();
        formatting.WebHidden.Should().BeFalse();
    }

    [Fact]
    public void FontDialog_selection_state_reports_mixed_hidden_text()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("hidden", RunFormatting.Default with { Hidden = true }));
        paragraph.Runs.Add(new Run(" visible"));
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.SelectAll();

        view.GetSelectionFormatting().HiddenIndeterminate.Should().BeTrue();
    }

    [Fact]
    public void FontDialog_apply_sets_font_family()
    {
        var doc = MakeDoc("Family");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        var original = RunFormatting.Default; // FontFamily = null
        var result = FontResult(
            Family: "Arial", SizePt: null,
            Bold: false, Italic: false, Underline: false, Strikethrough: false,
            VerticalAlign: VerticalAlign.Baseline,
            SmallCaps: false, AllCaps: false,
            ColorHex: null, HighlightHex: null);

        FontDialog.ApplyResult(view, result, original);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(r => r.Formatting.FontFamily == "Arial")
            .Should().BeTrue("ApplyResult should set font family to Arial");
    }

    [Fact]
    public void FontDialog_apply_noop_when_nothing_changed()
    {
        // Applying a result that exactly matches the original should leave the model unchanged.
        var doc = MakeDoc("Noop");
        var para = (Paragraph)doc.Blocks[0];
        var original = new RunFormatting { Bold = true, FontSizePt = 12, ColorHex = "#0000FF" };
        para.Runs.Clear();
        para.Runs.Add(new Run("Noop", original));

        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        var result = FontResult(
            Family: null, SizePt: 12.0,
            Bold: true, Italic: false, Underline: false, Strikethrough: false,
            VerticalAlign: VerticalAlign.Baseline,
            SmallCaps: false, AllCaps: false,
            ColorHex: "#0000FF", HighlightHex: null);

        FontDialog.ApplyResult(view, result, original);

        // Bold should remain true (toggle was NOT called because nothing changed).
        var resultPara = (Paragraph)view.Document.Blocks[0];
        resultPara.Runs.All(r => r.Formatting.Bold)
            .Should().BeTrue("when bold is already set and result also has bold, ApplyResult must not toggle it off");
    }

    // ── ParagraphDialog.ApplyResult: apply changes to the editor model ────────

    [Fact]
    public void FontDialog_apply_sets_advanced_typography_fields()
    {
        var doc = MakeDoc("Advanced typography");
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll();

        var original = RunFormatting.Default;
        var result = FontResult(
            Family: null,
            SizePt: null,
            Bold: false,
            Italic: false,
            Underline: false,
            Strikethrough: false,
            VerticalAlign: VerticalAlign.Baseline,
            SmallCaps: false,
            AllCaps: false,
            ColorHex: null,
            HighlightHex: null,
            FamilyChanged: false,
            SizeChanged: false,
            CharacterSpacingPt: 1.5,
            KerningMinSizePt: 12,
            PositionPt: 2,
            Ligatures: LigatureMode.StandardContextual,
            StylisticSet: 4,
            NumberForm: NumberForm.OldStyle,
            NumberSpacing: NumberSpacing.Tabular,
            AdvancedChanged: true);

        FontDialog.ApplyResult(view, result, original);

        var formatting = ((Paragraph)view.Document.Blocks[0]).Runs.Single().Formatting;
        formatting.CharacterSpacingPt.Should().Be(1.5);
        formatting.KerningMinSizePt.Should().Be(12);
        formatting.PositionPt.Should().Be(2);
        formatting.Ligatures.Should().Be(LigatureMode.StandardContextual);
        formatting.StylisticSet.Should().Be(4);
        formatting.NumberForm.Should().Be(NumberForm.OldStyle);
        formatting.NumberSpacing.Should().Be(NumberSpacing.Tabular);
    }

    [Fact]
    public void ParagraphDialog_apply_preserves_alignment()
    {
        var doc = MakeDoc("Center me");
        ((Paragraph)doc.Blocks[0]).Formatting = ParagraphFormatting.Default with
        {
            Alignment = TextAlignment.Center,
        };
        var view = new DocumentView();
        view.LoadDocument(doc);

        var result = ParagraphResult(
            Alignment: TextAlignment.Center,
            IndentLeftPt: 0, IndentRightPt: 0, FirstLineIndentPt: 0,
            SpaceBeforePt: 0, SpaceAfterPt: 8,
            LineRule: LineSpacingRule.Multiple, LineSpacingValue: 1.15);

        ParagraphDialog.ApplyResult(view, result);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Formatting.Alignment.Should().Be(TextAlignment.Center,
            "the canonical Paragraph dialog does not expose alignment");
    }

    [Fact]
    public void ParagraphDialog_apply_sets_space_before()
    {
        var doc = MakeDoc("Space before");
        var view = new DocumentView();
        view.LoadDocument(doc);

        var original = ParagraphFormatting.Default; // SpaceBeforePt = 0
        var result = ParagraphResult(
            Alignment: TextAlignment.Left,
            IndentLeftPt: 0, IndentRightPt: 0, FirstLineIndentPt: 0,
            SpaceBeforePt: 12, SpaceAfterPt: 8,
            LineRule: LineSpacingRule.Multiple, LineSpacingValue: 1.15);

        ParagraphDialog.ApplyResult(view, result);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Formatting.SpaceBeforePt.Should().Be(12,
            "ApplyResult should set space-before to 12pt");
    }

    [Fact]
    public void ParagraphDialog_apply_sets_space_after()
    {
        var doc = MakeDoc("Space after");
        var view = new DocumentView();
        view.LoadDocument(doc);

        var original = ParagraphFormatting.Default; // SpaceAfterPt = 8
        var result = ParagraphResult(
            Alignment: TextAlignment.Left,
            IndentLeftPt: 0, IndentRightPt: 0, FirstLineIndentPt: 0,
            SpaceBeforePt: 0, SpaceAfterPt: 24,
            LineRule: LineSpacingRule.Multiple, LineSpacingValue: 1.15);

        ParagraphDialog.ApplyResult(view, result);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Formatting.SpaceAfterPt.Should().Be(24,
            "ApplyResult should set space-after to 24pt");
    }

    [Fact]
    public void ParagraphDialog_apply_sets_double_line_spacing()
    {
        var doc = MakeDoc("Double space");
        var view = new DocumentView();
        view.LoadDocument(doc);

        var original = ParagraphFormatting.Default; // Multiple 1.15
        var result = ParagraphResult(
            Alignment: TextAlignment.Left,
            IndentLeftPt: 0, IndentRightPt: 0, FirstLineIndentPt: 0,
            SpaceBeforePt: 0, SpaceAfterPt: 8,
            LineRule: LineSpacingRule.Multiple, LineSpacingValue: 2.0);

        ParagraphDialog.ApplyResult(view, result);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Formatting.LineSpacing.Should().BeApproximately(2.0, 0.01,
            "ApplyResult should set line spacing to 2× multiple");
    }

    [Fact]
    public void ParagraphDialog_apply_uses_canonical_multiple_line_spacing()
    {
        var doc = MakeDoc("Exact space");
        ((Paragraph)doc.Blocks[0]).Formatting = ParagraphFormatting.Default with
        {
            LineRule = LineSpacingRule.Exact,
            LineHeightPt = 18,
        };
        var view = new DocumentView();
        view.LoadDocument(doc);

        var result = ParagraphResult(
            Alignment: TextAlignment.Left,
            IndentLeftPt: 0, IndentRightPt: 0, FirstLineIndentPt: 0,
            SpaceBeforePt: 0, SpaceAfterPt: 8,
            LineRule: LineSpacingRule.Exact, LineSpacingValue: 18);

        ParagraphDialog.ApplyResult(view, result);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Formatting.LineRule.Should().Be(LineSpacingRule.Multiple,
            "the canonical Paragraph dialog exposes a line-spacing multiplier");
        para.Formatting.LineSpacing.Should().BeApproximately(18, 0.01);
    }

    [Fact]
    public void ParagraphDialog_apply_sets_left_indent()
    {
        var doc = MakeDoc("Indented");
        var view = new DocumentView();
        view.LoadDocument(doc);

        var original = ParagraphFormatting.Default;
        var result = ParagraphResult(
            Alignment: TextAlignment.Left,
            IndentLeftPt: 36, IndentRightPt: 0, FirstLineIndentPt: 0,
            SpaceBeforePt: 0, SpaceAfterPt: 8,
            LineRule: LineSpacingRule.Multiple, LineSpacingValue: 1.15);

        ParagraphDialog.ApplyResult(view, result);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Formatting.IndentLeftPt.Should().BeApproximately(36, 0.01,
            "ApplyResult should set left indent to 36pt");
    }

    [Fact]
    public void ParagraphDialog_apply_sets_first_line_indent()
    {
        var doc = MakeDoc("First line indent");
        var view = new DocumentView();
        view.LoadDocument(doc);

        var original = ParagraphFormatting.Default;
        var result = ParagraphResult(
            Alignment: TextAlignment.Left,
            IndentLeftPt: 0, IndentRightPt: 0, FirstLineIndentPt: 18,
            SpaceBeforePt: 0, SpaceAfterPt: 8,
            LineRule: LineSpacingRule.Multiple, LineSpacingValue: 1.15);

        ParagraphDialog.ApplyResult(view, result);

        var para = (Paragraph)view.Document.Blocks[0];
        para.Formatting.FirstLineIndentPt.Should().BeApproximately(18, 0.01,
            "ApplyResult should set first-line indent to 18pt");
    }

    [Fact]
    public void ParagraphDialog_apply_marks_widow_control_as_explicit_even_when_cleared()
    {
        var doc = MakeDoc("Widow control");
        var view = new DocumentView();
        view.LoadDocument(doc);

        var result = new ParagraphBreaksDialogResult(
            LeftPt: 0,
            RightPt: 0,
            FirstLinePt: 0,
            SpaceBeforePt: 0,
            SpaceAfterPt: 8,
            LineSpacing: 1.15,
            KeepWithNext: false,
            KeepLinesTogether: false,
            WidowControl: false,
            PageBreakBefore: false,
            SuppressAutoHyphens: false,
            SuppressLineNumbers: false,
            ContextualSpacing: false);

        ParagraphDialog.ApplyResult(view, result);

        var formatting = ((Paragraph)view.Document.Blocks[0]).Formatting;
        formatting.WidowControl.Should().BeFalse();
        formatting.WidowControlIsSet.Should().BeTrue(
            "the WPF Paragraph dialog writes an explicit widow-control off value");
        formatting.SuppressAutoHyphens.Should().BeFalse();
        formatting.SuppressAutoHyphensIsSet.Should().BeTrue(
            "the paragraph dialog writes an explicit auto-hyphenation suppression override");
    }

    [Fact]
    public void ParagraphDialog_apply_noop_when_nothing_changed()
    {
        // If result exactly matches original, no SetSpaceBefore / SetAlignment etc. calls.
        var doc = MakeDoc("Noop para");
        var view = new DocumentView();
        view.LoadDocument(doc);

        var original = new ParagraphFormatting
        {
            Alignment = TextAlignment.Right,
            SpaceBeforePt = 6,
            SpaceAfterPt = 6,
            LineSpacing = 1.5,
            LineRule = LineSpacingRule.Multiple,
        };
        var para = (Paragraph)doc.Blocks[0];
        para.Formatting = original;
        view.LoadDocument(doc);

        var result = ParagraphResult(
            Alignment: TextAlignment.Right,
            IndentLeftPt: 0, IndentRightPt: 0, FirstLineIndentPt: 0,
            SpaceBeforePt: 6, SpaceAfterPt: 6,
            LineRule: LineSpacingRule.Multiple, LineSpacingValue: 1.5);

        ParagraphDialog.ApplyResult(view, result);

        var resultPara = (Paragraph)view.Document.Blocks[0];
        resultPara.Formatting.Alignment.Should().Be(TextAlignment.Right,
            "alignment must not change when already matching");
        resultPara.Formatting.SpaceBeforePt.Should().Be(6,
            "space-before must not change when already matching");
    }

    // ── ParagraphDialog.ApplyResult: multi-paragraph selection ───────────────

    /// <summary>
    /// Build a document with three editable body paragraphs and return it together with a DocumentView
    /// that has ALL three paragraphs selected (SelectAll).
    /// </summary>
    private static (TextDocument Doc, DocumentView View) MakeThreeParaDoc()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("First paragraph"));
        doc.Blocks.Add(new Paragraph("Second paragraph"));
        doc.Blocks.Add(new Paragraph("Third paragraph"));
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.SelectAll(); // spans all three blocks
        return (doc, view);
    }

    [Fact]
    public void ParagraphDialog_apply_preserves_alignment_on_all_selected_paragraphs()
    {
        var (doc, view) = MakeThreeParaDoc();
        foreach (var paragraph in doc.Blocks.OfType<Paragraph>())
            paragraph.Formatting = paragraph.Formatting with { Alignment = TextAlignment.Center };

        var result = ParagraphResult(
            Alignment: TextAlignment.Center,
            IndentLeftPt: 0, IndentRightPt: 0, FirstLineIndentPt: 0,
            SpaceBeforePt: 0, SpaceAfterPt: 8,
            LineRule: LineSpacingRule.Multiple, LineSpacingValue: 1.15);

        ParagraphDialog.ApplyResult(view, result);

        for (var i = 0; i < 3; i++)
        {
            var para = (Paragraph)doc.Blocks[i];
            para.Formatting.Alignment.Should().Be(TextAlignment.Center,
                $"paragraph {i} alignment is outside the canonical dialog contract");
        }
    }

    [Fact]
    public void ParagraphDialog_apply_sets_space_before_on_all_selected_paragraphs()
    {
        var (doc, view) = MakeThreeParaDoc();

        var original = ParagraphFormatting.Default; // SpaceBeforePt = 0
        var result = ParagraphResult(
            Alignment: TextAlignment.Left,
            IndentLeftPt: 0, IndentRightPt: 0, FirstLineIndentPt: 0,
            SpaceBeforePt: 12, SpaceAfterPt: 8,
            LineRule: LineSpacingRule.Multiple, LineSpacingValue: 1.15);

        ParagraphDialog.ApplyResult(view, result);

        for (var i = 0; i < 3; i++)
        {
            var para = (Paragraph)doc.Blocks[i];
            para.Formatting.SpaceBeforePt.Should().Be(12,
                $"paragraph {i} should have SpaceBeforePt=12 after multi-paragraph apply");
        }
    }

    [Fact]
    public void ParagraphDialog_apply_multi_paragraph_preserves_alignment_and_sets_space_before()
    {
        // The main regression test: selecting 3 paragraphs then applying Center + SpaceBefore=12
        // via the dialog must change ALL 3 paragraphs, not just the caret's paragraph.
        var (doc, view) = MakeThreeParaDoc();
        foreach (var paragraph in doc.Blocks.OfType<Paragraph>())
            paragraph.Formatting = paragraph.Formatting with { Alignment = TextAlignment.Center };

        var original = ParagraphFormatting.Default;
        var result = ParagraphResult(
            Alignment: TextAlignment.Center,
            IndentLeftPt: 0, IndentRightPt: 0, FirstLineIndentPt: 0,
            SpaceBeforePt: 12, SpaceAfterPt: 8,
            LineRule: LineSpacingRule.Multiple, LineSpacingValue: 1.15);

        ParagraphDialog.ApplyResult(view, result);

        for (var i = 0; i < 3; i++)
        {
            var para = (Paragraph)doc.Blocks[i];
            para.Formatting.Alignment.Should().Be(TextAlignment.Center,
                $"paragraph {i} alignment should be preserved");
            para.Formatting.SpaceBeforePt.Should().Be(12,
                $"paragraph {i} SpaceBeforePt should be 12");
        }
    }

    [Fact]
    public void ParagraphDialog_apply_single_undo_reverts_all_selected_paragraphs()
    {
        // One Undo must revert all three paragraphs that were changed in a single apply.
        var (doc, view) = MakeThreeParaDoc();

        var originalSpacing = Enumerable.Range(0, 3)
            .Select(i => ((Paragraph)doc.Blocks[i]).Formatting.SpaceBeforePt)
            .ToArray();

        var original = ParagraphFormatting.Default;
        var result = ParagraphResult(
            Alignment: TextAlignment.Right,
            IndentLeftPt: 0, IndentRightPt: 0, FirstLineIndentPt: 0,
            SpaceBeforePt: 6, SpaceAfterPt: 8,
            LineRule: LineSpacingRule.Multiple, LineSpacingValue: 1.15);

        ParagraphDialog.ApplyResult(view, result);

        // Verify all three were changed by the canonical spacing field.
        for (var i = 0; i < 3; i++)
            ((Paragraph)doc.Blocks[i]).Formatting.SpaceBeforePt.Should().Be(6,
                $"paragraph {i} should have spacing after apply");

        // A single Undo should revert all three.
        view.CanUndo.Should().BeTrue("there should be a command to undo after apply");
        view.Undo();

        // All three should be reverted.
        for (var i = 0; i < 3; i++)
        {
            var para = (Paragraph)doc.Blocks[i];
            para.Formatting.SpaceBeforePt.Should().Be(originalSpacing[i],
                $"paragraph {i} spacing should be reverted after single Undo");
        }

        // Undo queue should be empty (one composite command was the only action).
        view.CanUndo.Should().BeFalse("the single composite command should have been fully undone");
    }

    [Fact]
    public void ParagraphDialog_apply_caret_only_still_formats_single_paragraph()
    {
        // When there is no selection (caret only), only the current paragraph is changed.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Para A"));
        doc.Blocks.Add(new Paragraph("Para B"));
        doc.Blocks.Add(new Paragraph("Para C"));
        var view = new DocumentView();
        view.LoadDocument(doc);
        // No SelectAll → caret only at block 0.

        var original = ParagraphFormatting.Default;
        var result = ParagraphResult(
            Alignment: TextAlignment.Center,
            IndentLeftPt: 0, IndentRightPt: 0, FirstLineIndentPt: 0,
            SpaceBeforePt: 12, SpaceAfterPt: 8,
            LineRule: LineSpacingRule.Multiple, LineSpacingValue: 1.15);

        ParagraphDialog.ApplyResult(view, result);

        ((Paragraph)doc.Blocks[0]).Formatting.SpaceBeforePt.Should().Be(12,
            "caret paragraph (block 0) should be changed");
        ((Paragraph)doc.Blocks[1]).Formatting.SpaceBeforePt.Should().Be(0,
            "block 1 should be unchanged (caret-only apply)");
        ((Paragraph)doc.Blocks[2]).Formatting.SpaceBeforePt.Should().Be(0,
            "block 2 should be unchanged (caret-only apply)");
    }

    // ── GetCaretFormatting round-trip ─────────────────────────────────────────

    [Fact]
    public void GetCaretFormatting_returns_run_and_paragraph_formatting()
    {
        // Verify that GetCaretFormatting (used to pre-populate dialogs) returns the correct types.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph
        {
            Formatting = new ParagraphFormatting { Alignment = TextAlignment.Center, SpaceBeforePt = 6 },
        };
        para.Runs.Add(new Run("Hello", new RunFormatting { Bold = true, FontSizePt = 14 }));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadDocument(doc);

        var (runFmt, paraFmt) = view.GetCaretFormatting();

        // Run formatting is resolved; bold and size should be present.
        runFmt.Bold.Should().BeTrue("caret run formatting should reflect the run's Bold=true");
        runFmt.FontSizePt.Should().Be(14, "caret run formatting should reflect the run's 14pt size");

        // Paragraph formatting should reflect the paragraph's settings.
        paraFmt.Alignment.Should().Be(TextAlignment.Center,
            "caret paragraph formatting should reflect Center alignment");
    }
}
