namespace Free.Shared.AppServices.Tests;

public sealed class PlatformClipboardContractTests
{
    [Theory]
    [InlineData(PlatformClipboardReadStatus.Unavailable)]
    [InlineData(PlatformClipboardReadStatus.Empty)]
    [InlineData(PlatformClipboardReadStatus.Unsupported)]
    [InlineData(PlatformClipboardReadStatus.Failed)]
    public async Task TypedTextRead_PreservesNonSuccessStatus(PlatformClipboardReadStatus status)
    {
        var clipboard = new FakeClipboard
        {
            ReadResult = new PlatformClipboardReadResult<PlatformClipboardContent>(status),
        };

        var result = await clipboard.ReadTextAsync();

        result.Status.Should().Be(status);
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task TypedReads_ProjectRendererNeutralPayloads()
    {
        var clipboard = new FakeClipboard
        {
            ReadResult = PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                new PlatformClipboardContent(
                    Text: "hello",
                    FilePaths: ["C:\\one.txt"],
                    Image: new PlatformClipboardImage([1, 2, 3], 8, 6),
                    CustomData:
                    [
                        PlatformClipboardData.FromText("text/html", "<b>hello</b>"),
                        PlatformClipboardData.FromBytes("product/selection", [4, 5]),
                    ])),
        };

        (await clipboard.ReadTextAsync()).Value.Should().Be("hello");
        (await clipboard.ReadFilesAsync()).Value.Should().Equal("C:\\one.txt");
        (await clipboard.ReadImageAsync()).Value.Should().BeEquivalentTo(
            new PlatformClipboardImage([1, 2, 3], 8, 6));
        (await clipboard.ReadCustomAsync(new PlatformClipboardFormat(
            "product/selection",
            PlatformClipboardDataKind.Bytes))).Value!.Bytes.Should().Equal(4, 5);
    }

    [Fact]
    public async Task Write_ForwardsAllPayloadFlavorsWithoutToolkitObjects()
    {
        var clipboard = new FakeClipboard();
        var content = new PlatformClipboardContent(
            Text: "text",
            FilePaths: ["C:\\one.txt", "C:\\two.png"],
            Image: new PlatformClipboardImage([9, 8], 10, 20),
            CustomData:
            [
                PlatformClipboardData.FromText("HTML Format", "fragment"),
                PlatformClipboardData.FromBytes("application/custom", [7, 6]),
            ]);

        var result = await clipboard.WriteAsync(content);

        result.Status.Should().Be(PlatformClipboardWriteStatus.Success);
        clipboard.LastWrite.Should().BeSameAs(content);
    }

    [Fact]
    public void BytePayloads_AreSnapshottedAtConstruction()
    {
        byte[] source = [1, 2, 3];

        var data = PlatformClipboardData.FromBytes("application/custom", source);
        source[0] = 9;

        data.Bytes.Should().Equal(1, 2, 3);
    }

    private sealed class FakeClipboard : IPlatformClipboard
    {
        public PlatformClipboardReadResult<PlatformClipboardContent> ReadResult { get; set; } =
            PlatformClipboardReadResult<PlatformClipboardContent>.Empty();

        public PlatformClipboardContent? LastWrite { get; private set; }

        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ReadResult);

        public ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent content,
            CancellationToken cancellationToken = default)
        {
            LastWrite = content;
            return ValueTask.FromResult(PlatformClipboardWriteResult.Success());
        }

        public ValueTask<PlatformClipboardWriteResult> ClearAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());
    }
}
