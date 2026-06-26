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
