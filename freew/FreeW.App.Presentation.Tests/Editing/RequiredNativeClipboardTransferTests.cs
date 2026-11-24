using Free.Shared.AppServices;
using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class RequiredNativeClipboardTransferTests
{
    [Fact]
    public async Task RequiredNativePayloadIsCheckedBeforeWritingFlattenedAtomicData()
    {
        var run = Run.PageBreak();
        var source = new TextDocument();
        var paragraph = new Paragraph { Runs = { run } };
        source.Blocks.Add(paragraph);
        var plan = DocumentDisplaySelectionPlanner.Plan([new(0, run, "PAGE", true)], 0, 4);
        var documents = FreeWDisplaySelectionClipboardBuilder.Build(source, paragraph, plan);
        var clipboard = new Clipboard();

        var cut = await FreeWClipboardApplicationWorkflow.WriteSelectionAsync(clipboard, plan.SelectedText,
            documents.RichDocument, documents.NativeDocument, requireNativeDocument: true);

        cut.Status.Should().Be(FreeWClipboardTransferStatus.Unsupported);
        cut.CanCommitCut.Should().BeFalse();
        clipboard.WriteCalls.Should().Be(0);
        var copy = await FreeWClipboardApplicationWorkflow.WriteSelectionAsync(clipboard, plan.SelectedText,
            documents.RichDocument, documents.NativeDocument);
        copy.IsSuccess.Should().BeTrue();
        clipboard.WriteCalls.Should().Be(1);
        clipboard.Written!.Text.Should().Be("PAGE");
    }

    [Fact]
    public async Task AProvidedNativeDocumentThatCannotSerializeCannotCommitACut()
    {
        // Deliberately malformed model data makes the writer throw deterministically.
        // Keep the separate selected text valid so this tests serialization refusal.
        var native = new TextDocument();
        native.Blocks.Add(new Paragraph { Runs = { null! } });
        var fallback = FreeWClipboardApplicationWorkflow.CreateWriteContent("copied", null, native)!;
        fallback.GetBytes(FreeWClipboardApplicationWorkflow.NativeDocumentFormat, PlatformClipboardFormatScope.Application)
            .Should().BeNull("the fixture must actually exercise the native serialization failure path");
        var clipboard = new Clipboard();

        var result = await FreeWClipboardApplicationWorkflow.WriteSelectionAsync(clipboard, "copied",
            nativeDocument: native, requireNativeDocument: true);

        result.Status.Should().Be(FreeWClipboardTransferStatus.Unsupported);
        result.CanCommitCut.Should().BeFalse();
        clipboard.WriteCalls.Should().Be(0);
    }

    private sealed class Clipboard : IPlatformClipboard
    {
        public int WriteCalls { get; private set; }
        public PlatformClipboardContent? Written { get; private set; }
        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardReadResult<PlatformClipboardContent>.Unavailable());
        public ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent content, CancellationToken cancellationToken = default)
        {
            Written = content;
            WriteCalls++;
            return ValueTask.FromResult(PlatformClipboardWriteResult.Success());
        }
        public ValueTask<PlatformClipboardWriteResult> ClearAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());
    }
}
