using Free.Shared.AppServices;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationAssetImportHostSessionTests
{
    [Fact]
    public async Task ImportAsync_ComposesNativePortsAndPerRequestZoomCallback()
    {
        var editor = CreateEditor();
        byte[]? observed = null;
        var session = new PresentationAssetImportHostSession(
            new StubPicker(PresentationAssetPickerResult.Selected("cover.png", "native-source")),
            new StubReader([1, 2, 3]),
            editor);

        var result = await session.ImportAsync(
            PresentationAssetImportKind.ZoomCoverImage,
            (bytes, contentType) =>
            {
                observed = bytes;
                contentType.Should().Be("image/png");
                return true;
            });

        result.Status.Should().Be(PresentationAssetImportStatus.Succeeded);
        observed.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task MaterializeOutcomeAsync_RoutesStatusAndModalFeedbackThroughHostPorts()
    {
        var session = new PresentationAssetImportHostSession(
            new StubPicker(PresentationAssetPickerResult.Cancelled),
            new StubReader([]),
            CreateEditor());
        var defaultStatuses = new List<string>();
        var targetedStatuses = new List<string>();
        var messages = new RecordingMessageService();
        var request = PresentationAssetImportRequest.Create(PresentationAssetImportKind.Picture);

        var success = await session.MaterializeOutcomeAsync(
            new PresentationAssetImportResult(
                request,
                PresentationAssetImportStatus.Succeeded,
                SourceName: "photo.png"),
            PresentationFileTextResources.Presentation,
            new PresentationAssetImportOutcomePolicy(ShowInsertedStatus: true),
            defaultStatuses.Add,
            messages);
        var targeted = await session.MaterializeOutcomeAsync(
            new PresentationAssetImportResult(
                request,
                PresentationAssetImportStatus.Succeeded,
                SourceName: "target.png"),
            PresentationFileTextResources.Presentation,
            new PresentationAssetImportOutcomePolicy(ShowInsertedStatus: true),
            defaultStatuses.Add,
            messages,
            targetedStatuses.Add);
        var failure = await session.MaterializeOutcomeAsync(
            new PresentationAssetImportResult(
                request,
                PresentationAssetImportStatus.Failed,
                Message: "decode failed"),
            PresentationFileTextResources.Presentation,
            PresentationAssetImportOutcomePolicy.ModalError,
            defaultStatuses.Add,
            messages);

        success.StatusText.Should().Be(defaultStatuses.Single());
        targeted.StatusText.Should().Be(targetedStatuses.Single());
        defaultStatuses.Should().HaveCount(1);
        failure.Message.Should().NotBeNull();
        messages.Requests.Should().ContainSingle()
            .Which.Should().Be(failure.Message);
    }

    [Fact]
    public async Task NativePortAdapters_ForwardPickerAndReadWhileOwningTypedSourceLifetime()
    {
        var request = PresentationAssetImportRequest.Create(PresentationAssetImportKind.Picture);
        var nativeSource = new DisposableAssetSource();
        var picker = new PresentationAssetPickerAdapter((observed, cancellationToken) =>
        {
            observed.Should().BeSameAs(request);
            cancellationToken.Should().Be(CancellationToken.None);
            return Task.FromResult(PresentationAssetPickerResult.Selected("asset.png", nativeSource));
        });
        var reader = new PresentationAssetReaderAdapter<DisposableAssetSource>(
            static (_, _) => Task.FromResult<byte[]>([4, 5, 6]),
            static source => source.Dispose());

        var picked = await picker.PickAsync(request, CancellationToken.None);
        var bytes = await reader.ReadAsync(picked.Selection!, CancellationToken.None);

        bytes.Should().Equal(4, 5, 6);
        nativeSource.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task NativeReaderAdapter_RejectsMismatchedSourcesWithoutInvokingReader()
    {
        var invoked = false;
        var reader = new PresentationAssetReaderAdapter<DisposableAssetSource>(
            (_, _) =>
            {
                invoked = true;
                return Task.FromResult<byte[]>([]);
            },
            invalidSourceMessage: _ => "invalid native source");

        var act = () => reader.ReadAsync(
            new PresentationAssetSelection("asset.png", "wrong"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("invalid native source");
        invoked.Should().BeFalse();
    }

    private static EditingSession CreateEditor()
    {
        var presentation = Presentation.CreateEmpty();
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    private sealed class StubPicker(PresentationAssetPickerResult result) : IPresentationAssetPickerPort
    {
        public Task<PresentationAssetPickerResult> PickAsync(
            PresentationAssetImportRequest request,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class StubReader(byte[] bytes) : IPresentationAssetReaderPort
    {
        public Task<byte[]> ReadAsync(
            PresentationAssetSelection selection,
            CancellationToken cancellationToken) => Task.FromResult(bytes);
    }

    private sealed class DisposableAssetSource : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class RecordingMessageService : IUserMessageService
    {
        public List<UserMessageRequest> Requests { get; } = [];

        public ValueTask<UserMessageResult> ShowMessageAsync(
            UserMessageRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(UserMessageResult.Ok);
        }
    }
}
