using Free.Shared.AppServices;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWClipboardApplicationWorkflowTests
{
    [Fact]
    public async Task ReadPasteSpecialAsync_ProjectsTextAndParsedRtfInOneTransfer()
    {
        const string rtf = @"{\rtf1\ansi\b Bold\b0  plain}";
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                new PlatformClipboardContent(
                    Text: "plain",
                    CustomData: [PlatformClipboardData.FromText(
                        FreeWClipboardApplicationWorkflow.RichTextFormat,
                        rtf)])),
        };

        var result = await FreeWClipboardApplicationWorkflow.ReadPasteSpecialAsync(clipboard);

        result.IsSuccess.Should().BeTrue();
        result.Payload!.Text.Should().Be("plain");
        result.Payload.RichDocument.Should().NotBeNull();
        clipboard.LastReadRequest.Should().BeSameAs(
            FreeWClipboardApplicationWorkflow.PasteSpecialReadRequest);
    }

    [Fact]
    public async Task ReadPasteSpecialAsync_FallsBackToHtmlTableWhenNoRtfIsOnTheClipboard()
    {
        // R143 clip-1: FreeX never places "Rich Text Format" on the clipboard for a cell-range copy --
        // it places plain text plus a CF_HTML table fragment (matching FreeX.Core.Commands
        // ClipboardHtmlSerializer.Serialize/WrapAsCfHtml). Model that payload here (full CF_HTML header
        // + <html> wrapper, exactly what the WPF host's "HTML Format" carries) and confirm the rich
        // paste path recovers a formatted document from it instead of degrading to plain text.
        const string cfHtml =
            "Version:0.9\r\n" +
            "StartHTML:0000000097\r\n" +
            "EndHTML:0000000350\r\n" +
            "StartFragment:0000000133\r\n" +
            "EndFragment:0000000307\r\n" +
            "<html><head><meta charset=\"utf-8\"></head><body>\r\n<!--StartFragment-->" +
            "<table border=\"1\" cellspacing=\"0\" style=\"border-collapse:collapse\">" +
            "<tr><td style=\"font-weight:bold\">Name</td><td>Score</td></tr>" +
            "<tr><td>Ann</td><td>92</td></tr>" +
            "</table>" +
            "<!--EndFragment-->\r\n</body></html>";
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                new PlatformClipboardContent(
                    Text: "Name\tScore\r\nAnn\t92",
                    CustomData: [PlatformClipboardData.FromText("HTML Format", cfHtml)])),
        };

        var result = await FreeWClipboardApplicationWorkflow.ReadPasteSpecialAsync(clipboard);

        result.IsSuccess.Should().BeTrue();
        result.Payload!.RichDocument.Should().NotBeNull();
        result.Payload.RichDocument!.PlainText.Should().Contain("Name").And.Contain("Ann");

        var plan = FreeWClipboardApplicationWorkflow.PlanPaste(
            result.Payload,
            PasteSpecialOption.KeepSourceFormatting);
        plan.PreferRichDocument.Should().BeTrue("a FreeX cell-range copy's formatting must survive Keep Source Formatting paste into FreeW");
    }

    [Fact]
    public async Task ReadTextAsync_ClassifiesEmptyAndFailureWithoutRendererExceptions()
    {
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Empty(),
        };

        var empty = await FreeWClipboardApplicationWorkflow.ReadTextAsync(clipboard);
        empty.Status.Should().Be(FreeWClipboardTransferStatus.Empty);
        empty.FeedbackMessage.Should().Be(FreeWClipboardApplicationWorkflow.EmptyClipboardMessage);

        clipboard.ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Failed("busy");
        var failed = await FreeWClipboardApplicationWorkflow.ReadTextAsync(clipboard);
        failed.Status.Should().Be(FreeWClipboardTransferStatus.Failed);
        failed.FeedbackMessage.Should().Contain("busy");
    }

    [Fact]
    public async Task WriteSelectionAsync_PreservesCutCommitPolicyAndPayloadCreation()
    {
        var clipboard = new FakeClipboard
        {
            WriteResult = PlatformClipboardWriteResult.Unavailable(),
        };

        var unavailable = await FreeWClipboardApplicationWorkflow.WriteSelectionAsync(clipboard, "selected");
        unavailable.CanCommitCut.Should().BeTrue();
        clipboard.LastWrittenContent!.Text.Should().Be("selected");

        var empty = await FreeWClipboardApplicationWorkflow.WriteSelectionAsync(clipboard, string.Empty);
        empty.CanCommitCut.Should().BeFalse();
        empty.Status.Should().Be(FreeWClipboardTransferStatus.Empty);
    }

    [Theory]
    [InlineData(PasteSpecialOption.KeepTextOnly, DocumentPasteTextKind.TextOnly, false)]
    [InlineData(PasteSpecialOption.MergeFormatting, DocumentPasteTextKind.MergeFormatting, false)]
    [InlineData(PasteSpecialOption.KeepSourceFormatting, DocumentPasteTextKind.MergeFormatting, true)]
    public void PlanPaste_OwnsPasteSpecialFallbackPolicy(
        PasteSpecialOption option,
        DocumentPasteTextKind expectedKind,
        bool expectsRichDocument)
    {
        var document = TextDocument.CreateEmpty();
        var plan = FreeWClipboardApplicationWorkflow.PlanPaste(
            new FreeWClipboardPayload("text", document),
            option);

        plan.TextKind.Should().Be(expectedKind);
        plan.PreferRichDocument.Should().Be(expectsRichDocument);
    }

    private sealed class FakeClipboard : IPlatformClipboard
    {
        public PlatformClipboardReadResult<PlatformClipboardContent> ReadResult { get; set; } =
            PlatformClipboardReadResult<PlatformClipboardContent>.Empty();

        public PlatformClipboardWriteResult WriteResult { get; set; } =
            PlatformClipboardWriteResult.Success();

        public PlatformClipboardReadRequest? LastReadRequest { get; private set; }

        public PlatformClipboardContent? LastWrittenContent { get; private set; }

        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request,
            CancellationToken cancellationToken = default)
        {
            LastReadRequest = request;
            return ValueTask.FromResult(ReadResult);
        }

        public ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent content,
            CancellationToken cancellationToken = default)
        {
            LastWrittenContent = content;
            return ValueTask.FromResult(WriteResult);
        }

        public ValueTask<PlatformClipboardWriteResult> ClearAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());
    }
}
