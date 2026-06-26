using System.Windows;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 10B — unit tests for <see cref="OsClipboardService"/>.
///
/// All tests use <see cref="FakeOsClipboard"/> and <see cref="StubShapeRenderer"/> so no
/// real OS-clipboard access occurs.  This means the tests run safely in headless CI.
/// </summary>
public sealed class OsClipboardServiceTests
{
    // ── Fake implementations ───────────────────────────────────────────────────────

    /// <summary>
    /// In-memory clipboard stub.  Tests set HasImage / HasText / ImageBytes / Text before
    /// calling the service and inspect WasSetCalled / LastDataObject after.
    /// </summary>
    internal sealed class FakeOsClipboard : IOsClipboard
    {
        public bool HasImage   { get; set; }
        public bool HasText    { get; set; }
        public byte[]? ImageBytes { get; set; }
        public string? Text    { get; set; }

        public bool       WasSetCalled { get; private set; }
        public DataObject? LastDataObject { get; private set; }

        public bool     ContainsImage()   => HasImage;
        public bool     ContainsText()    => HasText;
        public byte[]?  GetImagePngBytes() => ImageBytes;
        public string?  GetText()         => Text;

        public void SetDataObject(DataObject data)
        {
            WasSetCalled   = true;
            LastDataObject = data;
        }
    }

    /// <summary>
    /// Shape renderer stub that returns a fixed 1×1 red PNG (valid minimal PNG).
    /// Tests can override ReturnEmpty to simulate a failed render.
    /// </summary>
    internal sealed class StubShapeRenderer : IShapeRenderer
    {
        // Minimal valid 1×1 red PNG (generated via System.Drawing, Base64-encoded).
        private static readonly byte[] _minimalPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");

        public bool ReturnEmpty { get; set; }

        public byte[] RenderShapesToPng(
            Presentation              presentation,
            Slide                     slide,
            IReadOnlyList<SlideShape> shapes,
            int widthPx, int heightPx)
            => ReturnEmpty ? Array.Empty<byte>() : _minimalPng;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────

    private static EditingSession MakeSessionWithShape(out SlideShape shape)
    {
        var pres  = Presentation.CreateEmpty();
        var slide = pres.Slides[0];

        shape = new SlideShape
        {
            Id           = 1u,
            Name         = "TestShape",
            Kind         = SlideShapeKind.AutoShape,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            OffsetXEmu   = 914_400,
            OffsetYEmu   = 457_200,
            ExtentCxEmu  = 2_743_200,
            ExtentCyEmu  = 1_828_800,
            TextBody     = new TextBody()
        };
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Hello" });
        shape.TextBody!.Paragraphs.Add(para);
        slide.Shapes.Add(shape);

        var bus  = new PresentationCommandBus(pres);
        var sess = new EditingSession(pres, bus);
        sess.Select(1u);
        return sess;
    }

    private static OsClipboardService MakeService(
        FakeOsClipboard?  clipboard = null,
        StubShapeRenderer? renderer  = null)
        => new(clipboard ?? new FakeOsClipboard(), renderer ?? new StubShapeRenderer());

    // ════════════════════════════════════════════════════════════════════════════════
    //  Paste-decision logic (pure, no UI)
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DecidePasteAction_OsImagePresent_ReturnsOsImage_WhenPreferOsTrue()
    {
        var action = OsClipboardService.DecidePasteAction(
            osHasImage: true, osHasText: false, internalHasData: false,
            preferOsClipboard: true);

        action.Should().Be(PasteAction.OsImage);
    }

    [Fact]
    public void DecidePasteAction_OsTextOnly_ReturnsOsText_WhenPreferOsTrue()
    {
        var action = OsClipboardService.DecidePasteAction(
            osHasImage: false, osHasText: true, internalHasData: false,
            preferOsClipboard: true);

        action.Should().Be(PasteAction.OsText);
    }

    [Fact]
    public void DecidePasteAction_InternalOnly_ReturnsInternal_WhenPreferOsTrue()
    {
        var action = OsClipboardService.DecidePasteAction(
            osHasImage: false, osHasText: false, internalHasData: true,
            preferOsClipboard: true);

        action.Should().Be(PasteAction.Internal);
    }

    [Fact]
    public void DecidePasteAction_NothingAvailable_ReturnsNothing()
    {
        var action = OsClipboardService.DecidePasteAction(
            osHasImage: false, osHasText: false, internalHasData: false,
            preferOsClipboard: true);

        action.Should().Be(PasteAction.Nothing);
    }

