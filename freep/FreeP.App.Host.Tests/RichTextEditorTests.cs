using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;
using ModelParagraph = FreeP.Core.Model.Paragraph;
using ModelRun       = FreeP.Core.Model.Run;
using WpfParagraph  = System.Windows.Documents.Paragraph;
using WpfRun        = System.Windows.Documents.Run;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 10A: tests for per-run rich-text in-canvas editing.
///
/// Coverage:
///  1. TextBodyFlowDocumentConverter round-trip (pure logic, no live RichTextBox needed).
///  2. Applying bold to a sub-range produces correctly split runs on commit.
///  3. Host StaFact: InCanvasTextEditor activates over a multi-run shape without throwing.
///  4. SetShapeTextBodyCommand applies and reverts correctly.
/// </summary>
public sealed class RichTextEditorTests
{
    // ─── TextBody → FlowDocument → TextBody round-trips ──────────────────────────

    [StaFact]
    public void Converter_TwoRunBody_RoundTrips_BoldAndColor()
    {
        // Arrange: TextBody with two runs, one bold+red, one plain.
        var body = MakeTwoRunBody();

        // Act: ToFlowDocument then FromFlowDocument.
        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 14);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        // Assert: paragraph count preserved.
        restored.Paragraphs.Should().HaveCount(1);

        var para = restored.Paragraphs[0];
        para.Runs.Should().HaveCount(2);

        // Run 0: bold red "Hello".
        var r0 = para.Runs[0];
        r0.Text.Should().Be("Hello");
        r0.Bold.Should().BeTrue("run 0 is bold");
        r0.Color.Should().NotBeNull("run 0 has an explicit color");
        r0.Color!.Resolved.R.Should().BeGreaterThan(200, "red channel is high (was 0xFF)");

