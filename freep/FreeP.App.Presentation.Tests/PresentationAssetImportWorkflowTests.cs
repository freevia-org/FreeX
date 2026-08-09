using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationAssetImportWorkflowTests
{
    [Theory]
    [InlineData(PresentationAssetImportKind.Picture, "photo.jpg", "image/jpeg")]
    [InlineData(PresentationAssetImportKind.Video, "clip.mov", "video/quicktime")]
    [InlineData(PresentationAssetImportKind.Audio, "sound.wav", "audio/wav")]
    [InlineData(PresentationAssetImportKind.TransitionSound, "sound.m4a", "audio/mp4")]
    [InlineData(PresentationAssetImportKind.PictureBullet, "bullet.gif", "image/gif")]
    [InlineData(PresentationAssetImportKind.SmartArtPicture, "node.svg", "image/svg+xml")]
    [InlineData(PresentationAssetImportKind.ZoomCoverImage, "cover.bmp", "image/bmp")]
    public async Task ImportAsync_BuildsTypedPayloadAndExecutes(
        PresentationAssetImportKind kind,
        string sourceName,
        string expectedContentType)
    {
        var bytes = new byte[] { 1, 2, 3 };
        PresentationAssetImportPayload? executed = null;
        var workflow = CreateWorkflow(
            PresentationAssetPickerResult.Selected(sourceName, "source"),
            bytes,
            payload =>
            {
                executed = payload;
                return PresentationAssetImportExecutionResult.Success;
            });

        var result = await workflow.ImportAsync(kind);

        result.Status.Should().Be(PresentationAssetImportStatus.Succeeded);
        result.SourceName.Should().Be(sourceName);
        executed.Should().NotBeNull();
        var payload = executed!;
        payload.Bytes.Should().BeSameAs(bytes);
        payload.ContentType.Should().Be(expectedContentType);
        if (kind == PresentationAssetImportKind.Picture)
            payload.Picture.Should().NotBeNull();
        else
            payload.Picture.Should().BeNull();

        if (kind is PresentationAssetImportKind.Video or PresentationAssetImportKind.Audio)
            payload.Media.Should().NotBeNull();
        else
            payload.Media.Should().BeNull();

        if (kind == PresentationAssetImportKind.PictureBullet)
            payload.PictureBullet.Should().NotBeNull();
        else
            payload.PictureBullet.Should().BeNull();
    }

    [Fact]
    public async Task ImportAsync_EmbeddedObjectPreservesOpaqueBytesWithoutContentType()
    {
        var bytes = new byte[] { 8, 9 };
        PresentationAssetImportPayload? executed = null;
        var workflow = CreateWorkflow(
            PresentationAssetPickerResult.Selected("sheet.xlsx", "source"),
            bytes,
            payload =>
            {
                executed = payload;
                return PresentationAssetImportExecutionResult.Success;
            });

        var result = await workflow.ImportAsync(PresentationAssetImportKind.EmbeddedObject);

        result.Succeeded.Should().BeTrue();
        executed.Should().NotBeNull();
        var payload = executed!;
        payload.Bytes.Should().BeSameAs(bytes);
        payload.ContentType.Should().BeNull();
    }

    [Theory]
    [InlineData(PresentationAssetPickerStatus.Cancelled, PresentationAssetImportStatus.Cancelled)]
    [InlineData(PresentationAssetPickerStatus.Unavailable, PresentationAssetImportStatus.Unavailable)]
    public async Task ImportAsync_PickerNonSelectionSkipsReadAndExecution(
        PresentationAssetPickerStatus pickerStatus,
        PresentationAssetImportStatus expectedStatus)
    {
        var reader = new StubReader { Exception = new InvalidOperationException("reader should not run") };
        var execution = new StubExecution(_ => throw new InvalidOperationException("execution should not run"));
        var pickerResult = pickerStatus == PresentationAssetPickerStatus.Cancelled
            ? PresentationAssetPickerResult.Cancelled
            : PresentationAssetPickerResult.Unavailable("No native picker");
        var workflow = new PresentationAssetImportWorkflow(
            new StubPicker(pickerResult),
            reader,
            execution);

        var result = await workflow.ImportAsync(PresentationAssetImportKind.Picture);

        result.Status.Should().Be(expectedStatus);
        result.Message.Should().Be(
            pickerStatus == PresentationAssetPickerStatus.Unavailable ? "No native picker" : null);
        reader.CallCount.Should().Be(0);
        execution.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ImportAsync_ReadFailureBecomesFailedResult()
    {
        var workflow = new PresentationAssetImportWorkflow(
            new StubPicker(PresentationAssetPickerResult.Selected("broken.png", "source")),
            new StubReader { Exception = new IOException("read failed") },
            new StubExecution(_ => PresentationAssetImportExecutionResult.Success));

        var result = await workflow.ImportAsync(PresentationAssetImportKind.Picture);

        result.Status.Should().Be(PresentationAssetImportStatus.Failed);
        result.SourceName.Should().Be("broken.png");
        result.Message.Should().Be("read failed");
        result.Exception.Should().BeOfType<IOException>();
    }

    [Fact]
    public async Task ImportAsync_ExecutionNoOpIsNotAFalseSuccess()
    {
        var workflow = CreateWorkflow(
            PresentationAssetPickerResult.Selected("bullet.png", "source"),
            [1],
            _ => PresentationAssetImportExecutionResult.NotApplied("No active paragraph"));

        var result = await workflow.ImportAsync(PresentationAssetImportKind.PictureBullet);

        result.Status.Should().Be(PresentationAssetImportStatus.NotApplied);
        result.Message.Should().Be("No active paragraph");
    }

    [Fact]
    public void ExecutionPort_AppliesEditorOwnedPictureOleAndTransitionImports()
    {
        var editor = CreateEditor();
        var embeddedCallbackCount = 0;
        var port = new PresentationAssetImportExecutionPort(
            editor,
            new PresentationAssetImportExecutionCallbacks(
                EmbeddedObjectInserted: () => embeddedCallbackCount++));

        var picture = port.Execute(PresentationAssetImportWorkflow.CreatePayload(
            PresentationAssetImportKind.Picture,
            "photo.jpg",
            [1, 2, 3]));
        var embedded = port.Execute(PresentationAssetImportWorkflow.CreatePayload(
            PresentationAssetImportKind.EmbeddedObject,
            "sheet.xlsx",
            [4, 5, 6]));
        var transition = port.Execute(PresentationAssetImportWorkflow.CreatePayload(
            PresentationAssetImportKind.TransitionSound,
            "sound.wav",
            [7, 8, 9]));

        picture.Applied.Should().BeTrue();
        embedded.Applied.Should().BeTrue();
        transition.Applied.Should().BeTrue();
        editor.CurrentSlide!.Shapes.Should().Contain(shape => shape.Kind == SlideShapeKind.Picture);
        editor.CurrentSlide.Shapes.Should().Contain(shape => shape.Kind == SlideShapeKind.Ole);
        editor.CurrentSlideTransition.Sound!.ContentType.Should().Be("audio/wav");
        embeddedCallbackCount.Should().Be(1);
    }

    [Fact]
    public void ExecutionPort_DelegatesHostContextImportsWithoutOwningNativeState()
    {
        var editor = CreateEditor();
        PresentationPictureBulletPayload? observedBullet = null;
        byte[]? observedSmartArt = null;
        byte[]? observedZoom = null;
        var port = new PresentationAssetImportExecutionPort(
            editor,
            new PresentationAssetImportExecutionCallbacks(
                ApplyPictureBullet: payload =>
                {
                    observedBullet = payload;
                    return true;
                },
                ApplySmartArtPicture: (bytes, _) =>
                {
                    observedSmartArt = bytes;
                    return true;
                },
                ApplyZoomCoverImage: (bytes, _) =>
                {
                    observedZoom = bytes;
                    return true;
                }));

        port.Execute(PresentationAssetImportWorkflow.CreatePayload(
            PresentationAssetImportKind.PictureBullet,
            "bullet.png",
            [1])).Applied.Should().BeTrue();
        port.Execute(PresentationAssetImportWorkflow.CreatePayload(
            PresentationAssetImportKind.SmartArtPicture,
            "node.png",
            [2])).Applied.Should().BeTrue();
        port.Execute(PresentationAssetImportWorkflow.CreatePayload(
            PresentationAssetImportKind.ZoomCoverImage,
            "cover.png",
            [3])).Applied.Should().BeTrue();

        observedBullet.Should().NotBeNull();
        observedSmartArt.Should().NotBeNull();
        observedZoom.Should().NotBeNull();
        observedBullet!.ImageBytes.Should().Equal(1);
        observedSmartArt!.Should().Equal(2);
        observedZoom!.Should().Equal(3);
    }

    private static EditingSession CreateEditor()
    {
        var presentation = Presentation.CreateEmpty();
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    private static PresentationAssetImportWorkflow CreateWorkflow(
        PresentationAssetPickerResult pickerResult,
        byte[] bytes,
        Func<PresentationAssetImportPayload, PresentationAssetImportExecutionResult> execute) =>
        new(
            new StubPicker(pickerResult),
            new StubReader { Bytes = bytes },
            new StubExecution(execute));

    private sealed class StubPicker(PresentationAssetPickerResult result) : IPresentationAssetPickerPort
    {
        public Task<PresentationAssetPickerResult> PickAsync(
            PresentationAssetImportRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class StubReader : IPresentationAssetReaderPort
    {
        public byte[] Bytes { get; init; } = [];
        public Exception? Exception { get; init; }
        public int CallCount { get; private set; }

        public Task<byte[]> ReadAsync(
            PresentationAssetSelection selection,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var exception = Exception;
            return exception is null
                ? Task.FromResult(Bytes)
                : Task.FromException<byte[]>(exception);
        }
    }

    private sealed class StubExecution(
        Func<PresentationAssetImportPayload, PresentationAssetImportExecutionResult> execute)
        : IPresentationAssetImportExecutionPort
    {
        public int CallCount { get; private set; }

        public PresentationAssetImportExecutionResult Execute(PresentationAssetImportPayload payload)
        {
            CallCount++;
            return execute(payload);
        }
    }
}
