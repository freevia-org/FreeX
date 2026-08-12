using Free.Shared.AppServices;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationClipboardWorkflowTests
{
    [Fact]
    public void CommitCopy_UsesCapturedSourceAndRestoresLiveSelection()
    {
        var (editor, slide) = CreateEditor();
        editor.Select(1);
        var request = PresentationClipboardWorkflow.PrepareInternalWrite(editor);
        editor.Select(2);

        PresentationClipboardWorkflow.CommitCopy(request);

        editor.SelectedShapeIds.Should().Equal(2u);
        editor.CanPaste.Should().BeTrue();
        editor.Paste();
        slide.Shapes.Should().HaveCount(3);
        slide.Shapes[^1].Name.Should().Be("First");
    }

    [Fact]
    public void CommitCut_ReturnsToCapturedSlideAndKeepsInternalClipboard()
    {
        var (editor, slide) = CreateEditor();
        editor.Select(1);
        var request = PresentationClipboardWorkflow.PrepareInternalWrite(editor);
        editor.Presentation.Slides.Add(new Slide { Title = "Second slide" });
        editor.SelectSlide(1);

        PresentationClipboardWorkflow.CommitCut(request);

        editor.CurrentSlideIndex.Should().Be(0);
        slide.Shapes.Select(shape => shape.Id).Should().Equal(2u);
        editor.CanPaste.Should().BeTrue();
        editor.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void CommitCut_InvokesNativeWriteHookBeforeDelete()
    {
        var (editor, slide) = CreateEditor();
        editor.Select(1);
        var request = PresentationClipboardWorkflow.PrepareInternalWrite(editor);
        var shapeCountDuringWrite = 0;

        PresentationClipboardWorkflow.CommitCut(
            request,
            () =>
            {
                shapeCountDuringWrite = slide.Shapes.Count;
            });

        shapeCountDuringWrite.Should().Be(2);
        slide.Shapes.Select(shape => shape.Id).Should().Equal(2u);
    }

    [Fact]
    public void ApplyPaste_MalformedNativeSelectionFallsBackToImage()
    {
        var (editor, slide) = CreateEditor();
        var request = PresentationClipboardWorkflow.PreparePaste(editor);
        var content = new PresentationClipboardContent(
            SelectionBytes: [1, 2, 3],
            PngBytes: [10, 20, 30],
            Text: "fallback");

        var source = PresentationClipboardWorkflow.ApplyPaste(
            request,
            content,
            ownCopyIsCurrent: false);

        source.Should().Be(PresentationClipboardPasteSource.Image);
        slide.Shapes.Should().HaveCount(3);
        slide.Shapes[^1].Kind.Should().Be(SlideShapeKind.Picture);
    }

    [Fact]
    public void ApplyPaste_RichTextCreatesEditableTextBox()
    {
        var (editor, slide) = CreateEditor();
        var payload = InCanvasRichClipboardPayload.FromPlainText("Portable rich text");
        var content = new PresentationClipboardContent(
            RichTextBytes: InCanvasRichClipboardPlanner.Serialize(payload));

        var source = PresentationClipboardWorkflow.ApplyPaste(
            PresentationClipboardWorkflow.PreparePaste(editor),
            content,
            ownCopyIsCurrent: false);

        source.Should().Be(PresentationClipboardPasteSource.RichText);
        slide.Shapes.Should().HaveCount(3);
        ExtractText(slide.Shapes[^1]).Should().Be("Portable rich text");
    }

    [Fact]
    public void ApplyPaste_PreferInternalIgnoresAvailableSystemImage()
    {
        var (editor, slide) = CreateEditor();
        editor.Select(1);
        editor.CopySelectedShapes();

        var source = PresentationClipboardWorkflow.ApplyPaste(
            PresentationClipboardWorkflow.PreparePaste(editor),
            new PresentationClipboardContent(PngBytes: [10, 20, 30]),
            ownCopyIsCurrent: false,
            preferSystemClipboard: false);

        source.Should().Be(PresentationClipboardPasteSource.Internal);
        slide.Shapes[^1].Name.Should().Be("First");
        slide.Shapes[^1].Kind.Should().Be(SlideShapeKind.AutoShape);
    }

    [Fact]
    public void RichFormatResolver_PrefersFreePPayloadOverExternalFormats()
    {
        var preferred = InCanvasRichClipboardPayload.FromPlainText("FreeP payload");
        var external = InCanvasRichClipboardPayload.FromPlainText("external payload");
        var content = new PresentationClipboardContent(
            Text: "plain text",
            RichTextBytes: InCanvasRichClipboardPlanner.Serialize(preferred),
            XamlPackageBytes: ExternalXamlClipboardPlanner.SerializeXamlPackage(external),
            RtfBytes: ExternalRichTextClipboardPlanner.SerializeRtf(external));

        var resolution = InCanvasRichClipboardFormatResolver.Resolve(content);

        resolution.Source.Should().Be(PresentationClipboardPasteSource.RichText);
        resolution.Payload!.PlainText.Should().Be("FreeP payload");
    }

    [Fact]
    public void RichFormatResolver_UsesXamlBeforeRtfAndPlainText()
    {
        var xaml = InCanvasRichClipboardPayload.FromPlainText("XAML payload");
        var rtf = InCanvasRichClipboardPayload.FromPlainText("RTF payload");
        var content = new PresentationClipboardContent(
            Text: "plain text",
            RichTextBytes: [1, 2, 3],
            XamlPackageBytes: ExternalXamlClipboardPlanner.SerializeXamlPackage(xaml),
            RtfBytes: ExternalRichTextClipboardPlanner.SerializeRtf(rtf));

        var resolution = InCanvasRichClipboardFormatResolver.Resolve(content);

        resolution.Source.Should().Be(PresentationClipboardPasteSource.XamlPackage);
        resolution.Payload!.PlainText.Should().Be("XAML payload");
    }

    [Fact]
    public void RichFormatResolver_FallsBackToPlainTextAfterMalformedRichFormats()
    {
        var resolution = InCanvasRichClipboardFormatResolver.Resolve(
            new PresentationClipboardContent(
                Text: "plain fallback",
                RichTextBytes: [1],
                XamlPackageBytes: [2],
                RtfBytes: [3]));

        resolution.Source.Should().Be(PresentationClipboardPasteSource.Text);
        resolution.Payload!.PlainText.Should().Be("plain fallback");
    }

    [Fact]
    public void OwnershipTracker_RequiresMatchingOwnerIdentityAndInternalData()
    {
        var tracker = new PresentationClipboardOwnershipTracker();
        var content = new PresentationClipboardContent(
            SelectionBytes: [1],
            OwnerToken: "owner");
        tracker.RecordSuccessfulWrite(content, "revision-7");

        tracker.HasCurrentPlatformIdentity("revision-7").Should().BeTrue();
        tracker.IsCurrent(content, "revision-7", internalHasData: true).Should().BeTrue();
        tracker.IsCurrent(content, "revision-8", internalHasData: true).Should().BeFalse();
        tracker.IsCurrent(
            content with { OwnerToken = "external" },
            "revision-7",
            internalHasData: true).Should().BeFalse();
        tracker.IsCurrent(content, "revision-7", internalHasData: false).Should().BeFalse();

        tracker.Invalidate();
        tracker.HasCurrentPlatformIdentity("revision-7").Should().BeFalse();
    }

    [Fact]
    public void ContentIdentity_UsesHostPngNormalization()
    {
        var first = new PresentationClipboardContent(
            SelectionBytes: [1],
            PngBytes: [2],
            Text: "same",
            OwnerToken: "owner");
        var second = first with { PngBytes = [3] };

        PresentationClipboardContentIdentity.Compute(first, _ => [9])
            .Should().Be(PresentationClipboardContentIdentity.Compute(second, _ => [9]));
        PresentationClipboardContentIdentity.Compute(first)
            .Should().NotBe(PresentationClipboardContentIdentity.Compute(second));
        PresentationClipboardContentIdentity.Compute(first, _ => null)
            .Should().Be(PresentationClipboardContentIdentity.Compute(first with { PngBytes = null }));
    }

    [Fact]
    public async Task OperationQueue_SerializesCommandsAndContinuesAfterFailure()
    {
        var queue = new PresentationClipboardOperationQueue();
        var events = new List<string>();
        var first = queue.Enqueue(() =>
        {
            events.Add("first");
            return Task.FromException(new InvalidOperationException("clipboard unavailable"));
        });
        var second = queue.Enqueue(() =>
        {
            events.Add("second");
            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        await second;

        events.Should().Equal("first", "second");
        queue.Completion.Should().BeSameAs(second);
    }

    [Fact]
    public async Task PlatformSession_CutWritesBeforeDeleteAndKeepsInternalClipboard()
    {
        var (editor, slide) = CreateEditor();
        editor.Select(1);
        var clipboard = new FakePlatformClipboard();
        var shapeCountDuringWrite = 0;
        clipboard.BeforeWrite = () => shapeCountDuringWrite = slide.Shapes.Count;
        var session = CreatePlatformSession(clipboard);

        var written = await session.CutAsync(editor);

        written.Should().BeTrue();
        shapeCountDuringWrite.Should().Be(2);
        slide.Shapes.Select(shape => shape.Id).Should().Equal(2u);
        editor.CanPaste.Should().BeTrue();
    }

    [Fact]
    public async Task PlatformSession_WriteFailureOwnsFeedbackAndStillCompletesCut()
    {
        var (editor, slide) = CreateEditor();
        editor.Select(1);
        var clipboard = new FakePlatformClipboard { FailWrites = true };
        var session = CreatePlatformSession(clipboard);

        var written = await session.CutAsync(editor);

        written.Should().BeFalse();
        session.LastWriteFailureMessage.Should().Be("clipboard locked");
        slide.Shapes.Select(shape => shape.Id).Should().Equal(2u);
        editor.CanPaste.Should().BeTrue();
    }

    [Fact]
    public async Task PlatformSession_ExternalIdentityChangeInvalidatesOwnCopyAndUsesExternalImage()
    {
        var (editor, slide) = CreateEditor();
        editor.Select(1);
        var clipboard = new FakePlatformClipboard();
        var session = CreatePlatformSession(clipboard);
        (await session.CopyAsync(editor)).Should().BeTrue();

        clipboard.ReplaceExternally(new PresentationClipboardContent(PngBytes: [10, 20, 30]));
        var source = await session.PasteAsync(editor);

        source.Should().Be(PresentationClipboardPasteSource.Image);
        slide.Shapes[^1].Kind.Should().Be(SlideShapeKind.Picture);
        session.OwnCopyHasCurrentPlatformIdentity.Should().BeFalse();
    }

    [Fact]
    public void PlatformAdapters_KeepClipboardDecisionsInSharedWorkflow()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var workflow = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "Core",
            "PresentationClipboardWorkflow.cs"));
        var avaloniaService = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "PresentationClipboardService.cs"));
        var avaloniaWindow = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));
        var wpfService = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "OsClipboardService.cs"));
        var wpfRichAdapter = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Wpf",
            "WpfRichTextClipboardAdapter.cs"));
        var avaloniaRichAdapter = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "AvaloniaRichTextEditor.cs"));

        workflow.Should().Contain("PresentationClipboardWorkflow");
        workflow.Should().Contain("PresentationPlatformClipboardSession");
        workflow.Should().Contain("PresentationClipboardOwnershipTracker");
        workflow.Should().Contain("PresentationClipboardOperationQueue");
        workflow.Should().Contain("ApplyRichPayload");

        wpfService.Should().Contain("PresentationPlatformClipboardSession")
            .And.NotContain("PresentationClipboardOwnershipTracker")
            .And.NotContain("PresentationClipboardWorkflow.ApplyPaste(");
        avaloniaWindow.Should().Contain("PresentationPlatformClipboardSession")
            .And.NotContain("PresentationClipboardOwnershipTracker")
            .And.NotContain("PresentationClipboardWorkflow.ApplyPaste(");
        avaloniaService.Should().NotContain("PresentationClipboardOwnershipTracker")
            .And.NotContain("AvaloniaPresentationClipboardService")
            .And.NotContain("IPresentationSystemClipboard");

        avaloniaService.Should().NotContain("IncrementalHash");
        avaloniaWindow.Should().Contain("PresentationClipboardOperationQueue");
        avaloniaWindow.Should().NotContain("RunClipboardOperationAsync");

        foreach (var richAdapter in new[] { wpfRichAdapter, avaloniaRichAdapter })
        {
            richAdapter.Should().Contain("InCanvasRichClipboardFormatResolver.Resolve(");
            richAdapter.Should().NotContain("InCanvasRichClipboardPlanner.Deserialize(");
            richAdapter.Should().NotContain("ExternalXamlClipboardPlanner.TryParseXamlPackage(");
            richAdapter.Should().NotContain("ExternalRichTextClipboardPlanner.TryParseRtf(");
        }
    }

    private static (EditingSession Editor, Slide Slide) CreateEditor()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.Add(CreateShape(1, "First", "First text"));
        slide.Shapes.Add(CreateShape(2, "Second", "Second text"));
        return (
            new EditingSession(presentation, new PresentationCommandBus(presentation)),
            slide);
    }

    private static PresentationPlatformClipboardSession CreatePlatformSession(
        IPlatformClipboard clipboard) =>
        new(
            clipboard,
            static (_, _, _) => [1, 2, 3],
            static content => PresentationClipboardPlatformMapper.ToPlatformContent(content),
            PresentationClipboardPlatformIdentityStrategy.ChangeIdentity);

    private static SlideShape CreateShape(uint id, string name, string text)
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = text } } });
        return new SlideShape
        {
            Id = id,
            Name = name,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = id * 914_400,
            OffsetYEmu = 457_200,
            ExtentCxEmu = 1_828_800,
            ExtentCyEmu = 914_400,
            TextBody = body,
        };
    }

    private static string ExtractText(SlideShape shape) =>
        string.Concat(shape.TextBody!.Paragraphs.SelectMany(p => p.Runs).Select(run => run.Text));

    private sealed class FakePlatformClipboard : IPlatformClipboard
    {
        private PresentationClipboardContent _content = new();
        private int _revision;

        public bool FailWrites { get; init; }
        public Action? BeforeWrite { get; set; }

        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                    PresentationClipboardPlatformMapper.ToPlatformContent(_content)));

        public ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent content,
            CancellationToken cancellationToken = default)
        {
            if (FailWrites)
                return ValueTask.FromResult(PlatformClipboardWriteResult.Failed("clipboard locked"));

            BeforeWrite?.Invoke();
            _content = PresentationClipboardPlatformMapper.FromPlatformContent(content);
            _revision++;
            return ValueTask.FromResult(PlatformClipboardWriteResult.Success());
        }

        public ValueTask<PlatformClipboardWriteResult> ClearAsync(
            CancellationToken cancellationToken = default)
        {
            _content = new PresentationClipboardContent();
            _revision++;
            return ValueTask.FromResult(PlatformClipboardWriteResult.Success());
        }

        public string TryGetChangeIdentity() => _revision.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public void ReplaceExternally(PresentationClipboardContent content)
        {
            _content = content;
            _revision++;
        }
    }
}
