using Free.Shared.AppServices;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentFragments;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests.DocumentFragments;

public sealed class FreeWDocumentFragmentImportWorkflowTests
{
    [Fact]
    public void PickerPlans_AreSharedAcrossRenderersAndSupportDocxAndPlainText()
    {
        var text = FreeWDocumentFragmentImportPlanner.CreateTextFromFileRequest();
        var embeddedObject = FreeWDocumentFragmentImportPlanner.CreateEmbeddedObjectRequest();

        text.PickerPlan.Title.Should().Be("Insert Text from File");
        text.PickerPlan.DefaultExtensionWithDot.Should().Be(".docx");
        text.PickerPlan.FileTypes.Should().ContainSingle();
        text.PickerPlan.FileTypes[0].DisplayName.Should().Be(
            FreeWFileTextResources.TextFromFileTypeName);
        text.PickerPlan.FileTypes[0].Patterns.Should().Equal("*.docx", "*.txt");
        text.PickerPlan.FileTypes[0].MimeTypes.Should().Equal(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "text/plain");
        text.PickerPlan.BuildWpfFilter().Should().Be(
            $"{FreeWFileTextResources.TextFromFileTypeName}|*.docx;*.txt");

        embeddedObject.PickerPlan.BuildWpfFilter().Should().Be("All files (*.*)|*.*");
        embeddedObject.PickerPlan.FileTypes.Should().ContainSingle()
            .Which.DisplayName.Should().Be("All files (*.*)");
    }

    [Fact]
    public async Task DocumentImport_ResolvesAdapterAndLinkedImagePreviews()
    {
        var ports = new FakePorts("fragment.docx", [1, 2, 3]);
        var adapter = new FakeDocumentAdapter(".docx");
        var workflow = ports.CreateWorkflow([adapter]);

        var result = await workflow.ImportAsync(
            FreeWDocumentFragmentImportPlanner.CreateTextFromFileRequest());

        result.Status.Should().Be(FreeWDocumentFragmentImportStatus.Succeeded);
        result.SourceExtension.Should().Be(".docx");
        result.Insertion!.Kind.Should().Be(FreeWDocumentFragmentInsertionKind.Document);
        adapter.LoadedBytes.Should().Equal(1, 2, 3);
        ports.ReadTextCalls.Should().Be(0);
        ports.ResolveLinkedImagePreviewsCalls.Should().Be(1);
    }

    [Fact]
    public async Task PlainTextImport_PreservesPlainTextInsertionPath()
    {
        var ports = new FakePorts("notes.TXT", [1, 2, 3]) { Text = "plain text" };
        var adapter = new FakeDocumentAdapter(".docx");

        var result = await ports.CreateWorkflow([adapter]).ImportAsync(
            FreeWDocumentFragmentImportPlanner.CreateTextFromFileRequest());

        result.Status.Should().Be(FreeWDocumentFragmentImportStatus.Succeeded);
        result.Insertion.Should().BeEquivalentTo(
            FreeWDocumentFragmentInsertionRequest.ForPlainText("plain text"));
        ports.ReadTextCalls.Should().Be(1);
        ports.ReadBytesCalls.Should().Be(0);
        adapter.LoadCalls.Should().Be(0);
    }

    [Fact]
    public async Task DocumentImport_ResolvesDocumentAdapterByExtension()
    {
        var ports = new FakePorts("fragment.docx", [4, 5, 6]);
        var adapter = new FakeDocumentAdapter(".docx");

        var result = await ports.CreateWorkflow([adapter]).ImportAsync(
            FreeWDocumentFragmentImportPlanner.CreateTextFromFileRequest());

        result.Status.Should().Be(FreeWDocumentFragmentImportStatus.Succeeded);
        result.Insertion!.Kind.Should().Be(FreeWDocumentFragmentInsertionKind.Document);
        adapter.LoadedBytes.Should().Equal(4, 5, 6);
        ports.ResolveLinkedImagePreviewsCalls.Should().Be(1);
    }

    [Fact]
    public async Task TextImport_ReportsUnsupportedFormatWithoutReadingOrInsertion()
    {
        var ports = new FakePorts("fragment.rtf", [4, 5, 6]);

        var result = await ports.CreateWorkflow([new FakeDocumentAdapter(".docx")]).ImportAsync(
            FreeWDocumentFragmentImportPlanner.CreateTextFromFileRequest());

        result.Status.Should().Be(FreeWDocumentFragmentImportStatus.UnsupportedFormat);
        result.SourceExtension.Should().Be(".rtf");
        ports.ReadBytesCalls.Should().Be(0);
        ports.InsertCalls.Should().Be(0);
    }