        // Run 1: plain " world".
        var r1 = para.Runs[1];
        r1.Text.Should().Be(" world");
        r1.Bold.Should().BeFalse("run 1 is not bold");
    }

    [StaFact]
    public void Converter_TwoRunBody_RoundTrips_FontSizePreserved()
    {
        var body = MakeTwoRunBody();
        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 14);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        var r0 = restored.Paragraphs[0].Runs[0];
        r0.FontSizePt.Should().NotBeNull();
        r0.FontSizePt!.Value.Should().BeApproximately(24.0, 0.1, "first run was 24pt");
    }

    [StaFact]
    public void Converter_MultiParagraph_PreservesAlignmentAndRunCount()
    {
        // Two paragraphs: first left-aligned (2 runs), second center-aligned (1 run).
        var body = new TextBody { Wrap = true };

        var p0 = new ModelParagraph { Align = TextAlign.Left };
        p0.Runs.Add(new ModelRun { Text = "Left ", Bold = true });
        p0.Runs.Add(new ModelRun { Text = "para" });
        body.Paragraphs.Add(p0);

        var p1 = new ModelParagraph { Align = TextAlign.Center };
        p1.Runs.Add(new ModelRun { Text = "Centered", Italic = true });
        body.Paragraphs.Add(p1);

        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        restored.Paragraphs.Should().HaveCount(2);

        restored.Paragraphs[0].Align.Should().Be(TextAlign.Left);
        restored.Paragraphs[0].Runs.Should().HaveCount(2);
        restored.Paragraphs[0].Runs[0].Bold.Should().BeTrue();
        restored.Paragraphs[0].Runs[1].Bold.Should().BeFalse();

        restored.Paragraphs[1].Align.Should().Be(TextAlign.Center);
        restored.Paragraphs[1].Runs[0].Italic.Should().BeTrue();
    }

    [StaFact]
    public void Converter_EmptyBody_ReturnsOneParagraphOneEmptyRun()
    {
        var body     = new TextBody();
        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc);

        restored.Paragraphs.Should().HaveCount(1);
        restored.Paragraphs[0].Runs.Should().HaveCount(1);
        restored.Paragraphs[0].Runs[0].Text.Should().BeEmpty();
    }

    [StaFact]
    public void Converter_NullBody_ReturnsOneParagraphOneEmptyRun()
    {
        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(null);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc);

        restored.Paragraphs.Should().HaveCount(1);
        restored.Paragraphs[0].Runs.Should().HaveCount(1);
        restored.Paragraphs[0].Runs[0].Text.Should().BeEmpty();
    }

    // ─── Bold applied to a sub-range splits runs correctly on commit ──────────

    [StaFact]
    public void BoldSubRangeApplication_SplitsRuns_OnDocumentRead()
    {
        // Simulate: original body has one run "Hello world".
        // After editing the user bold-selects "world". The RichTextBox splits the run.
        // We verify FromFlowDocument reads the resulting multi-run paragraph correctly.

        var doc = new FlowDocument();
        var wp  = new System.Windows.Documents.Paragraph();
        // Normal run.
        var wr1 = new System.Windows.Documents.Run("Hello ") { FontWeight = FontWeights.Normal };
        // Bold run (simulates the user bolding "world").
        var wr2 = new System.Windows.Documents.Run("world") { FontWeight = FontWeights.Bold };
        wp.Inlines.Add(wr1);
        wp.Inlines.Add(wr2);
        doc.Blocks.Add(wp);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc);

        restored.Paragraphs.Should().HaveCount(1);
        restored.Paragraphs[0].Runs.Should().HaveCount(2, "two WPF runs produce two model runs");

        restored.Paragraphs[0].Runs[0].Text.Should().Be("Hello ");
        restored.Paragraphs[0].Runs[0].Bold.Should().BeFalse();

        restored.Paragraphs[0].Runs[1].Text.Should().Be("world");
        restored.Paragraphs[0].Runs[1].Bold.Should().BeTrue();
    }

    // ─── SetShapeTextBodyCommand apply/revert ─────────────────────────────────

    [Fact]
    public void SetShapeTextBodyCommand_Apply_Revert_RestoresOriginalBody()
    {
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var original = MakeTwoRunBody();
        var shape = new SlideShape
        {
            Id          = 1,
            ExtentCxEmu = 914400L,
            ExtentCyEmu = 914400L,
            TextBody    = original,
        };
        slide.Shapes.Add(shape);

        // New body: single run plain text.
        var newBody = new TextBody { Wrap = true };
        var np = new ModelParagraph();
        np.Runs.Add(new ModelRun { Text = "Replaced" });
        newBody.Paragraphs.Add(np);

        var bus    = new PresentationCommandBus(p);
        var editor = new EditingSession(p, bus);

        bus.Execute(new SetShapeTextBodyCommand(0, 1, newBody));

        shape.TextBody!.Paragraphs.Should().HaveCount(1);
        shape.TextBody.Paragraphs[0].Runs[0].Text.Should().Be("Replaced");
        editor.CanUndo.Should().BeTrue();

        editor.Undo();

        shape.TextBody!.Paragraphs.Should().HaveCount(1, "original had 1 paragraph");
        shape.TextBody.Paragraphs[0].Runs.Should().HaveCount(2, "original had 2 runs");
        shape.TextBody.Paragraphs[0].Runs[0].Bold.Should().BeTrue("run 0 was bold");
    }

    // ─── InCanvasTextEditor activates over multi-run shape without throwing ──

    [StaFact]
    public void InCanvasTextEditor_ActivateOverMultiRunShape_DoesNotThrow()
    {
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var shape = new SlideShape
        {
            Id          = 1,
            OffsetXEmu  = 0,
            OffsetYEmu  = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            TextBody    = MakeTwoRunBody(),
        };
        slide.Shapes.Add(shape);

        var bus     = new PresentationCommandBus(p);
        var editor  = new EditingSession(p, bus);
        var canvas  = new SlideCanvas();
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);

        // Set a valid transform on the canvas (simulates a rendered state).
        // We set Presentation so CurrentTransform is computed during render;
        // but since we haven't rendered, CurrentTransform is Identity which is fine.
        canvas.Presentation = p;
        canvas.Slide = slide;

        var act = () => canvas.TextEditor!.Activate(shape.Id);
        act.Should().NotThrow("Activate over a multi-run shape should succeed");

        canvas.TextEditor!.IsActive.Should().BeTrue();
        canvas.TextEditor.ActiveShapeId.Should().Be(shape.Id);

        var box = overlay.Children
            .OfType<System.Windows.Controls.RichTextBox>()
            .Single();
        box.Width.Should().BeApproximately(288, 0.1);
        box.Height.Should().BeApproximately(144, 0.1);
    }

    [StaFact]
    public void InCanvasTextEditor_Commit_IssuesSetShapeTextBodyCommand()
    {
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var body = MakeTwoRunBody();
        var shape = new SlideShape
        {
            Id          = 1,
            OffsetXEmu  = 0,
            OffsetYEmu  = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            TextBody    = body,
        };
        slide.Shapes.Add(shape);

        var bus     = new PresentationCommandBus(p);
        var editor  = new EditingSession(p, bus);
        var canvas  = new SlideCanvas();
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);
        canvas.Presentation = p;
        canvas.Slide = slide;

        canvas.TextEditor!.Activate(shape.Id);
        canvas.TextEditor.IsActive.Should().BeTrue();

        // Commit with an unchanged document should succeed without throwing.
        var act = () => canvas.TextEditor.Commit();
        act.Should().NotThrow();

        canvas.TextEditor.IsActive.Should().BeFalse("editor closes after commit");

        // An unchanged document produces no undo entry (CanUndo is still false at session start).
        editor.CanUndo.Should().BeFalse(
            "no command should be issued when the text body is unchanged");
    }

    // ─── Y1: inherited FontFamily/FontSizePt null must NOT be baked ─────────────

    [StaFact]
    public void InCanvasTextEditor_ApplyBoldToSelection_PreservesMixedRunsOnCommit()
    {
        var p = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var body = MakeTwoRunBody();
        body.Paragraphs[0].Runs[0].Bold = false;
        body.Paragraphs[0].Runs[0].BoldSet = false;
        body.Paragraphs[0].Runs[1].Italic = true;
        body.Paragraphs[0].Runs[1].ItalicSet = true;

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            TextBody = body,
        };
        slide.Shapes.Add(shape);

        var bus = new PresentationCommandBus(p);
        var editor = new EditingSession(p, bus);
        var canvas = new SlideCanvas();
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);
        canvas.Presentation = p;
        canvas.Slide = slide;

        canvas.TextEditor!.Activate(shape.Id);
        canvas.TextEditor.ApplyBold();
        canvas.TextEditor.Commit();

        editor.CanUndo.Should().BeTrue("formatting the active rich text selection should issue a command");
        var runs = shape.TextBody!.Paragraphs[0].Runs;
        runs.Should().HaveCount(2);
        runs[0].Text.Should().Be("Hello");
        runs[1].Text.Should().Be(" world");
        runs.Should().OnlyContain(r => r.Bold);
        runs[1].Italic.Should().BeTrue("existing mixed-run italic formatting should survive");
    }

    [StaFact]
    public void InCanvasTextEditor_TextAndFormattingStayLocalAndCommitAsOneUndoStep()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            TextBody = MakeTwoRunBody(),
        };
        slide.Shapes.Add(shape);

        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var canvas = new SlideCanvas();
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);
        canvas.Presentation = presentation;
        canvas.Slide = slide;

        canvas.TextEditor!.Activate(shape.Id);
        var richTextBox = overlay.Children.OfType<System.Windows.Controls.RichTextBox>().Single();
        richTextBox.SelectAll();
        richTextBox.Selection.Text = "One committed edit";
        richTextBox.SelectAll();
        canvas.TextEditor.ApplyBold();

        editor.CanUndo.Should().BeFalse("the active WPF rich edit is still local");
        InCanvasTextEditPlanner.ExtractPlainText(shape.TextBody).Should().Be("Hello world");

        canvas.TextEditor.Commit();

        editor.CanUndo.Should().BeTrue();
        InCanvasTextEditPlanner.ExtractPlainText(shape.TextBody).Should().Be("One committed edit");
        shape.TextBody!.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().OnlyContain(run => run.Bold);

        editor.Undo();
        editor.CanUndo.Should().BeFalse("text and formatting must share one model command");
        InCanvasTextEditPlanner.ExtractPlainText(shape.TextBody).Should().Be("Hello world");
        shape.TextBody!.Paragraphs[0].Runs.Should().HaveCount(2);
    }

    [StaFact]
    public void Converter_InheritedFontFamilyAndSize_RoundTrip_StillNull()
    {
        // Arrange: run with FontFamily=null and FontSizePt=null (inherit from placeholder).
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun
        {
            Text       = "Inherited",
            FontFamily = null,    // must stay null after round-trip
            FontSizePt = null,    // must stay null after round-trip
        });
        body.Paragraphs.Add(para);

        // Act: round-trip via ToFlowDocument → FromFlowDocument.
        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 14);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        // Assert: inherited values must NOT have been baked in.
        var r = restored.Paragraphs[0].Runs[0];
        r.Text.Should().Be("Inherited");
        r.FontFamily.Should().BeNull("FontFamily=null must survive round-trip (not baked to 'Calibri')");
        r.FontSizePt.Should().BeNull("FontSizePt=null must survive round-trip (not baked to 14pt)");
    }

    // ─── Y2: inherited/scheme Color null must NOT be baked; SchemeColor ref must survive ──

    [StaFact]
    public void Converter_InheritedColor_RoundTrip_StillNull()
    {
        // Arrange: run with Color=null (inherit).
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph();
        para.Runs.Add(new ModelRun { Text = "NoColor", Color = null });
        body.Paragraphs.Add(para);

        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 14);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        var r = restored.Paragraphs[0].Runs[0];
        r.Color.Should().BeNull("Color=null (inherit) must survive round-trip, not be baked to sRGB");
    }

    [StaFact]
    public void Converter_SchemeColor_RoundTrip_PreservesRef()
    {
        // Arrange: run with a SchemeColor (accent1) — the "theme slot" case.
        var schemeRef = new SchemeColorRef { Slot = ThemeColorSlot.Accent1, LumMod = 0.8, LumOff = 0.0 };
        var themeColor = new ThemeAwareColor(new SrgbColor(0x44, 0x72, 0xC4), schemeRef);

        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph();
        para.Runs.Add(new ModelRun { Text = "Themed", Color = themeColor });
        body.Paragraphs.Add(para);

        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 14);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        var r = restored.Paragraphs[0].Runs[0];
        r.Color.Should().NotBeNull();
        r.Color!.SchemeColor.Should().NotBeNull(
            "SchemeColor ref must survive the round-trip (not be replaced by a plain sRGB)");
        r.Color.SchemeColor!.Slot.Should().Be(ThemeColorSlot.Accent1);
        r.Color.SchemeColor.LumMod.Should().BeApproximately(0.8, 1e-9);
    }

    // ─── Y1+Y2: no-op edit (convert and convert back) leaves TextBodiesEqual true ──

    [StaFact]
    public void Converter_NoOpEdit_InheritedRun_BodyUnchanged()
    {
        // Arrange: a body with a run that has all-null formatting (fully inherited).
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun
        {
            Text       = "Placeholder text",
            FontFamily = null,
            FontSizePt = null,
            Color      = null,
        });
        body.Paragraphs.Add(para);

        // Act: simulate a no-op edit (convert to doc, convert back with original body as reference).
        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 18);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        // Assert: both runs have the same (null) inherited fields — bodies are "equal".
        // We verify the fields directly (TextBodiesEqual is private).
        var orig = body.Paragraphs[0].Runs[0];
        var rest = restored.Paragraphs[0].Runs[0];
        rest.Text.Should().Be(orig.Text);
        rest.FontFamily.Should().BeNull("FontFamily must stay null after no-op edit");
        rest.FontSizePt.Should().BeNull("FontSizePt must stay null after no-op edit");
        rest.Color.Should().BeNull("Color must stay null after no-op edit");
        rest.Bold.Should().Be(orig.Bold);
        rest.Italic.Should().Be(orig.Italic);
    }

    // ─── Y3: color-only change IS detected by TextBodiesEqual (via Commit path) ─

    [StaFact]
    public void InCanvasTextEditor_Commit_ColorOnlyChange_IssuesCommand()
    {
        // Arrange: shape with one red run.
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var redColor  = new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00));
        var blueColor = new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF));

        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun
        {
            Text       = "Red",
            FontFamily = "Calibri",
            FontSizePt = 14,
            Color      = redColor,
        });
        body.Paragraphs.Add(para);

        var shape = new SlideShape
        {
            Id          = 1,
            OffsetXEmu  = 0,
            OffsetYEmu  = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            TextBody    = body,
        };
        slide.Shapes.Add(shape);

        var bus     = new PresentationCommandBus(p);
        var editor  = new EditingSession(p, bus);
        var canvas  = new SlideCanvas();
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);
        canvas.Presentation = p;
        canvas.Slide        = slide;

        // Activate the editor so we can call ApplyColor on the selection.
        canvas.TextEditor!.Activate(shape.Id);
        canvas.TextEditor.IsActive.Should().BeTrue();

        // Apply a blue color to the selection (changes the run's color).
        canvas.TextEditor.ApplyColor(blueColor);

        // Commit — this should issue a command because color changed.
        canvas.TextEditor.Commit();

        editor.CanUndo.Should().BeTrue(
            "a color-only change must be detected and issue an undo-able command");
    }

    // ─── Y4: SemiBold weight NOT coerced to Bold ───────────────────────────────

    [StaFact]
    public void Converter_SemiBoldRun_NotCoercedToBold()
    {
        // Build a FlowDocument with a SemiBold run manually (simulating WPF producing it).
        var doc = new FlowDocument();
        var wp  = new System.Windows.Documents.Paragraph();
        var wr  = new System.Windows.Documents.Run("SemiBold text")
        {
            FontWeight = FontWeights.SemiBold
        };
        wp.Inlines.Add(wr);
        doc.Blocks.Add(wp);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc);

        var r = restored.Paragraphs[0].Runs[0];
        r.Bold.Should().BeFalse("SemiBold must NOT be coerced to Bold=true");
    }

    // ─── Y5: soft-break "\n" runs survive round-trip as LineBreak ─────────────

    [StaFact]
    public void Converter_SoftBreakRun_RoundTrips_AsLineBreak()
    {
        // Arrange: a body with a soft line-break run (Text=="\n").
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph();
        para.Runs.Add(new ModelRun { Text = "Line 1" });
        para.Runs.Add(new ModelRun { Text = "\n" });    // soft break
        para.Runs.Add(new ModelRun { Text = "Line 2" });
        body.Paragraphs.Add(para);

        // Act: to FlowDocument and back.
        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 12);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        var runs = restored.Paragraphs[0].Runs;
        runs.Should().HaveCount(3, "three model runs (text + soft-break + text)");
        runs[0].Text.Should().Be("Line 1");
        runs[1].Text.Should().Be("\n", "soft break must survive as '\\n' text in the model");
        runs[2].Text.Should().Be("Line 2");
    }

    // ─── Z2: offset-based original-run matching (no cross-contamination after edits) ─

    /// <summary>
    /// Z2 (a): original [A(scheme accent1), B(scheme accent2)], NO edit.
    /// Both scheme colors must be preserved (existing Y2 behavior verified with offset matching).
    /// </summary>
    [StaFact]
    public void Z2_UnEditedTwoSchemeColorRuns_BothSchemeColorsPreserved()
    {
        var accent1Color = new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });
        var accent2Color = new ThemeAwareColor(
            new SrgbColor(0xED, 0x7D, 0x31),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent2 });

        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun { Text = "AAA", Color = accent1Color, FontFamily = "Calibri", FontSizePt = 12 });
        para.Runs.Add(new ModelRun { Text = "BBB", Color = accent2Color, FontFamily = "Calibri", FontSizePt = 12 });
        body.Paragraphs.Add(para);

        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 12);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        var runs = restored.Paragraphs[0].Runs;
        runs.Should().HaveCount(2, "no edit → same run count");

        runs[0].Color.Should().NotBeNull("A still has a color");
        var colorA = runs[0].Color!;
        colorA.SchemeColor.Should().NotBeNull("A's scheme color must be preserved");
        colorA.SchemeColor!.Slot.Should().Be(ThemeColorSlot.Accent1,
            "Z2 (a): run A must retain accent1, not contaminate with accent2");

        runs[1].Color.Should().NotBeNull("B still has a color");
        var colorB = runs[1].Color!;
        colorB.SchemeColor.Should().NotBeNull("B's scheme color must be preserved");
        colorB.SchemeColor!.Slot.Should().Be(ThemeColorSlot.Accent2,
            "Z2 (a): run B must retain accent2");
    }

    /// <summary>
    /// Z2 (b): a character typed in the MIDDLE of run A splits it into two inlines (A1, A2).
    /// Run B follows.  Offset matching must give A1 and A2 both accent1, and B accent2.
    /// No cross-contamination (old ordinal bug: A2 would get accent2, B would lose its color).
    /// </summary>
    [StaFact]
    public void Z2_TypingMidRunA_HalvesKeepAccent1_BKeepsAccent2()
    {
        var accent1Color = new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });
        var accent2Color = new ThemeAwareColor(
            new SrgbColor(0xED, 0x7D, 0x31),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent2 });

        // Original: A(3 chars, accent1) + B(3 chars, accent2).
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun { Text = "AAA", Color = accent1Color, FontFamily = "Calibri", FontSizePt = 12 });
        para.Runs.Add(new ModelRun { Text = "BBB", Color = accent2Color, FontFamily = "Calibri", FontSizePt = 12 });
        body.Paragraphs.Add(para);

        // Simulate user typed one char in the middle of A: FlowDocument has [A1, A2, B].
        // A1 = "AA" (offset 0..1), A2 = "xA" (offset 2..3 in original = still within A), B = "BBB".
        // We build a FlowDocument manually (no RichTextBox required — the converter is pure).
        var doc = new System.Windows.Documents.FlowDocument();
        var wp  = new System.Windows.Documents.Paragraph();

        // Brush colour matching accent1 (so WpfInlineToModelRun recognises it as unchanged).
        var accent1Brush = new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4));
        var accent2Brush = new SolidColorBrush(Color.FromRgb(0xED, 0x7D, 0x31));

        var wrA1 = new WpfRun("AA") { Foreground = accent1Brush, FontFamily = new FontFamily("Calibri"), FontSize = 12 * (96.0/72.0) };
        var wrA2 = new WpfRun("xA") { Foreground = accent1Brush, FontFamily = new FontFamily("Calibri"), FontSize = 12 * (96.0/72.0) };
        var wrB  = new WpfRun("BBB") { Foreground = accent2Brush, FontFamily = new FontFamily("Calibri"), FontSize = 12 * (96.0/72.0) };
        wp.Inlines.Add(wrA1);
        wp.Inlines.Add(wrA2);
        wp.Inlines.Add(wrB);
        doc.Blocks.Add(wp);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        var runs = restored.Paragraphs[0].Runs;
        runs.Should().HaveCount(3, "A split into two inlines + B");

        // A1 (offset 0) is inside original A [0,3) → accent1.
        runs[0].Color.Should().NotBeNull();
        runs[0].Color!.SchemeColor?.Slot.Should().Be(ThemeColorSlot.Accent1,
            "Z2 (b): A1 starts at offset 0, inside original A → accent1");

        // A2 ("xA") is in the DISTURBED MIDDLE under prefix/suffix matching:
        // prefix=0 (text "AA" ≠ original "AAA"), suffix=1 (BBB=BBB).
        // The AA1 fail-safe rule: middle runs NEVER carry a scheme color ref from an original run.
        // If A2 has a locally-set Foreground brush, WpfInlineToModelRun synthesizes a plain sRGB
        // (no SchemeColor) — that is acceptable (the sRGB is correct, only the scheme ref is lost).
        // The WRONG outcome would be A2 getting accent1's SchemeColor ref from the wrong original.
        var a2SchemeSlot = runs[1].Color?.SchemeColor?.Slot;
        a2SchemeSlot.Should().BeNull(
            "Z2 (b): A2 is in the disturbed middle → must NOT carry any scheme color ref " +
            "(fail-safe: never contaminate with wrong original run's scheme ref)");

        // B (suffix match: text "BBB" == original B "BBB") → accent2.
        runs[2].Color.Should().NotBeNull();
        runs[2].Color!.SchemeColor?.Slot.Should().Be(ThemeColorSlot.Accent2,
            "Z2 (b): B is in the unchanged suffix → accent2 preserved");
    }

    /// <summary>
    /// Z2 (c): a soft break (LineBreak = "\n") inserted between A and B.
    /// LineBreak counts as 1 char in offset accounting.
    /// B's scheme color must not shift due to the break consuming one offset slot.
    /// </summary>
    [StaFact]
    public void Z2_SoftBreakBetweenRuns_DoesNotShiftBColor()
    {
        var accent1Color = new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });
        var accent2Color = new ThemeAwareColor(
            new SrgbColor(0xED, 0x7D, 0x31),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent2 });

        // Original: A(3 chars, accent1) + softbreak(1 char, no color) + B(3 chars, accent2).
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun { Text = "AAA", Color = accent1Color, FontFamily = "Calibri", FontSizePt = 12 });
        para.Runs.Add(new ModelRun { Text = "\n" }); // soft break, no color
        para.Runs.Add(new ModelRun { Text = "BBB", Color = accent2Color, FontFamily = "Calibri", FontSizePt = 12 });
        body.Paragraphs.Add(para);

        // Simulate round-trip through FlowDocument (no edit).
        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 12);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        var runs = restored.Paragraphs[0].Runs;
        runs.Should().HaveCount(3, "A + softbreak + B");

        runs[0].Text.Should().Be("AAA");
        runs[0].Color?.SchemeColor?.Slot.Should().Be(ThemeColorSlot.Accent1,
            "Z2 (c): A's color must be accent1 even when a soft-break follows");

        runs[1].Text.Should().Be("\n", "soft break preserved");
        runs[1].Color.Should().BeNull("soft break run has no color (null)");

        runs[2].Text.Should().Be("BBB");
        runs[2].Color?.SchemeColor?.Slot.Should().Be(ThemeColorSlot.Accent2,
            "Z2 (c): B's color must be accent2 — soft-break offset shift must not misalign it");
    }

    /// <summary>
    /// Z2 (d): brand-new text appended beyond the original text length.
    /// The new run has no corresponding original run → must inherit (Color=null), not carry a wrong color.
    /// </summary>
    [StaFact]
    public void Z2_NewTextBeyondOriginalLength_InheritsNull_NotWrongColor()
    {
        var accent1Color = new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });

        // Original: single run A(3 chars, accent1).
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun { Text = "AAA", Color = accent1Color, FontFamily = "Calibri", FontSizePt = 12 });
        body.Paragraphs.Add(para);

        // Simulate user appended "NEW" after A: FlowDocument has [A, NEW].
        // NEW has no foreground set (inherited) — it's new text beyond original length.
        var doc = new System.Windows.Documents.FlowDocument();
        var wp  = new System.Windows.Documents.Paragraph();
        var accent1Brush = new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4));
        var wrA   = new WpfRun("AAA") { Foreground = accent1Brush, FontFamily = new FontFamily("Calibri"), FontSize = 12 * (96.0/72.0) };
        var wrNew = new WpfRun("NEW"); // no foreground set — inherits
        wp.Inlines.Add(wrA);
        wp.Inlines.Add(wrNew);
        doc.Blocks.Add(wp);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        var runs = restored.Paragraphs[0].Runs;
        runs.Should().HaveCount(2);

        // A: offset 0, inside original A [0,3) → should preserve accent1.
        runs[0].Color?.SchemeColor?.Slot.Should().Be(ThemeColorSlot.Accent1,
            "Z2 (d): original A run still gets accent1");

        // NEW: offset 3, beyond original length (3..3) → no matching original run → null (inherit).
        runs[1].Color.Should().BeNull(
            "Z2 (d): new text beyond original length must inherit (null), not carry accent1 or any color");
    }

    // ─── AA1: prefix/suffix fail-safe (delete-shift, insert-shift, suffix-preserve, append) ─

    /// <summary>
    /// AA1 (a) — DELETE-SHIFT: the core AA1 bug case.
    /// Original: A[0,5)(accent1, locally-set Foreground), B[5,10)(Color=null, inherits).
    /// User deletes 3 chars from A: edited inlines become A'("AB", 2 chars) + B("FGHIJ", 5 chars).
    /// Under prefix/suffix: A'.Text="AB" ≠ orig A.Text="ABCDE" → prefix=0.
    ///                       B.Text="FGHIJ" == orig B.Text="FGHIJ" → suffix=1.
    /// B is in the suffix, matched to original B whose Color=null → reconstructed B.Color must be NULL.
    /// </summary>
    [StaFact]
    public void AA1_DeleteShift_RunB_ColorIsNull_NotAccent1()
    {
        var accent1Color = new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });

        // Original: A(5 chars, accent1, local Foreground) + B(5 chars, Color=null inherit).
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun { Text = "ABCDE", Color = accent1Color, FontFamily = "Calibri", FontSizePt = 12 });
        para.Runs.Add(new ModelRun { Text = "FGHIJ", Color = null });   // Color=null: INHERIT
        body.Paragraphs.Add(para);

        // Build post-edit FlowDocument: A'("AB", locally-set accent1 brush) + B("FGHIJ", no foreground).
        // This mirrors what the RichTextBox produces after the user deletes 3 chars from A.
        var doc = new FlowDocument();
        var wp  = new WpfParagraph();
        var accent1Brush = new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4));

        var wrA = new WpfRun("AB") { Foreground = accent1Brush };      // locally set
        var wrB = new WpfRun("FGHIJ");                                   // no local foreground (inherit)
        wp.Inlines.Add(wrA);
        wp.Inlines.Add(wrB);
        doc.Blocks.Add(wp);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);
        var runs = restored.Paragraphs[0].Runs;

        runs.Should().HaveCount(2);

        // A' is in the DISTURBED MIDDLE (text "AB" ≠ orig "ABCDE" → prefix=0).
        // Its local Foreground matches accent1 but the prefix check failed → it still gets
        // the locally-set sRGB (synthesized, not the scheme ref). That is acceptable degradation.
        // (We don't assert its exact Color here — the key assertion is about B.)

        // B MUST be null (inherit), NOT accent1.
        // Suffix match: B.Text "FGHIJ" == orig B.Text "FGHIJ" → matches orig B (Color=null).
        runs[1].Color.Should().BeNull(
            "AA1 (a): B's Color=null (inherit) must survive — must NOT be contaminated with A's accent1 " +
            "even though 3 chars were deleted from A shifting B's character offset");
    }

    /// <summary>
    /// AA1 (b) — INSERT-SHIFT: inserting chars before B shifts B's offset but its text stays the same.
    /// B should still inherit (null), not get A's color.
    /// </summary>
    [StaFact]
    public void AA1_InsertShift_RunB_ColorIsNull_NotAccent1()
    {
        var accent1Color = new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });

        // Original: A(3 chars, accent1) + B(3 chars, Color=null inherit).
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun { Text = "AAA", Color = accent1Color });
        para.Runs.Add(new ModelRun { Text = "BBB", Color = null });
        body.Paragraphs.Add(para);

        // Post-edit: user inserted "XX" inside A → A is now "XAAXAA" or "AAX" or similar.
        // The important thing: A has different text, B has the SAME text "BBB".
        var doc = new FlowDocument();
        var wp  = new WpfParagraph();
        var accent1Brush = new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4));

        var wrA = new WpfRun("AAAXX") { Foreground = accent1Brush };   // A now longer, locally set
        var wrB = new WpfRun("BBB");                                     // B unchanged, no local foreground
        wp.Inlines.Add(wrA);
        wp.Inlines.Add(wrB);
        doc.Blocks.Add(wp);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);
        var runs = restored.Paragraphs[0].Runs;
        runs.Should().HaveCount(2);

        // A: text "AAAXX" ≠ orig "AAA" → prefix=0, A is in middle → null or synthesized sRGB.
        // B: suffix match "BBB"=="BBB" → matched to orig B (Color=null).
        runs[1].Color.Should().BeNull(
            "AA1 (b): B Color=null must survive an insert in A — B is in the suffix, matched to orig B");
    }

    /// <summary>
    /// AA1 (c) — TRAILING-UNCHANGED: edit run A, leave B and C untouched at the end.
    /// B and C must retain their scheme colors (suffix match).
    /// </summary>
    [StaFact]
    public void AA1_TrailingUnchangedRuns_KeepSchemeColors()
    {
        var accent1Color = new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });
        var accent2Color = new ThemeAwareColor(
            new SrgbColor(0xED, 0x7D, 0x31),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent2 });

        // Original: A(accent1, "AAA") + B(accent2, "BBB") + C(null, "CCC").
        // C has no color (inherit).
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun { Text = "AAA", Color = accent1Color });
        para.Runs.Add(new ModelRun { Text = "BBB", Color = accent2Color });
        para.Runs.Add(new ModelRun { Text = "CCC", Color = null });
        body.Paragraphs.Add(para);

        // Simulate user edited A (now "ZZZ"), B and C unchanged.
        var doc = new FlowDocument();
        var wp  = new WpfParagraph();
        var accent1Brush = new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4));
        var accent2Brush = new SolidColorBrush(Color.FromRgb(0xED, 0x7D, 0x31));

        var wrA = new WpfRun("ZZZ") { Foreground = accent1Brush };
        var wrB = new WpfRun("BBB") { Foreground = accent2Brush };
        var wrC = new WpfRun("CCC");                                   // no local foreground
        wp.Inlines.Add(wrA);
        wp.Inlines.Add(wrB);
        wp.Inlines.Add(wrC);
        doc.Blocks.Add(wp);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);
        var runs = restored.Paragraphs[0].Runs;
        runs.Should().HaveCount(3);

        // A: "ZZZ" ≠ orig "AAA" → prefix=0, A in middle → no scheme ref (acceptable).
        // Suffix: C "CCC"=="CCC" (suffixLen≥1), then B "BBB"=="BBB" (suffixLen≥2) → suffixLen=2.
        // B matched to orig B (accent2), C matched to orig C (null).

        runs[1].Color.Should().NotBeNull("B is in the suffix, matched to orig B (accent2)");
        runs[1].Color!.SchemeColor.Should().NotBeNull("B's scheme ref must be preserved");
        runs[1].Color!.SchemeColor!.Slot.Should().Be(ThemeColorSlot.Accent2,
            "AA1 (c): B is trailing-unchanged → must keep accent2");

        runs[2].Color.Should().BeNull(
            "AA1 (c): C is trailing-unchanged with Color=null → must stay null");
    }

    /// <summary>
    /// AA1 (d) — APPEND-NEW-TEXT: appending a new run at the end must NOT carry the last original run's color.
    /// </summary>
    [StaFact]
    public void AA1_AppendNewRun_NewRunInheritsNull()
    {
        var accent1Color = new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });

        // Original: A(accent1, "Hello").
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun { Text = "Hello", Color = accent1Color });
        body.Paragraphs.Add(para);

        // Post-edit: A unchanged, NEW appended.
        // A Foreground locally set (accent1 brush), NEW has no foreground.
        var doc = new FlowDocument();
        var wp  = new WpfParagraph();
        var accent1Brush = new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4));

        var wrA   = new WpfRun("Hello") { Foreground = accent1Brush };
        var wrNew = new WpfRun(" world!");   // new text, no foreground set
        wp.Inlines.Add(wrA);
        wp.Inlines.Add(wrNew);
        doc.Blocks.Add(wp);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);
        var runs = restored.Paragraphs[0].Runs;
        runs.Should().HaveCount(2);

        // Prefix: "Hello"=="Hello" → prefixLen=1. A matched to orig A (accent1).
        runs[0].Color.Should().NotBeNull("A is unchanged (prefix match) → accent1 preserved");
        runs[0].Color!.SchemeColor?.Slot.Should().Be(ThemeColorSlot.Accent1,
            "AA1 (d): A's scheme ref must be preserved when it's in the unchanged prefix");

        // NEW: prefixLen=1, suffixLen=0 (only 1 orig run, already consumed by prefix).
        // NEW is in the middle → null.
        runs[1].Color.Should().BeNull(
            "AA1 (d): newly appended run has no original counterpart → must inherit (null), not carry accent1");
    }

    /// <summary>
    /// AA1 (e) — NO-EDIT full prefix: original [A(accent1), B(accent2)], no edit at all.
    /// Both scheme refs must be preserved via prefix match (all runs match → full prefix, suffix=0).
    /// </summary>
    [StaFact]
    public void AA1_NoEdit_TwoSchemeRuns_BothPreservedViaPrefix()
    {
        var accent1Color = new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });
        var accent2Color = new ThemeAwareColor(
            new SrgbColor(0xED, 0x7D, 0x31),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent2 });

        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun { Text = "AAA", Color = accent1Color, FontFamily = "Calibri", FontSizePt = 12 });
        para.Runs.Add(new ModelRun { Text = "BBB", Color = accent2Color, FontFamily = "Calibri", FontSizePt = 12 });
        body.Paragraphs.Add(para);

        // No edit: round-trip via ToFlowDocument / FromFlowDocument.
        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 12);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        var runs = restored.Paragraphs[0].Runs;
        runs.Should().HaveCount(2, "no edit → same run count");

        runs[0].Color!.SchemeColor!.Slot.Should().Be(ThemeColorSlot.Accent1,
            "AA1 (e): A in unchanged prefix → accent1 preserved");
        runs[1].Color!.SchemeColor!.Slot.Should().Be(ThemeColorSlot.Accent2,
            "AA1 (e): B in unchanged prefix → accent2 preserved");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a TextBody with a single paragraph and two runs:
    /// - Run 0: "Hello", Bold=true, Color=Red(#FF0000), FontSize=24pt
    /// - Run 1: " world", Bold=false, no color, FontSize=12pt
    /// </summary>
    private static TextBody MakeTwoRunBody()
    {
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun
        {
            Text       = "Hello",
            Bold       = true,
            FontFamily = "Calibri",
            FontSizePt = 24,
            Color      = new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)),
        });
        para.Runs.Add(new ModelRun
        {
            Text       = " world",
            Bold       = false,
            FontFamily = "Calibri",
            FontSizePt = 12,
        });
        body.Paragraphs.Add(para);
        return body;
    }
}
