using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using Free.Shared.Drawing;
using Free.Shared.AppServices;
using Free.Shared.Shell.Wpf;
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
    [StaFact]
    public async Task SharedWpfClipboard_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var clipboard = new WpfPlatformClipboard();

        var action = async () => await clipboard.WriteAsync(
            new PlatformClipboardContent(Text: "cancelled"),
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Fake implementations ───────────────────────────────────────────────────────

    /// <summary>
    /// In-memory clipboard stub. Tests configure the shared content contract and inspect
    /// the last write without touching the WPF clipboard.
    ///
    /// Z1: <see cref="SequenceNumber"/> starts at 1 and is auto-incremented by
    /// <see cref="Write"/> (mirroring Windows, which bumps the sequence on every write).
    /// Tests can also set <see cref="SequenceNumber"/> directly to simulate an external app
    /// overwriting the clipboard.
    /// </summary>
    internal sealed class FakeOsClipboard : IPlatformClipboard
    {
        public bool HasImage   { get; set; }
        public bool HasText    { get; set; }
        public byte[]? ImageBytes { get; set; }
        public string? Text    { get; set; }
        public byte[]? SelectionBytes { get; set; }
        public byte[]? RichTextBytes { get; set; }
        public byte[]? XamlPackageBytes { get; set; }
        public byte[]? RtfBytes { get; set; }
        public string? OwnerToken { get; set; }
        public bool ThrowOnRead { get; set; }
        public bool ThrowOnWrite { get; set; }

        public bool       WasSetCalled { get; private set; }
        public PresentationClipboardContent? LastContent { get; private set; }

        /// <summary>
        /// Simulates the Windows clipboard sequence number.
        /// Auto-incremented by <see cref="Write"/>; may also be set directly
        /// by tests to simulate an external clipboard write (another app overwrote it).
        /// </summary>
        public long SequenceNumber { get; set; } = 1;

        public PresentationClipboardContent Read()
        {
            if (ThrowOnRead)
                throw new InvalidOperationException("clipboard locked");
            return new PresentationClipboardContent(
                SelectionBytes: SelectionBytes,
                PngBytes: HasImage ? ImageBytes : null,
                Text: HasText ? Text : null,
                OwnerToken: OwnerToken,
                RichTextBytes: RichTextBytes,
                XamlPackageBytes: XamlPackageBytes,
                RtfBytes: RtfBytes);
        }

        public bool ContainsImage() => HasImage;
        public bool ContainsText() => HasText;
        public byte[]? GetImagePngBytes() => ImageBytes;
        public string? GetText() => Text;
        public void SetDataObject(DataObject data) =>
            Write(WpfOsClipboard.ReadDataObject(data));

        public void Write(PresentationClipboardContent content)
        {
            if (ThrowOnWrite)
                throw new InvalidOperationException("clipboard locked");
            WasSetCalled   = true;
            LastContent = content;
            SelectionBytes = content.SelectionBytes;
            RichTextBytes = content.RichTextBytes;
            XamlPackageBytes = content.XamlPackageBytes;
            ImageBytes = content.PngBytes;
            Text = content.Text;
            OwnerToken = content.OwnerToken;
            HasImage = content.HasImage;
            HasText = content.HasText;
            // Mimic Windows: every clipboard write bumps the sequence number.
            SequenceNumber++;
        }

        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnRead)
                return ValueTask.FromResult(
                    PlatformClipboardReadResult<PlatformClipboardContent>.Failed("clipboard locked"));
            return ValueTask.FromResult(
                PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                    PresentationClipboardPlatformMapper.ToPlatformContent(
                        Read(),
                        PresentationClipboardPlatformMapper.ResolveNativeScope(),
                        PresentationClipboardPlatformMapper.ResolveNativeXamlPackageFormat(),
                        PresentationClipboardPlatformMapper.ResolveNativeRtfFormat())));
        }

        public ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent content,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnWrite)
                return ValueTask.FromResult(PlatformClipboardWriteResult.Failed("clipboard locked"));
            Write(PresentationClipboardPlatformMapper.FromPlatformContent(content));
            return ValueTask.FromResult(PlatformClipboardWriteResult.Success());
        }

        public ValueTask<PlatformClipboardWriteResult> ClearAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());

        public string? TryGetChangeIdentity() =>
            SequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
        slide.Shapes.Clear();

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
    public void CompatibilityPlanner_OsImagePresent_ReturnsOsImage()
    {
        var action = OsClipboardService.DecidePasteAction(
            osHasImage: true,
            osHasText: false,
            internalHasData: false);

        action.Should().Be(PasteAction.OsImage);
    }

    [Fact]
    public void SharedPlanner_OsTextOnly_ReturnsText()
    {
        var action = PresentationClipboardPastePlanner.Decide(
            hasNativeSelection: false, hasImage: false, hasText: true,
            internalHasData: false, ownCopyIsCurrent: false);

        action.Should().Be(PresentationClipboardPasteSource.Text);
    }

    [Fact]
    public void SharedPlanner_InternalOnly_ReturnsInternal()
    {
        var action = PresentationClipboardPastePlanner.Decide(
            hasNativeSelection: false, hasImage: false, hasText: false,
            internalHasData: true, ownCopyIsCurrent: false);

        action.Should().Be(PresentationClipboardPasteSource.Internal);
    }

    [Fact]
    public void DecidePasteAction_NothingAvailable_ReturnsNothing()
    {
        var action = PresentationClipboardPastePlanner.Decide(
            hasNativeSelection: false, hasImage: false, hasText: false,
            internalHasData: false, ownCopyIsCurrent: false);

        action.Should().Be(PresentationClipboardPasteSource.Nothing);
    }

    [Fact]
    public void SharedPlanner_ImageAndText_ImageWins()
    {
        var action = PresentationClipboardPastePlanner.Decide(
            hasNativeSelection: false, hasImage: true, hasText: true,
            internalHasData: false, ownCopyIsCurrent: false);

        action.Should().Be(PresentationClipboardPasteSource.Image,
            "image takes priority over text in OS-preferred mode");
    }

    [Fact]
    public void SharedPlanner_RichTextPrecedesXamlPackageAndPlainText()
    {
        var source = PresentationClipboardPastePlanner.Decide(
            hasNativeSelection: false,
            hasImage: false,
            hasText: true,
            internalHasData: false,
            ownCopyIsCurrent: false,
            hasRichText: true,
            hasXamlPackage: true);

        source.Should().Be(PresentationClipboardPasteSource.RichText);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Copy payload builder (with fake renderer, no real clipboard)
    // ════════════════════════════════════════════════════════════════════════════════

    [StaFact]
    public void BuildDataObject_WithTextShape_ContainsText()
    {
        var session = MakeSessionWithShape(out _);
        var service = MakeService();
        var dataObj = service.BuildDataObject(
            session.Presentation,
            session.CurrentSlide!,
            session.CurrentSlide!.Shapes);

        // WPF DataObject.SetText stores under UnicodeText (CF_UNICODETEXT).
        dataObj.GetDataPresent(DataFormats.UnicodeText).Should().BeTrue(
            "text from shape's TextBody should be placed on the DataObject");
        var text = (string?)dataObj.GetData(DataFormats.UnicodeText);
        text.Should().Contain("Hello");
    }

    [StaFact]
    public void BuildDataObject_WithStubRenderer_ContainsImage()
    {
        var dataObj = WpfOsClipboard.BuildDataObject(
            new PresentationClipboardContent(PngBytes: _minPng));

        dataObj.GetDataPresent(DataFormats.Bitmap).Should().BeTrue(
            "renderer produced a PNG that should be placed as bitmap on DataObject");
    }

    [StaFact]
    public void BuildDataObject_RendererReturnsEmpty_NoBitmapInDataObject()
    {
        var dataObj = WpfOsClipboard.BuildDataObject(
            new PresentationClipboardContent(PngBytes: [], Text: "Hello"));

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
    public void WpfDataObject_PublishesPublicFormats_AndRoundTripsSharedContent()
    {
        var sess = MakeSessionWithShape(out var shape);
        var selection = PresentationClipboardSelectionCodec.Serialize(
            sess.Presentation,
            sess.CurrentSlide!,
            [shape]);
        var content = new PresentationClipboardContent(
            selection,
            _minPng,
            "Hello",
            "cross-host-owner");

        var dataObject = WpfOsClipboard.BuildDataObject(content);

        WpfOsClipboard.SelectionFormat.Should().Be(PresentationClipboardFormats.Selection);
        WpfOsClipboard.OwnerTokenFormat.Should().Be(PresentationClipboardFormats.OwnerToken);
        WpfOsClipboard.LegacyAvaloniaSelectionFormat.Should().Be(
            "avn-app-fmt:" + PresentationClipboardFormats.Selection);
        WpfOsClipboard.LegacyAvaloniaOwnerTokenFormat.Should().Be(
            "avn-app-fmt:" + PresentationClipboardFormats.OwnerToken);
        dataObject.GetDataPresent(WpfOsClipboard.SelectionFormat, false).Should().BeTrue();
        dataObject.GetDataPresent(WpfOsClipboard.OwnerTokenFormat, false).Should().BeTrue();
        dataObject.GetDataPresent(
            WpfOsClipboard.LegacyAvaloniaSelectionFormat,
            false).Should().BeFalse();
        dataObject.GetDataPresent(
            WpfOsClipboard.LegacyAvaloniaOwnerTokenFormat,
            false).Should().BeFalse();
        dataObject.GetFormats(autoConvert: false).Should().Contain(
            WpfOsClipboard.SelectionFormat,
            WpfOsClipboard.OwnerTokenFormat);
        ((MemoryStream)dataObject.GetData(WpfOsClipboard.SelectionFormat, false)!)
            .ToArray().Should().Equal(selection);
        ((MemoryStream)dataObject.GetData(WpfOsClipboard.OwnerTokenFormat, false)!)
            .ToArray().Should().Equal(
                System.Text.Encoding.Unicode.GetBytes("cross-host-owner\0"));

        var roundTrip = WpfOsClipboard.ReadDataObject(dataObject);
        roundTrip.SelectionBytes.Should().Equal(selection);
        roundTrip.OwnerToken.Should().Be("cross-host-owner");
        roundTrip.Text.Should().Be("Hello");
        roundTrip.HasImage.Should().BeTrue();
        PresentationClipboardSelectionCodec.Deserialize(roundTrip.SelectionBytes!)
            .Should().ContainSingle()
            .Which.Name.Should().Be("TestShape");
    }

    [StaFact]
    public void WpfDataObject_ComPayloadMatchesAvaloniaWin32HGlobalContract()
    {
        var selection = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x11, 0x22, 0x33 };
        var ownerBytes = Encoding.Unicode.GetBytes("native-owner\0");
        var dataObject = WpfOsClipboard.BuildDataObject(new PresentationClipboardContent(
            SelectionBytes: selection,
            OwnerToken: "native-owner"));

        ReadComHGlobal(dataObject, WpfOsClipboard.SelectionFormat)
            .Should().Equal(selection);
        ReadComHGlobal(dataObject, WpfOsClipboard.OwnerTokenFormat)
            .Should().Equal(ownerBytes);
    }

    [StaFact]
    public void WpfDataObject_ReadsLegacyAvaloniaApplicationFormats()
    {
        var dataObject = new DataObject();
        dataObject.SetData(
            WpfOsClipboard.LegacyAvaloniaSelectionFormat,
            new MemoryStream([5, 4, 3], writable: false),
            false);
        dataObject.SetData(
            WpfOsClipboard.LegacyAvaloniaOwnerTokenFormat,
            new MemoryStream(
                System.Text.Encoding.Unicode.GetBytes("legacy-owner\0"),
                writable: false),
            false);

        var content = WpfOsClipboard.ReadDataObject(dataObject);

        content.SelectionBytes.Should().Equal(5, 4, 3);
        content.OwnerToken.Should().Be("legacy-owner");
    }

    [StaFact]
    public void WpfDataObject_ReadsNativeRtfPayload()
    {
        var rtf = Encoding.ASCII.GetBytes(@"{\rtf1\ansi Native RTF}");
        var dataObject = new DataObject();
        dataObject.SetData(DataFormats.Rtf, new MemoryStream(rtf, writable: false), false);

        var content = WpfOsClipboard.ReadDataObject(dataObject);

        content.RtfBytes.Should().Equal(rtf);
        content.HasRichText.Should().BeTrue();
    }

    private static byte[] ReadComHGlobal(DataObject dataObject, string format)
    {
        var formatEtc = new FORMATETC
        {
            cfFormat = unchecked((short)DataFormats.GetDataFormat(format).Id),
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL,
        };
        ((System.Runtime.InteropServices.ComTypes.IDataObject)dataObject)
            .GetData(ref formatEtc, out var medium);

        try
        {
            medium.tymed.Should().Be(TYMED.TYMED_HGLOBAL);
            var size = checked((int)NativeClipboardMemory.GlobalSize(medium.unionmember));
            var source = NativeClipboardMemory.GlobalLock(medium.unionmember);
            source.Should().NotBe(IntPtr.Zero);
            try
            {
                var bytes = new byte[size];
                Marshal.Copy(source, bytes, 0, size);
                return bytes;
            }
            finally
            {
                NativeClipboardMemory.GlobalUnlock(medium.unionmember);
            }
        }
        finally
        {
            NativeClipboardMemory.ReleaseStgMedium(ref medium);
        }
    }

    private static class NativeClipboardMemory
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalUnlock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nuint GlobalSize(IntPtr memory);

        [DllImport("ole32.dll")]
        internal static extern void ReleaseStgMedium(ref STGMEDIUM medium);
    }

    [StaFact]
    public void WpfDataObject_MalformedCustomPayload_PreservesTextFallback()
    {
        var dataObject = new DataObject();
        dataObject.SetData(WpfOsClipboard.SelectionFormat, "not binary data", false);
        dataObject.SetData(WpfOsClipboard.OwnerTokenFormat, new byte[] { 1 }, false);
        dataObject.SetText("surviving text");

        var content = WpfOsClipboard.ReadDataObject(dataObject);

        content.HasSelection.Should().BeFalse();
        content.OwnerToken.Should().BeNull();
        content.Text.Should().Be("surviving text");
    }

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

    [StaFact]
    public void TryPlaceSelectionOnOsClipboard_WriteFailure_RecordsMessageForCallersToSurface()
    {
        var fake = new FakeOsClipboard { ThrowOnWrite = true };
        var sess = MakeSessionWithShape(out _);
        var svc  = new OsClipboardService(fake, new StubShapeRenderer());

        svc.TryPlaceSelectionOnOsClipboard(sess).Should().BeFalse();

        svc.LastWriteFailureMessage.Should().Be("clipboard locked");
    }

    [StaFact]
    public void TryPlaceSelectionOnOsClipboard_Success_ClearsAnyPreviouslyRecordedFailure()
    {
        var fake = new FakeOsClipboard { ThrowOnWrite = true };
        var sess = MakeSessionWithShape(out _);
        var svc  = new OsClipboardService(fake, new StubShapeRenderer());
        svc.TryPlaceSelectionOnOsClipboard(sess).Should().BeFalse();
        svc.LastWriteFailureMessage.Should().NotBeNull();

        fake.ThrowOnWrite = false;
        svc.TryPlaceSelectionOnOsClipboard(sess).Should().BeTrue();

        svc.LastWriteFailureMessage.Should().BeNull();
    }

    [StaFact]
    public void TryPlaceSelectionOnOsClipboard_NoSelection_DoesNotResurfaceAStaleWriteFailure()
    {
        // Sibling no-regression for the null-content early-return path: a prior failed write must
        // not leak its error message onto an unrelated later copy that has nothing selected.
        var fake = new FakeOsClipboard { ThrowOnWrite = true };
        var sess = MakeSessionWithShape(out _);
        var svc  = new OsClipboardService(fake, new StubShapeRenderer());
        svc.TryPlaceSelectionOnOsClipboard(sess).Should().BeFalse();
        svc.LastWriteFailureMessage.Should().NotBeNull();

        sess.ClearSelection();
        svc.TryPlaceSelectionOnOsClipboard(sess).Should().BeFalse();

        svc.LastWriteFailureMessage.Should().BeNull();
    }

    [StaFact]
    public void WpfClipboardCommands_Copy_WriteFailure_InvokesOnWriteFailedWithMessage()
    {
        // End-to-end reproduction of the silent-failure finding: Copy used to swallow the
        // OS-clipboard write exception entirely (WpfClipboardCommands.Copy discarded the bool
        // result), so the caller had no way to tell the user.
        var fake = new FakeOsClipboard { ThrowOnWrite = true };
        var sess = MakeSessionWithShape(out _);
        var svc  = new OsClipboardService(fake, new StubShapeRenderer());
        string? reported = null;

        WpfClipboardCommands.Copy(sess, svc, error => reported = error);

        reported.Should().Be("clipboard locked");
    }

    [StaFact]
    public void WpfClipboardCommands_Copy_Success_DoesNotInvokeOnWriteFailed()
    {
        // Sibling no-regression: a successful copy must not report a failure.
        var fake = new FakeOsClipboard();
        var sess = MakeSessionWithShape(out _);
        var svc  = new OsClipboardService(fake, new StubShapeRenderer());
        var invoked = false;

        WpfClipboardCommands.Copy(sess, svc, _ => invoked = true);

        invoked.Should().BeFalse();
        fake.WasSetCalled.Should().BeTrue();
    }

    [StaFact]
    public void WpfClipboardCommands_Cut_WriteFailure_InvokesOnWriteFailedWithMessage()
    {
        var fake = new FakeOsClipboard { ThrowOnWrite = true };
        var sess = MakeSessionWithShape(out _);
        var svc  = new OsClipboardService(fake, new StubShapeRenderer());
        string? reported = null;

        WpfClipboardCommands.Cut(sess, svc, error => reported = error);

        reported.Should().Be("clipboard locked");
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
    public void Paste_RichTextPayload_InsertsFormattedTextBox()
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run
                {
                    Text = "Rich paste",
                    FontFamily = "Arial",
                    FontSizePt = 18,
                    Bold = true,
                    BoldSet = true,
                    Color = new ThemeAwareColor(SrgbColor.FromRgb(0x1F4E79)),
                },
            },
        });
        var fake = new FakeOsClipboard
        {
            RichTextBytes = InCanvasRichClipboardPlanner.Serialize(
                new InCanvasRichClipboardPayload(body, "Rich paste")),
            HasText = true,
            Text = "plain fallback",
        };
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var service = new OsClipboardService(fake, new StubShapeRenderer());

        var result = service.PasteWithResult(editor);

        result.Should().Be(PresentationClipboardPasteSource.RichText);
        var run = editor.CurrentSlide!.Shapes.Single().TextBody!.Paragraphs.Single().Runs.Single();
        run.Text.Should().Be("Rich paste");
        run.FontFamily.Should().Be("Arial");
        run.FontSizePt.Should().Be(18);
        run.Bold.Should().BeTrue();
        run.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
    }

    [StaFact]
    public void Paste_ExternalRtfPayload_InsertsFormattedTextBox()
    {
        var fake = new FakeOsClipboard
        {
            RtfBytes = Encoding.ASCII.GetBytes(
                @"{\rtf1\ansi{\fonttbl{\f0 Calibri;}}\pard\f0\fs24 Before \b bold\b0\par After}"),
        };
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var service = new OsClipboardService(fake, new StubShapeRenderer());

        var result = service.PasteWithResult(editor);

        result.Should().Be(PresentationClipboardPasteSource.RichText);
        var body = editor.CurrentSlide!.Shapes.Single().TextBody!;
        body.Paragraphs.Should().HaveCount(2);
        body.Paragraphs[0].Runs.Select(run => run.Text).Should().ContainInOrder("Before ", "bold");
        body.Paragraphs[0].Runs.Single(run => run.Text == "bold").Bold.Should().BeTrue();
        body.Paragraphs[1].Runs.Single().Text.Should().Be("After");
    }

    [StaFact]
    public void Paste_ExternalRtfPicture_InsertsPictureAndRetainsText()
    {
        var rtf = Encoding.ASCII.GetBytes(
            @"{\rtf1\ansi Caption {\pict\pngblip " + Convert.ToHexString(_minPng) + @"} After}");
        var fake = new FakeOsClipboard { RtfBytes = rtf };
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var service = new OsClipboardService(fake, new StubShapeRenderer());

        var result = service.PasteWithResult(editor);

        result.Should().Be(PresentationClipboardPasteSource.RichText);
        editor.CurrentSlide!.Shapes.Should().HaveCount(2);
        editor.CurrentSlide.Shapes[0].Kind.Should().Be(SlideShapeKind.Picture);
        editor.CurrentSlide.Shapes[0].Picture!.Bytes.Should().Equal(_minPng);
        editor.CurrentSlide.Shapes[0].Picture!.ContentType.Should().Be("image/png");
        editor.CurrentSlide.Shapes[1].TextBody!.Paragraphs.Single().Runs
            .Should().Contain(run => run.Text.Contains("Caption ", StringComparison.Ordinal));
        editor.CurrentSlide.Shapes[1].TextBody!.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Text)
            .Should().NotContain(text => text.Contains('\uFFFC', StringComparison.Ordinal));
    }

    [StaFact]
    public void Paste_ExternalRtfPicture_PreservesDisplayDimensions()
    {
        var rtf = Encoding.ASCII.GetBytes(
            @"{\rtf1\ansi Caption {\pict\pngblip\picwgoal1440\pichgoal720 "
            + Convert.ToHexString(_minPng) + "} After}");
        var fake = new FakeOsClipboard { RtfBytes = rtf };
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var service = new OsClipboardService(fake, new StubShapeRenderer());

        service.PasteWithResult(editor).Should().Be(PresentationClipboardPasteSource.RichText);
        var picture = editor.CurrentSlide!.Shapes[0];
        picture.ExtentCxEmu.Should().Be(914_400);
        picture.ExtentCyEmu.Should().Be(457_200);
    }

    [StaFact]
    public void Paste_ExternalRtfMultiplePictures_InsertsEveryPictureAndText()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var rtf = Encoding.ASCII.GetBytes(
            @"{\rtf1\ansi Before {\pict\pngblip " + Convert.ToHexString(_minPng)
            + @"} middle {\pict\jpegblip " + Convert.ToHexString(jpeg) + @"} After}");
        var fake = new FakeOsClipboard { RtfBytes = rtf };
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var service = new OsClipboardService(fake, new StubShapeRenderer());

        service.PasteWithResult(editor).Should().Be(PresentationClipboardPasteSource.RichText);
        editor.CurrentSlide!.Shapes.Should().HaveCount(3);
        editor.CurrentSlide.Shapes[0].Picture!.Bytes.Should().Equal(_minPng);
        editor.CurrentSlide.Shapes[1].Picture!.Bytes.Should().Equal(jpeg);
        editor.CurrentSlide.Shapes[1].Picture!.ContentType.Should().Be("image/jpeg");
        editor.CurrentSlide.Shapes[2].TextBody!.Paragraphs.Single().Runs
            .Select(run => run.Text)
            .Should().Contain(text => text.Contains("Before ", StringComparison.Ordinal));
    }

    [StaFact]
    public void Paste_ExternalRtfObject_InsertsEditableObjectAndVisibleResultText()
    {
        var rtf = Encoding.ASCII.GetBytes(
            @"{\rtf1\ansi Before {\object{\*\objclass Word.Document.12}{\*\objdata 010203}{\objresult Embedded result}} After}");
        var fake = new FakeOsClipboard { RtfBytes = rtf };
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var service = new OsClipboardService(fake, new StubShapeRenderer());

        var result = service.PasteWithResult(editor);

        result.Should().Be(PresentationClipboardPasteSource.RichText);
        editor.CurrentSlide!.Shapes.Should().HaveCount(2);
        var objectShape = editor.CurrentSlide.Shapes[0];
        objectShape.Kind.Should().Be(SlideShapeKind.Ole);
        objectShape.OleObject!.EmbeddedBytes.Should().Equal(0x01, 0x02, 0x03);
        objectShape.OleObject.EmbeddedExtension.Should().Be("docx");
        InCanvasTextEditPlanner.ExtractPlainText(
                editor.CurrentSlide.Shapes[1].TextBody)
            .Should().Be("Before Embedded result After");
    }

    [StaFact]
    public void Paste_XamlPackageTable_InsertsNativeEditableTable()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Table><TableRowGroup><TableRow>
                <TableCell Background="#FFF2F2F2" Padding="4,2,6,8"
                           BorderBrush="#FF1F4E79" BorderThickness="1,2,3,4"
                           VerticalContentAlignment="Center"><Paragraph><Bold>Q1</Bold></Paragraph></TableCell>
                <TableCell><Paragraph>42</Paragraph></TableCell>
              </TableRow></TableRowGroup></Table>
            </FlowDocument>
            """;
        var fake = new FakeOsClipboard
        {
            XamlPackageBytes = CreateXamlPackage(xaml),
            HasText = true,
            Text = "plain fallback",
        };
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var service = new OsClipboardService(fake, new StubShapeRenderer());

        var result = service.PasteWithResult(editor);

        result.Should().Be(PresentationClipboardPasteSource.XamlPackage);
        var shape = editor.CurrentSlide!.Shapes.Single();
        shape.Kind.Should().Be(SlideShapeKind.Table);
        shape.Table.Should().NotBeNull();
        shape.Table!.Rows.Should().ContainSingle();
        shape.Table.ColumnWidthsEmu.Should().HaveCount(2);
        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs.Single().Runs
            .Single().Text.Should().Be("Q1");
        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs.Single().Runs
            .Single().Bold.Should().BeTrue();
        var firstCell = shape.Table.Rows[0].Cells[0];
        firstCell.Fill.Should().BeOfType<ShapeFill.Solid>().Which.Color.Resolved
            .Should().Be(SrgbColor.FromRgb(0xF2F2F2));
        firstCell.Anchor.Should().Be(TableCellAnchor.Middle);
        firstCell.InsetLeftPt.Should().Be(3);
        firstCell.InsetTopPt.Should().Be(1.5);
        firstCell.InsetRightPt.Should().Be(4.5);
        firstCell.InsetBottomPt.Should().Be(6);
        firstCell.Borders!.Left.Should().BeOfType<ShapeOutline.Visible>().Which.WidthPt.Should().Be(0.75);
        firstCell.Borders.Top.Should().BeOfType<ShapeOutline.Visible>().Which.WidthPt.Should().Be(1.5);
        firstCell.Borders.Right.Should().BeOfType<ShapeOutline.Visible>().Which.WidthPt.Should().Be(2.25);
        firstCell.Borders.Bottom.Should().BeOfType<ShapeOutline.Visible>().Which.WidthPt.Should().Be(3);
        shape.Table.Rows[0].Cells[1].TextBody!.Paragraphs.Single().Runs
            .Single().Text.Should().Be("42");
    }

    [StaFact]
    public void Paste_ExternalRtfTable_InsertsNativeEditableTable()
    {
        var fake = new FakeOsClipboard
        {
            RtfBytes = Encoding.ASCII.GetBytes(
                @"{\rtf1\ansi\trowd\cellx1440\cellx2880{\b Header}\cell{\i Value}\cell\row}"),
        };
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var service = new OsClipboardService(fake, new StubShapeRenderer());

        var result = service.PasteWithResult(editor);

        result.Should().Be(PresentationClipboardPasteSource.RichText);
        var shape = editor.CurrentSlide!.Shapes.Single();
        shape.Kind.Should().Be(SlideShapeKind.Table);
        shape.Table!.ColumnWidthsEmu.Should().Equal(914400L, 914400L);
        shape.Table.Rows.Single().Cells[0].TextBody!.Paragraphs.Single().Runs
            .Single().Bold.Should().BeTrue();
        shape.Table.Rows.Single().Cells[1].TextBody!.Paragraphs.Single().Runs
            .Single().Italic.Should().BeTrue();
    }

    [StaFact]
    public void Paste_ExternalRtfTable_PreservesSolidCellFillAndBorder()
    {
        var fake = new FakeOsClipboard
        {
            RtfBytes = Encoding.ASCII.GetBytes(
                @"{\rtf1\ansi
{\colortbl;\red255\green255\blue0;\red31\green78\blue121;}
\trowd\clcbpat1\clvertalc\clpadl120\clpadr240\clpadt60\clpadb180\clbrdrl\brdrs\brdrw10\brdrcf2\cellx1440\cellx2880
Header\cell Value\cell\row}"),
        };
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var service = new OsClipboardService(fake, new StubShapeRenderer());

        service.PasteWithResult(editor).Should().Be(PresentationClipboardPasteSource.RichText);
        var cell = editor.CurrentSlide!.Shapes.Single().Table!.Rows.Single().Cells[0];
        ((ShapeFill.Solid)cell.Fill!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0xFFFF00));
        ((ShapeOutline.Visible)cell.Borders!.Left!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
        cell.Anchor.Should().Be(TableCellAnchor.Middle);
        cell.InsetLeftPt.Should().Be(6);
        cell.InsetRightPt.Should().Be(12);
        cell.InsetTopPt.Should().Be(3);
        cell.InsetBottomPt.Should().Be(9);
        ((ShapeOutline.Visible)cell.Borders.Left).WidthPt.Should().Be(0.5);
    }

    [StaFact]
    public void Paste_XamlPackageImage_InsertsPictureFromPackageResource()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <BlockUIContainer><Image Source="Images/pasted.png" Width="96" Height="48" /></BlockUIContainer>
            </FlowDocument>
            """;
        var fake = new FakeOsClipboard
        {
            XamlPackageBytes = CreateXamlPackage(xaml, ("Images/pasted.png", _minPng)),
        };
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var service = new OsClipboardService(fake, new StubShapeRenderer());

        var result = service.PasteWithResult(editor);

        result.Should().Be(PresentationClipboardPasteSource.XamlPackage);
        var picture = editor.CurrentSlide!.Shapes.Single();
        picture.Kind.Should().Be(SlideShapeKind.Picture);
        picture.Picture!.ContentType.Should().Be("image/png");
        picture.Picture.Bytes.Should().Equal(_minPng);
        picture.ExtentCxEmu.Should().Be(914400);
        picture.ExtentCyEmu.Should().Be(457200);
    }

    [StaFact]
    public void Paste_XamlPackageImages_InsertsAllPackageResourcesInOrder()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <BlockUIContainer><Image Source="Images/first.png" /></BlockUIContainer>
              <BlockUIContainer><Image Source="Images/second.jpg" /></BlockUIContainer>
            </FlowDocument>
            """;
        var first = new byte[] { 0x01, 0x02 };
        var second = new byte[] { 0x03, 0x04, 0x05 };
        var fake = new FakeOsClipboard
        {
            XamlPackageBytes = CreateXamlPackage(
                xaml,
                ("Images/first.png", first),
                ("Images/second.jpg", second)),
        };
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var service = new OsClipboardService(fake, new StubShapeRenderer());

        service.PasteWithResult(editor).Should().Be(PresentationClipboardPasteSource.XamlPackage);
        editor.CurrentSlide!.Shapes.Should().HaveCount(2);
        editor.CurrentSlide.Shapes[0].Picture!.Bytes.Should().Equal(first);
        editor.CurrentSlide.Shapes[0].Picture!.ContentType.Should().Be("image/png");
        editor.CurrentSlide.Shapes[1].Picture!.Bytes.Should().Equal(second);
        editor.CurrentSlide.Shapes[1].Picture!.ContentType.Should().Be("image/jpeg");
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

    [StaFact]
    public void Paste_ExternalNativeSelection_PrecedesImageAndText()
    {
        var source = MakeSessionWithShape(out var sourceShape);
        var native = PresentationClipboardSelectionCodec.Serialize(
            source.Presentation,
            source.CurrentSlide!,
            [sourceShape]);
        var fake = new FakeOsClipboard
        {
            SelectionBytes = native,
            HasImage = true,
            ImageBytes = _minPng,
            HasText = true,
            Text = "fallback text",
            OwnerToken = "external-owner",
        };
        var destination = Presentation.CreateEmpty();
        destination.Slides[0].Shapes.Clear();
        var editor = new EditingSession(destination, new PresentationCommandBus(destination));
        var service = new OsClipboardService(fake, new StubShapeRenderer());

        var result = service.PasteWithResult(editor);

        result.Should().Be(PresentationClipboardPasteSource.NativeSelection);
        editor.CurrentSlide!.Shapes.Should().ContainSingle();
        editor.CurrentSlide.Shapes[0].Kind.Should().Be(SlideShapeKind.AutoShape);
        editor.CurrentSlide.Shapes[0].TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Hello");
    }

    [StaFact]
    public void Paste_MalformedNativeSelection_FallsBackToImageThenText()
    {
        var fake = new FakeOsClipboard
        {
            SelectionBytes = [1, 2, 3],
            HasImage = true,
            ImageBytes = _minPng,
            HasText = true,
            Text = "fallback text",
            OwnerToken = "external-owner",
        };
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var service = new OsClipboardService(fake, new StubShapeRenderer());

        var result = service.PasteWithResult(editor);

        result.Should().Be(PresentationClipboardPasteSource.Image);
        editor.CurrentSlide!.Shapes.Should().ContainSingle()
            .Which.Kind.Should().Be(SlideShapeKind.Picture);
    }

    [StaFact]
    public void LockedClipboard_ReadAndWrite_DegradesToInternalClipboard()
    {
        var fake = new FakeOsClipboard { ThrowOnRead = true, ThrowOnWrite = true };
        var editor = MakeSessionWithShape(out _);
        var service = new OsClipboardService(fake, new StubShapeRenderer());
        editor.CopySelectedShapes();

        service.TryPlaceSelectionOnOsClipboard(editor).Should().BeFalse();
        editor.ClearSelection();
        var result = service.PasteWithResult(editor);

        result.Should().Be(PresentationClipboardPasteSource.Internal);
        editor.CurrentSlide!.Shapes.Should().HaveCount(2);
        service.OwnCopyIsCurrentOnOs.Should().BeFalse();
    }

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

    // ════════════════════════════════════════════════════════════════════════════════
    //  Y6 — in-app copy→paste round-trip produces an editable shape, not a picture
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Y6: after an in-app copy (CopySelectedShapes + PlaceSelectionOnOsClipboard), a
    /// subsequent Paste should return the editable deep-copied shape from the INTERNAL
    /// clipboard, NOT a rasterised picture from the OS clipboard.
    /// </summary>
    [StaFact]
    public void Y6_InAppCopyThenPaste_YieldsEditableShape_NotPicture()
    {
        // OS clipboard reports an image (the PNG we placed during copy).
        var fake = new FakeOsClipboard { HasImage = true, ImageBytes = _minPng };
        var sess = MakeSessionWithShape(out var original);
        var svc  = new OsClipboardService(fake, new StubShapeRenderer());

        // Simulate in-app copy: internal clipboard + OS clipboard.
        sess.CopySelectedShapes();
        svc.PlaceSelectionOnOsClipboard(sess);

        // The OS clipboard has an image, but it came from THIS app.
        svc.OwnCopyIsCurrentOnOs.Should().BeTrue(
            "generation token should be set after PlaceSelectionOnOsClipboard");

        sess.ClearSelection();
        var before = sess.CurrentSlide!.Shapes.Count;

        svc.Paste(sess, preferOsClipboard: true);

        sess.CurrentSlide!.Shapes.Count.Should().Be(before + 1,
            "one shape should be pasted");

        var pasted = sess.CurrentSlide!.Shapes.Last();
        pasted.Kind.Should().NotBe(SlideShapeKind.Picture,
            "Y6: in-app paste must not flatten to a picture");
        pasted.Kind.Should().Be(original.Kind,
            "Y6: pasted shape kind should match the original AutoShape");
        pasted.Id.Should().NotBe(original.Id,
            "pasted shape must have a fresh Id");
    }

    /// <summary>
    /// Y6 boundary: pasting an image that came from ANOTHER app (no own-copy token)
    /// still inserts a Picture shape — the external-image path must be unaffected.
    /// </summary>
    [StaFact]
    public void Y6_ExternalImagePaste_StillInsertsPicture()
    {
        // OS clipboard has an image but we never called PlaceSelectionOnOsClipboard
        // → OwnCopyIsCurrentOnOs is false.
        var fake = new FakeOsClipboard { HasImage = true, ImageBytes = _minPng };
        var sess = MakeSessionWithShape(out _);
        sess.ClearSelection();
        var svc    = new OsClipboardService(fake, new StubShapeRenderer());
        var before = sess.CurrentSlide!.Shapes.Count;

        svc.Paste(sess, preferOsClipboard: true);

        sess.CurrentSlide!.Shapes.Count.Should().Be(before + 1);
        sess.CurrentSlide!.Shapes.Last().Kind.Should().Be(SlideShapeKind.Picture,
            "Y6 boundary: external image → Picture shape");
    }

    /// <summary>
    /// Y6: DecidePasteAction returns Internal when ownCopyIsCurrentOnOs=true even though
    /// the OS has an image.
    /// </summary>
    [Fact]
    public void Y6_DecidePasteAction_OwnCopyOnOs_PrefersInternal()
    {
        var action = PresentationClipboardPastePlanner.Decide(
            hasNativeSelection: true,
            hasImage: true,
            hasText: true,
            internalHasData: true,
            ownCopyIsCurrent: true);

        action.Should().Be(PresentationClipboardPasteSource.Internal,
            "Y6: own-copy token + internal data → Internal wins over OS image");
    }

    /// <summary>
    /// Y6: own-copy token with NO internal data still falls through to OsImage.
    /// </summary>
    [Fact]
    public void Y6_DecidePasteAction_OwnCopyOnOs_NoInternal_FallsToOsImage()
    {
        var action = PresentationClipboardPastePlanner.Decide(
            hasNativeSelection: false,
            hasImage: true,
            hasText: false,
            internalHasData: false,
            ownCopyIsCurrent: true);

        action.Should().Be(PresentationClipboardPasteSource.Image,
            "Y6 boundary: own-copy but no internal data → OsImage (nothing to prefer)");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Y7 — cut populates OS clipboard BEFORE delete clears selection
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Y7: simulates the corrected Ctrl+X sequence from MainWindow:
    ///   CopySelectedShapes → PlaceSelectionOnOsClipboard → DeleteSelected
    /// Verifies that (a) the OS clipboard is written, and (b) paste-after-cut yields
    /// the cut shapes (editable), not stale OS content.
    /// </summary>
    [StaFact]
    public void Y7_CutSequence_PopulatesOsClipboard_BeforeDelete()
    {
        var fake = new FakeOsClipboard();    // empty OS clipboard before cut
        var sess = MakeSessionWithShape(out var original);
        var svc  = new OsClipboardService(fake, new StubShapeRenderer());

        // Corrected Ctrl+X sequence (mirrors the fixed MainWindow binding).
        sess.CopySelectedShapes();
        svc.PlaceSelectionOnOsClipboard(sess);
        sess.DeleteSelected();

        // (a) OS clipboard was written.
        fake.WasSetCalled.Should().BeTrue(
            "Y7: OS clipboard must be populated during the cut sequence");

        // (b) Internal clipboard still has data (CopySelectedShapes ran before Delete).
        sess.CanPaste.Should().BeTrue(
            "Y7: internal clipboard must survive DeleteSelected");

        // (c) Paste-after-cut yields the cut shape (editable), not a picture.
        var before = sess.CurrentSlide!.Shapes.Count;
        svc.Paste(sess, preferOsClipboard: true);

        sess.CurrentSlide!.Shapes.Count.Should().Be(before + 1,
            "Y7: paste-after-cut should insert the cut shapes");
        sess.CurrentSlide!.Shapes.Last().Kind.Should().NotBe(SlideShapeKind.Picture,
            "Y7: paste-after-cut must produce an editable shape, not a picture");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Y8/Y9 — OS-text paste uses InsertTextBox(text) — single command, multi-line
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Y8: the inserted textbox carries the clipboard text as part of the initial shape
    /// (verified by reading the text back from the model — if the mutation happened
    /// outside the command bus the undo→redo cycle would lose the text).
    /// </summary>
    [StaFact]
    public void Y8_OsTextPaste_TextIsInShapeBeforeAnyMutation()
    {
        const string clipText = "Pasted from clipboard";
        var fake = new FakeOsClipboard { HasText = true, Text = clipText };
        var sess = MakeSessionWithShape(out _);
        sess.ClearSelection();
        var svc    = new OsClipboardService(fake, new StubShapeRenderer());
        var before = sess.CurrentSlide!.Shapes.Count;

        svc.Paste(sess, preferOsClipboard: true);

        sess.CurrentSlide!.Shapes.Count.Should().Be(before + 1);
        var inserted = sess.CurrentSlide!.Shapes.Last();
        inserted.Kind.Should().Be(SlideShapeKind.AutoShape);
        var body = inserted.TextBody;
        body.Should().NotBeNull();
        var allText = string.Concat(body!.Paragraphs.SelectMany(p => p.Runs).Select(r => r.Text));
        allText.Should().Contain(clipText.Replace("\n", "").Replace("\r", ""),
            "Y8: clipboard text must be in the shape model after paste");
    }

    /// <summary>
    /// Y9: multi-line clipboard text is split into separate paragraphs.
    /// </summary>
    [StaFact]
    public void Y9_MultiLinePaste_SplitsIntoParagraphs()
    {
        const string clipText = "Line one\nLine two\nLine three";
        var fake = new FakeOsClipboard { HasText = true, Text = clipText };
        var sess = MakeSessionWithShape(out _);
        sess.ClearSelection();
        var svc = new OsClipboardService(fake, new StubShapeRenderer());

        svc.Paste(sess, preferOsClipboard: true);

        var inserted = sess.CurrentSlide!.Shapes.Last();
        inserted.TextBody.Should().NotBeNull();
        inserted.TextBody!.Paragraphs.Count.Should().Be(3,
            "Y9: three newline-separated lines should produce three paragraphs");
        inserted.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Line one");
        inserted.TextBody!.Paragraphs[1].Runs[0].Text.Should().Be("Line two");
        inserted.TextBody!.Paragraphs[2].Runs[0].Text.Should().Be("Line three");
    }

    /// <summary>
    /// Y9: empty clipboard text still inserts a valid (empty) textbox with at least one run.
    /// </summary>
    [StaFact]
    public void Y9_EmptyTextPaste_DoesNotInsertBox()
    {
        // OsClipboardService.Paste guards IsNullOrEmpty before calling InsertTextBox.
        var fake = new FakeOsClipboard { HasText = true, Text = "" };
        var sess = MakeSessionWithShape(out _);
        sess.ClearSelection();
        var svc    = new OsClipboardService(fake, new StubShapeRenderer());
        var before = sess.CurrentSlide!.Shapes.Count;

        svc.Paste(sess, preferOsClipboard: true);

        // Empty text → guard in Paste() → no shape inserted.
        sess.CurrentSlide!.Shapes.Count.Should().Be(before,
            "Y9: empty OS text should not insert a textbox");
    }

    private static byte[] CreateXamlPackage(
        string xaml,
        params (string Name, byte[] Bytes)[] resources)
    {
        using var output = new MemoryStream();
        using (var package = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var resource in resources)
            {
                var entry = package.CreateEntry(resource.Name);
                using var stream = entry.Open();
                stream.Write(resource.Bytes);
            }

            var xamlEntry = package.CreateEntry("Xaml/Document.xaml");
            using var writer = new StreamWriter(xamlEntry.Open(), Encoding.UTF8);
            writer.Write(xaml);
        }
        return output.ToArray();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Z1 — own-copy token invalidated by external clipboard write (sequence number)
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Z1 (a): in-app copy followed immediately by in-app paste (sequence number unchanged)
    /// still prefers the internal editable clipboard (existing Y6 behavior preserved).
    /// </summary>
    [StaFact]
    public void Z1_InAppCopyThenPaste_SequenceUnchanged_PrefersInternal()
    {
        var fake = new FakeOsClipboard { HasImage = true, ImageBytes = _minPng };
        var sess = MakeSessionWithShape(out var original);
        var svc  = new OsClipboardService(fake, new StubShapeRenderer());

        // Simulate in-app copy: internal clipboard + OS clipboard.
        sess.CopySelectedShapes();
        svc.PlaceSelectionOnOsClipboard(sess);

        // OwnCopyIsCurrentOnOs should be true: sequence number matches.
        svc.OwnCopyIsCurrentOnOs.Should().BeTrue(
            "Z1: sequence number matches our last write → own copy is current");

        sess.ClearSelection();
        var before = sess.CurrentSlide!.Shapes.Count;

        svc.Paste(sess, preferOsClipboard: true);

        sess.CurrentSlide!.Shapes.Count.Should().Be(before + 1);
        var pasted = sess.CurrentSlide!.Shapes.Last();
        pasted.Kind.Should().NotBe(SlideShapeKind.Picture,
            "Z1 (a): sequence unchanged → own-copy still current → editable shape, not picture");
        pasted.Kind.Should().Be(original.Kind);
    }

    /// <summary>
    /// Z1 (b): in-app copy, then an external app overwrites the OS clipboard (fake sequence
    /// number bumped).  Paste must detect the external change and return OsImage, NOT the
    /// stale internal shape.  This is the regression scenario.
    /// </summary>
    [StaFact]
    public void Z1_ExternalClipboardChangeAfterInAppCopy_PastesExternalImage_NotStaleShape()
    {
        var fake = new FakeOsClipboard { HasImage = true, ImageBytes = _minPng };
        var sess = MakeSessionWithShape(out _);
        var svc  = new OsClipboardService(fake, new StubShapeRenderer());

        // In-app copy: internal clipboard + OS clipboard.
        sess.CopySelectedShapes();
        svc.PlaceSelectionOnOsClipboard(sess);

        svc.OwnCopyIsCurrentOnOs.Should().BeTrue("own copy should be current right after copy");

        // Simulate another app writing to the clipboard (bumps sequence number).
        fake.SequenceNumber++;
        fake.SelectionBytes = null;
        fake.OwnerToken = null;
        // The OS clipboard now contains an external image (already set via HasImage=true).

        // OwnCopyIsCurrentOnOs must now be false.
        svc.OwnCopyIsCurrentOnOs.Should().BeFalse(
            "Z1: sequence number changed by external app → own copy is stale");

        sess.ClearSelection();
        var before = sess.CurrentSlide!.Shapes.Count;

        svc.Paste(sess, preferOsClipboard: true);

        sess.CurrentSlide!.Shapes.Count.Should().Be(before + 1,
            "an external image was on the OS clipboard → one shape inserted");
        var pasted = sess.CurrentSlide!.Shapes.Last();
        pasted.Kind.Should().Be(SlideShapeKind.Picture,
            "Z1 (b): external clipboard change → paste must use external image (Picture), NOT stale internal shape");
    }

    /// <summary>
    /// Z1 (c): external image with no prior in-app copy (own-copy token never set)
    /// → paste returns OsImage.  Sanity check that the baseline path is unaffected.
    /// </summary>
    [StaFact]
    public void Z1_ExternalImageWithNoPriorInAppCopy_PastesOsImage()
    {
        // OS clipboard has an image but we never called PlaceSelectionOnOsClipboard.
        var fake = new FakeOsClipboard { HasImage = true, ImageBytes = _minPng };
        var sess = MakeSessionWithShape(out _);
        sess.ClearSelection();
        var svc    = new OsClipboardService(fake, new StubShapeRenderer());
        var before = sess.CurrentSlide!.Shapes.Count;

        svc.OwnCopyIsCurrentOnOs.Should().BeFalse(
            "Z1 (c): no in-app copy ever done → own copy is never current");

        svc.Paste(sess, preferOsClipboard: true);

        sess.CurrentSlide!.Shapes.Count.Should().Be(before + 1);
        sess.CurrentSlide!.Shapes.Last().Kind.Should().Be(SlideShapeKind.Picture,
            "Z1 (c): external image, no prior in-app copy → Picture");
    }
}
