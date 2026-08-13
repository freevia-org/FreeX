using Free.Shared.AppServices;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentFragments;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.DocumentFragments;

public sealed class FreeWPictureImportWorkflowTests
{
    [Fact]
    public async Task DecoderPolicyReturnsFactsAndNormalizesDecoderFailures()
    {
        var facts = new FreeWPictureDecoderFacts(40, 20);

        (await FreeWPictureDecoderPolicy.DecodeOrUnavailable(
            CancellationToken.None,
            () => facts)).Should().BeSameAs(facts);
        (await FreeWPictureDecoderPolicy.DecodeOrUnavailable(
            CancellationToken.None,
            () => throw new InvalidDataException("invalid image")))
            .Should().BeSameAs(FreeWPictureDecoderFacts.Unavailable);
    }

    [Fact]
    public async Task DecoderPolicyHonorsCancellationBeforeNativeDecode()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var invoked = false;

        var action = async () => await FreeWPictureDecoderPolicy.DecodeOrUnavailable(
            cancellation.Token,
            () =>
            {
                invoked = true;
                return new FreeWPictureDecoderFacts(1, 1);
            });

        await action.Should().ThrowAsync<OperationCanceledException>();
        invoked.Should().BeFalse();
    }

    [Fact]
    public void PickerPlanConvergesRendererFormatsToUnionWithoutDroppingAllFiles()
    {
        var picker = FreeWPictureImportPlanner.CreateRequest().PickerPlan;

        picker.PictureFiles.Patterns.Should().Equal(
            "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.tif", "*.tiff", "*.svg");
        picker.PictureFiles.MimeTypes.Should().Equal(
            "image/png", "image/jpeg", "image/gif", "image/bmp", "image/tiff", "image/svg+xml");
        picker.FileTypes.SelectMany(type => type.Patterns).Should().Contain("*.*");
        picker.BuildWpfFilter().Should()
            .Contain("*.gif")
            .And.Contain("*.tiff")
            .And.Contain("*.svg")
            .And.EndWith("All files (*.*)|*.*");
    }

    [Theory]
    [InlineData("photo.png", FreeWPictureImportSourceKind.PreservedRaster)]
    [InlineData("photo.JPEG", FreeWPictureImportSourceKind.PreservedRaster)]
    [InlineData("photo.tiff", FreeWPictureImportSourceKind.PreservedRaster)]
    [InlineData("diagram.svg", FreeWPictureImportSourceKind.Svg)]
    [InlineData("photo.webp", FreeWPictureImportSourceKind.NativeRasterization)]
    public void SourceClassificationOwnsPreservationAndRasterizationPolicy(
        string sourceName,
        FreeWPictureImportSourceKind expected)
    {
        FreeWPictureImportPlanner.ClassifySource(sourceName).Should().Be(expected);
    }

    [Fact]
    public void SizingUsesCanonicalDisplayDpiCapsLongestEdgeAndPreservesAspect()
    {
        var plan = FreeWPictureImportPlanner.PlanSize(
            new FreeWPictureDecoderFacts(1200, 600, SourceDpiX: 300, SourceDpiY: 300));

        plan.WidthPt.Should().BeApproximately(400, 0.001);
        plan.HeightPt.Should().BeApproximately(200, 0.001);
        plan.EffectiveDpiX.Should().Be(96);
        plan.EffectiveDpiY.Should().Be(96);
        plan.WasScaled.Should().BeTrue();
        plan.UsedFallbackSize.Should().BeFalse();
        (plan.WidthPt / plan.HeightPt).Should().BeApproximately(2, 0.001);
    }

    [Fact]
    public void SizingUsesSharedFallbackWhenNativeDecoderHasNoFacts()
    {
        FreeWPictureImportPlanner.PlanSize(FreeWPictureDecoderFacts.Unavailable)
            .Should().Be(new FreeWPictureImportSizingPlan(
                200,
                150,
                96,
                96,
                UsedFallbackSize: true,
                WasScaled: false));
    }

    [Fact]
    public async Task PreservedRasterKeepsBytesFormatAndOriginalPixelDimensions()
    {
        var sourceBytes = new byte[] { 1, 2, 3 };
        var ports = new FakePorts("photo.gif", sourceBytes)
        {
            DecoderFacts = new FreeWPictureDecoderFacts(320, 160),
        };

        var result = await ports.CreateWorkflow().ImportAsync();

        result.Status.Should().Be(FreeWPictureImportStatus.Succeeded);
        result.Insertion.Should().NotBeNull();
        result.Insertion!.Bytes.Should().BeSameAs(sourceBytes);
        result.Insertion.Format.Should().Be(ImageFormat.Gif);
        result.Insertion.OriginalPixelWidth.Should().Be(320);
        result.Insertion.OriginalPixelHeight.Should().Be(160);
        result.Insertion.WidthPt.Should().BeApproximately(240, 0.001);
        result.Insertion.HeightPt.Should().BeApproximately(120, 0.001);
        ports.RasterizeCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("diagram.svg", FreeWPictureImportSourceKind.Svg)]
    [InlineData("photo.webp", FreeWPictureImportSourceKind.NativeRasterization)]
    public async Task NonPreservableSourcesUseNativeRasterizerOutcome(
        string sourceName,
        FreeWPictureImportSourceKind expectedKind)
    {
        var ports = new FakePorts(sourceName, [1, 2, 3])
        {
            Rasterization = new FreeWPictureRasterizationOutcome(
                [9, 8, 7],
                new FreeWPictureDecoderFacts(400, 200)),
        };

        var result = await ports.CreateWorkflow().ImportAsync();

        result.Status.Should().Be(FreeWPictureImportStatus.Succeeded);
        result.Insertion!.Bytes.Should().Equal(9, 8, 7);
        result.Insertion.Format.Should().Be(ImageFormat.Png);
        result.Insertion.WidthPt.Should().BeApproximately(300, 0.001);
        result.Insertion.HeightPt.Should().BeApproximately(150, 0.001);
        ports.LastRasterizationRequest!.SourceKind.Should().Be(expectedKind);
        ports.LastRasterizationRequest.MaximumPixelEdge.Should().Be(400);
        ports.DecodeCalls.Should().Be(0);
    }

    [Fact]
    public async Task CancellationStopsBeforeReaderDecoderRasterizerAndInsertion()
    {
        var ports = new FakePorts("photo.png", [1])
        {
            PickerResult = FreeWPictureImportPickerResult.Cancelled,
        };

        var result = await ports.CreateWorkflow().ImportAsync();

        result.Status.Should().Be(FreeWPictureImportStatus.Cancelled);
        ports.ReadCalls.Should().Be(0);
        ports.DecodeCalls.Should().Be(0);
        ports.RasterizeCalls.Should().Be(0);
        ports.InsertCalls.Should().Be(0);
    }

    [Fact]
    public async Task FailuresReturnTypedResultAndPreserveRendererFeedbackSurfaces()
    {
        var ports = new FakePorts("photo.png", [1])
        {
            ReadException = new IOException("disk failed"),
        };

        var result = await ports.CreateWorkflow().ImportAsync();
        var status = FreeWPictureImportOutcomePlanner.Plan(
            result,
            FreeWFileTextResources.Document,
            FreeWPictureImportFailureSurface.Status);
        var modal = FreeWPictureImportOutcomePlanner.Plan(
            result,
            FreeWFileTextResources.Document,
            FreeWPictureImportFailureSurface.ModalError);

        result.Status.Should().Be(FreeWPictureImportStatus.Failed);
        result.Exception.Should().BeOfType<IOException>();
        status.StatusText.Should().Contain("disk failed");
        modal.ModalTitle.Should().Be("FreeW");
        modal.ModalMessage.Should().Be("Could not insert the image:\ndisk failed");
    }

    [Fact]
    public void PortableWorkflowOwnsPolicyWhileRenderersOwnNativeRealization()
    {
        var workflow = ReadSource(
            "freew", "FreeW.App.Presentation", "DocumentFragments", "FreeWPictureImportWorkflow.cs");
        var wpfCommands = ReadSource(
            "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var wpfPorts = ReadSource(
            "freew", "FreeW.App.Host", "PictureImport", "WpfPictureImportPorts.cs");
        var avaloniaWindow = ReadSource(
            "freew", "FreeW.App.Avalonia", "MainWindow.cs");
        var avaloniaPorts = ReadSource(
            "freew", "FreeW.App.Avalonia", "PictureImport", "AvaloniaPictureImportPorts.cs");

        workflow.Should().Contain("public sealed class FreeWPictureImportWorkflow");
        workflow.Should().Contain("FreeWPictureImportPlanner.PlanSize(");
        workflow.Should().NotContain("System.Windows");
        workflow.Should().NotContain("Avalonia");
        workflow.Should().NotContain("BitmapImage");

        wpfCommands.Should().Contain("new FreeWPictureImportWorkflow(");
        wpfCommands.Should().NotContain("Images (*.png;*.jpg;*.jpeg;*.svg)");
        wpfCommands.Should().NotContain("LoadAsInlineImage(");
        avaloniaWindow.Should().Contain("new FreeWPictureImportWorkflow(");
        avaloniaWindow.Should().NotContain("MeasureImagePoints(");
        avaloniaWindow.Should().NotContain("ImageFileType");

        wpfPorts.Should().Contain("BitmapFrame.Create(");
        wpfPorts.Should().Contain("SvgRasterizerHelper.RasterizeToInlineImage(");
        avaloniaPorts.Should().Contain("new Bitmap(source)");
        avaloniaPorts.Should().Contain("SvgIconRasterizer.LoadFileToPaintedBounds(");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }

    private sealed class FakePorts :
        IFreeWPictureImportPickerPort,
        IFreeWPictureImportSourceReaderPort,
        IFreeWPictureDecoderPort,
        IFreeWPictureRasterizerPort,
        IFreeWPictureInsertionPort
    {
        private readonly byte[] _sourceBytes;

        public FakePorts(string sourceName, byte[] sourceBytes)
        {
            _sourceBytes = sourceBytes;
            PickerResult = FreeWPictureImportPickerResult.Selected(sourceName, new object());
        }

        public FreeWPictureImportPickerResult PickerResult { get; set; }
        public Exception? ReadException { get; set; }
        public FreeWPictureDecoderFacts DecoderFacts { get; set; } = new(100, 50);
        public FreeWPictureRasterizationOutcome Rasterization { get; set; } =
            new([4, 5, 6], new FreeWPictureDecoderFacts(100, 50));
        public FreeWPictureRasterizationRequest? LastRasterizationRequest { get; private set; }
        public int ReadCalls { get; private set; }
        public int DecodeCalls { get; private set; }
        public int RasterizeCalls { get; private set; }
        public int InsertCalls { get; private set; }

        public FreeWPictureImportWorkflow CreateWorkflow() => new(this, this, this, this, this);

        public Task<FreeWPictureImportPickerResult> PickAsync(
            FreeWPictureImportRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(PickerResult);

        public Task<byte[]> ReadAsync(
            FreeWPictureImportSelection selection,
            CancellationToken cancellationToken)
        {
            ReadCalls++;
            if (ReadException is not null)
                throw ReadException;
            return Task.FromResult(_sourceBytes);
        }

        public ValueTask<FreeWPictureDecoderFacts> DecodeAsync(
            FreeWPictureImportSelection selection,
            byte[] bytes,
            CancellationToken cancellationToken)
        {
            DecodeCalls++;
            return ValueTask.FromResult(DecoderFacts);
        }

        public ValueTask<FreeWPictureRasterizationOutcome> RasterizeAsync(
            FreeWPictureRasterizationRequest request,
            CancellationToken cancellationToken)
        {
            RasterizeCalls++;
            LastRasterizationRequest = request;
            return ValueTask.FromResult(Rasterization);
        }

        public FreeWPictureInsertionResult Insert(FreeWPictureInsertionRequest request)
        {
            InsertCalls++;
            return FreeWPictureInsertionResult.Success;
        }
    }
}