    [Fact]
    public async Task EmbeddedObjectImport_BuildsPortableOlePackageInsertion()
    {
        var ports = new FakePorts("budget.xlsx", [7, 8, 9]);

        var result = await ports.CreateWorkflow([]).ImportAsync(
            FreeWDocumentFragmentImportPlanner.CreateEmbeddedObjectRequest());

        result.Status.Should().Be(FreeWDocumentFragmentImportStatus.Succeeded);
        result.Insertion!.Kind.Should().Be(FreeWDocumentFragmentInsertionKind.EmbeddedObject);
        result.Insertion.EmbeddedObject!.ProgId.Should().Be(OlePackagePayloadBuilder.ProgId);
        result.Insertion.EmbeddedObject.Payload.Should().NotBeEmpty();
        ports.ReadBytesCalls.Should().Be(1);
    }

    [Fact]
    public async Task CancellationAndNotApplied_AreTypedAndDoNotBecomeFailures()
    {
        var cancelledPorts = new FakePorts("fragment.docx", [1])
        {
            PickerResult = FreeWDocumentFragmentPickerResult.Cancelled,
        };
        var notAppliedPorts = new FakePorts("fragment.docx", [1])
        {
            InsertionResult = FreeWDocumentFragmentInsertionResult.NotApplied("no caret"),
        };
        var request = FreeWDocumentFragmentImportPlanner.CreateTextFromFileRequest();

        var cancelled = await cancelledPorts.CreateWorkflow([new FakeDocumentAdapter(".docx")])
            .ImportAsync(request);
        var notApplied = await notAppliedPorts.CreateWorkflow([new FakeDocumentAdapter(".docx")])
            .ImportAsync(request);

        cancelled.Status.Should().Be(FreeWDocumentFragmentImportStatus.Cancelled);
        cancelledPorts.ReadBytesCalls.Should().Be(0);
        notApplied.Status.Should().Be(FreeWDocumentFragmentImportStatus.NotApplied);
        notApplied.Message.Should().Be("no caret");
    }

    [Fact]
    public async Task FailuresAndUnsupportedFormats_PreserveExactHostFeedback()
    {
        var ports = new FakePorts("fragment.docx", [1])
        {
            ReadException = new IOException("broken file"),
        };
        var request = FreeWDocumentFragmentImportPlanner.CreateTextFromFileRequest();
        var failed = await ports.CreateWorkflow([new FakeDocumentAdapter(".docx")])
            .ImportAsync(request);
        var unsupported = new FreeWDocumentFragmentImportResult(
            FreeWDocumentFragmentImportPlanner.CreateTextFromFileRequest(),
            FreeWDocumentFragmentImportStatus.UnsupportedFormat,
            SourceExtension: ".rtf");
        var objectFailed = new FreeWDocumentFragmentImportResult(
            FreeWDocumentFragmentImportPlanner.CreateEmbeddedObjectRequest(),
            FreeWDocumentFragmentImportStatus.Failed,
            Message: "denied");

        var wpf = FreeWDocumentFragmentImportOutcomePlanner.Plan(
            failed,
            FreeWFileTextResources.Document,
            FreeWDocumentFragmentImportFailureSurface.WpfModalError);
        var avaloniaText = FreeWDocumentFragmentImportOutcomePlanner.Plan(
            unsupported,
            FreeWFileTextResources.Document,
            FreeWDocumentFragmentImportFailureSurface.AvaloniaStatus);
        var avaloniaObject = FreeWDocumentFragmentImportOutcomePlanner.Plan(
            objectFailed,
            FreeWFileTextResources.Document,
            FreeWDocumentFragmentImportFailureSurface.AvaloniaStatus);

        failed.Status.Should().Be(FreeWDocumentFragmentImportStatus.Failed);
        wpf.ModalTitle.Should().Be("FreeW");
        wpf.ModalMessage.Should().Be("Could not insert the file:\nbroken file");
        avaloniaText.StatusText.Should().Be(
            SisterAppFileTextPlanner.FormatUnsupportedFileType(
                FreeWFileTextResources.Document,
                FreeWFileTextResources.InsertTextCommand,
                ".rtf"));
        avaloniaObject.StatusText.Should().Be("Could not insert the object: denied");
    }