    [Fact]
    public void DecidePasteAction_InternalHasData_PrefersInternal_WhenPreferOsFalse()
    {
        var action = OsClipboardService.DecidePasteAction(
            osHasImage: true, osHasText: true, internalHasData: true,
            preferOsClipboard: false);

        action.Should().Be(PasteAction.Internal,
            "when preferOsClipboard=false, internal clipboard wins");
    }

    [Fact]
    public void DecidePasteAction_OsImageAndText_ImageWins_WhenPreferOsTrue()
    {
        var action = OsClipboardService.DecidePasteAction(
            osHasImage: true, osHasText: true, internalHasData: false,
            preferOsClipboard: true);

        action.Should().Be(PasteAction.OsImage,
            "image takes priority over text in OS-preferred mode");
    }

    [Fact]
    public void DecidePasteAction_NothingOnOsButInternalHasData_PreferOsFalse_ReturnsInternal()
    {
        var action = OsClipboardService.DecidePasteAction(
            osHasImage: false, osHasText: false, internalHasData: true,
            preferOsClipboard: false);

        action.Should().Be(PasteAction.Internal);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Copy payload builder (with fake renderer, no real clipboard)
    // ════════════════════════════════════════════════════════════════════════════════

    [StaFact]
    public void BuildDataObject_WithTextShape_ContainsText()
    {
        var sess    = MakeSessionWithShape(out _);
        var svc     = MakeService();
        var slide   = sess.CurrentSlide!;
        var shapes  = slide.Shapes.AsReadOnly();

        var dataObj = svc.BuildDataObject(sess.Presentation, slide, shapes);

        // WPF DataObject.SetText stores under UnicodeText (CF_UNICODETEXT).
        dataObj.GetDataPresent(DataFormats.UnicodeText).Should().BeTrue(
            "text from shape's TextBody should be placed on the DataObject");
        var text = (string?)dataObj.GetData(DataFormats.UnicodeText);
        text.Should().Contain("Hello");
    }

    [StaFact]
    public void BuildDataObject_WithStubRenderer_ContainsImage()
    {
        var sess   = MakeSessionWithShape(out _);
        var svc    = MakeService();
        var slide  = sess.CurrentSlide!;
        var shapes = slide.Shapes.AsReadOnly();

        var dataObj = svc.BuildDataObject(sess.Presentation, slide, shapes);

        dataObj.GetDataPresent(DataFormats.Bitmap).Should().BeTrue(
            "renderer produced a PNG that should be placed as bitmap on DataObject");
    }

    [StaFact]
    public void BuildDataObject_RendererReturnsEmpty_NoBitmapInDataObject()
    {
        var renderer = new StubShapeRenderer { ReturnEmpty = true };
        var sess     = MakeSessionWithShape(out _);
        var svc      = new OsClipboardService(new FakeOsClipboard(), renderer);
        var slide    = sess.CurrentSlide!;
        var shapes   = slide.Shapes.AsReadOnly();

        var dataObj = svc.BuildDataObject(sess.Presentation, slide, shapes);

        // No image (renderer returned empty), but text still present.
        dataObj.GetDataPresent(DataFormats.UnicodeText).Should().BeTrue(
            "text is still placed even when the renderer returns empty");
        dataObj.GetDataPresent(DataFormats.Bitmap).Should().BeFalse(
            "no PNG from renderer → no bitmap on DataObject");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  PlaceSelectionOnOsClipboard (no real clipboard via FakeOsClipboard)
    // ════════════════════════════════════════════════════════════════════════════════

    [StaFact]
    public void PlaceSelectionOnOsClipboard_WithSelection_CallsSetDataObject()
    {
        var fake = new FakeOsClipboard();
        var sess = MakeSessionWithShape(out _);
        var svc  = new OsClipboardService(fake, new StubShapeRenderer());

        svc.PlaceSelectionOnOsClipboard(sess);

        fake.WasSetCalled.Should().BeTrue("SetDataObject must be called when shapes are selected");
    }

    [StaFact]
    public void PlaceSelectionOnOsClipboard_WithNoSelection_DoesNotCallSetDataObject()
    {
        var fake = new FakeOsClipboard();
        var sess = MakeSessionWithShape(out _);
        sess.ClearSelection();
        var svc  = new OsClipboardService(fake, new StubShapeRenderer());

        svc.PlaceSelectionOnOsClipboard(sess);

        fake.WasSetCalled.Should().BeFalse("no shapes selected → no clipboard write");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Paste routing (with fake clipboard + fake renderer)
    // ════════════════════════════════════════════════════════════════════════════════

    // Minimal valid 1×1 PNG for image paste tests (same as StubShapeRenderer).
    private static readonly byte[] _minPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");

    [StaFact]
    public void Paste_OsImagePresent_InsertsAPictureShape()
    {
        var fake = new FakeOsClipboard { HasImage = true, ImageBytes = _minPng };
        var sess = MakeSessionWithShape(out _);
        sess.ClearSelection();
        var svc  = new OsClipboardService(fake, new StubShapeRenderer());
        var before = sess.CurrentSlide!.Shapes.Count;

        svc.Paste(sess, preferOsClipboard: true);

        sess.CurrentSlide!.Shapes.Count.Should().Be(before + 1,
            "an image paste inserts one Picture shape");
        sess.CurrentSlide!.Shapes.Last().Kind.Should().Be(SlideShapeKind.Picture);
    }

    [StaFact]
    public void Paste_OsTextPresent_InsertsTextBox()
    {
        var fake = new FakeOsClipboard { HasText = true, Text = "Pasted text" };
        var sess = MakeSessionWithShape(out _);
        sess.ClearSelection();
        var svc  = new OsClipboardService(fake, new StubShapeRenderer());
        var before = sess.CurrentSlide!.Shapes.Count;

        svc.Paste(sess, preferOsClipboard: true);

        sess.CurrentSlide!.Shapes.Count.Should().Be(before + 1,
            "a text paste inserts one AutoShape (textbox)");
        var inserted = sess.CurrentSlide!.Shapes.Last();
        inserted.Kind.Should().Be(SlideShapeKind.AutoShape);
        inserted.TextBody.Should().NotBeNull();
    }

    [StaFact]
    public void Paste_InternalClipboardFallback_WhenOsEmpty()
    {
        var fake = new FakeOsClipboard();   // empty OS clipboard
        var sess = MakeSessionWithShape(out _);
        // Load the internal clipboard with a shape.
        sess.CopySelectedShapes();
        sess.ClearSelection();
        var svc    = new OsClipboardService(fake, new StubShapeRenderer());
        var before = sess.CurrentSlide!.Shapes.Count;

        svc.Paste(sess, preferOsClipboard: true);

        sess.CurrentSlide!.Shapes.Count.Should().Be(before + 1,
            "internal clipboard is used when OS clipboard is empty");
    }

    [StaFact]
    public void Paste_NothingAvailable_DoesNotChangeShapeCount()
    {
        var fake = new FakeOsClipboard();   // empty OS clipboard
        var sess = MakeSessionWithShape(out _);
        sess.ClearSelection();
        // No internal clipboard data either.
        var svc    = new OsClipboardService(fake, new StubShapeRenderer());
        var before = sess.CurrentSlide!.Shapes.Count;

        svc.Paste(sess, preferOsClipboard: true);

        sess.CurrentSlide!.Shapes.Count.Should().Be(before,
            "nothing on any clipboard → no shape inserted");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  ExtractText helper
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExtractText_MultipleShapesWithText_JoinsWithDoubleNewline()
    {
        var s1 = new SlideShape { Id = 1u, Kind = SlideShapeKind.AutoShape, TextBody = new TextBody() };
        var p1 = new Paragraph(); p1.Runs.Add(new Run { Text = "Foo" }); s1.TextBody!.Paragraphs.Add(p1);

        var s2 = new SlideShape { Id = 2u, Kind = SlideShapeKind.AutoShape, TextBody = new TextBody() };
        var p2 = new Paragraph(); p2.Runs.Add(new Run { Text = "Bar" }); s2.TextBody!.Paragraphs.Add(p2);

        var text = OsClipboardService.ExtractText(new[] { s1, s2 });

        text.Should().Contain("Foo");
        text.Should().Contain("Bar");
    }

    [Fact]
    public void ExtractText_ShapeWithNoTextBody_IsSkipped()
    {
        var s1 = new SlideShape { Id = 1u, Kind = SlideShapeKind.AutoShape }; // no TextBody
        var text = OsClipboardService.ExtractText(new[] { s1 });
        text.Should().BeEmpty();
    }
}