    [Fact]
    public void Ownership_PortableWorkflowOwnsPolicyAndHostsOnlyRealizeNativeBoundaries()
    {
        var workflow = ReadSource(
            "freew", "FreeW.App.Presentation", "DocumentFragments",
            "FreeWDocumentFragmentImportWorkflow.cs");
        var wpfCommands = ReadSource(
            "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var wpfPorts = ReadSource(
            "freew", "FreeW.App.Host", "DocumentFragments", "WpfDocumentFragmentImportPorts.cs");
        var avaloniaWindow = ReadSource(
            "freew", "FreeW.App.Avalonia", "MainWindow.cs");
        var avaloniaPorts = ReadSource(
            "freew", "FreeW.App.Avalonia", "DocumentFragments",
            "AvaloniaDocumentFragmentImportPorts.cs");

        workflow.Should().Contain("DocumentFileFormatResolver.FindOpenAdapter(");
        workflow.Should().Contain("OlePackagePayloadBuilder.Create(");
        workflow.Should().NotContain("FreeWDocumentFragmentHostProfile");
        workflow.Should().NotContain("FreeWTextImportPolicy");
        workflow.Should().NotContain("using System.Windows");
        workflow.Should().NotContain("using Avalonia");
        workflow.Should().NotContain("File.ReadAll");

        wpfCommands.Should().Contain("new FreeWDocumentFragmentImportWorkflow(");
        wpfCommands.Should().Contain("CreateTextFromFileRequest()");
        wpfCommands.Should().Contain("CreateEmbeddedObjectRequest()");
        wpfCommands.Should().NotContain("Word Documents (*.docx)|*.docx");
        wpfCommands.Should().NotContain("OlePackagePayloadBuilder.Create(");
        wpfCommands.Should().NotContain("DocxReader.Read(result.FileName");
        wpfPorts.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
        wpfPorts.Should().Contain("FileByteReadWorkflow.ReadLocalPathBytesAsync(");
        wpfPorts.Should().NotContain("File.ReadAllBytes(");
        wpfPorts.Should().Contain("LinkedImagePreviewResolver.ResolveLocalPreviews(");
        wpfPorts.Should().Contain("editor.InsertDocument(");

        avaloniaWindow.Should().Contain("new FreeWDocumentFragmentImportWorkflow(");
        avaloniaWindow.Should().Contain("CreateTextFromFileRequest()");
        avaloniaWindow.Should().Contain("CreateEmbeddedObjectRequest()");
        avaloniaWindow.Should().NotContain("TextFromFileType");
        avaloniaWindow.Should().NotContain("EmbeddedObjectFileType");
        avaloniaWindow.Should().NotContain("OlePackagePayloadBuilder.Create(");
        avaloniaPorts.Should().Contain("AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(");
        avaloniaPorts.Should().Contain("FileByteReadWorkflow.ReadLocalPathBytesAsync(");
        avaloniaPorts.Should().NotContain("File.ReadAllBytesAsync(");
        avaloniaPorts.Should().Contain("File.ReadAllTextAsync(");
        avaloniaPorts.Should().Contain("editor.InsertQuickPartText(");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }

    private sealed class FakePorts :
        IFreeWDocumentFragmentPickerPort,
        IFreeWDocumentFragmentSourceReaderPort,
        IFreeWDocumentFragmentInsertionPort
    {
        private readonly byte[] _bytes;

        public FakePorts(string sourceName, byte[] bytes)
        {
            _bytes = bytes;
            PickerResult = FreeWDocumentFragmentPickerResult.Selected(
                sourceName,
                $"C:\\imports\\{sourceName}",
                new object());
        }

        public FreeWDocumentFragmentPickerResult PickerResult { get; set; }
        public FreeWDocumentFragmentInsertionResult InsertionResult { get; set; } =
            FreeWDocumentFragmentInsertionResult.Success;
        public string Text { get; set; } = string.Empty;
        public Exception? ReadException { get; set; }
        public int ReadBytesCalls { get; private set; }
        public int ReadTextCalls { get; private set; }
        public int ResolveLinkedImagePreviewsCalls { get; private set; }
        public int InsertCalls { get; private set; }

        public FreeWDocumentFragmentImportWorkflow CreateWorkflow(
            IEnumerable<IDocumentFileAdapter> adapters) =>
            new(adapters, this, this, this);

        public Task<FreeWDocumentFragmentPickerResult> PickAsync(
            FreeWDocumentFragmentImportRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(PickerResult);

        public Task<byte[]> ReadBytesAsync(
            FreeWDocumentFragmentImportSelection selection,
            CancellationToken cancellationToken)
        {
            ReadBytesCalls++;
            if (ReadException is not null)
                throw ReadException;
            return Task.FromResult(_bytes);
        }

        public Task<string> ReadTextAsync(
            FreeWDocumentFragmentImportSelection selection,
            CancellationToken cancellationToken)
        {
            ReadTextCalls++;
            if (ReadException is not null)
                throw ReadException;
            return Task.FromResult(Text);
        }

        public void ResolveLinkedImagePreviews(
            FreeWDocumentFragmentImportSelection selection,
            TextDocument document) =>
            ResolveLinkedImagePreviewsCalls++;

        public FreeWDocumentFragmentInsertionResult Insert(FreeWDocumentFragmentInsertionRequest request)
        {
            InsertCalls++;
            return InsertionResult;
        }
    }

    private sealed class FakeDocumentAdapter(string extension) : IDocumentFileAdapter
    {
        public string Extension => extension;
        public string FormatName => "Fake document";
        public int LoadCalls { get; private set; }
        public byte[]? LoadedBytes { get; private set; }

        public TextDocument Load(Stream stream)
        {
            LoadCalls++;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            LoadedBytes = copy.ToArray();
            return new TextDocument();
        }

        public void Save(TextDocument document, Stream stream) => throw new NotSupportedException();
    }
}
