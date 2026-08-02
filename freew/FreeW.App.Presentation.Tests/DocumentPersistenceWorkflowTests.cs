using Free.Shared.IO;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentPersistenceWorkflowTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeW.DocumentPersistenceWorkflowTests", Guid.NewGuid().ToString("N"));

    public DocumentPersistenceWorkflowTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Open_TemplateFormat_ReturnsLoadedDocumentWithoutSavedPath()
    {
        var adapter = new FakeDocumentAdapter(
            [new FileFormatDescriptor(".dotx", "Word Template", OpensAsTemplate: true)]);
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        var path = WriteText("Template.dotx", "Seed copy");

        var result = workflow.Open(path);

        result.Document.PlainText.Should().Be("Seed copy");
        result.SavedPath.Should().BeNull();
        result.OpenedAsTemplate.Should().BeTrue();
        result.Format!.OpensAsTemplate.Should().BeTrue();
    }

    [Fact]
    public void Open_PathAwareWorkflowResolvesLocalLinkedImagePreview()
    {
        var preview = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 };
        File.WriteAllBytes(Path.Combine(_tempDir, "linked.png"), preview);
        var source = Document("Body");
        source.Paragraphs.Single().Runs.Add(Run.FromImage(new InlineImage([], 24, 18)
        {
            LinkedImageTarget = "linked.png"
        }));
        var adapter = new FakeDocumentAdapter(
            [new FileFormatDescriptor(".docx", "Word Document")])
        {
            LoadAction = _ => source
        };
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        var path = WriteText("Linked.docx", "stub");

        var result = workflow.Open(path);

        result.Document.Paragraphs.Single().Runs.Single(run => run.Image is not null)
            .Image!.ResolvedLinkedImageBytes.Should().Equal(preview);
    }

    [Fact]
    public void BuildFormatCapabilityRows_ReportsTemplateCompatibilityImportAndExportTruth()
    {
        var workflow = new DocumentPersistenceWorkflow();

        var rows = workflow.BuildFormatCapabilityRows(includeXpsExport: true);

        rows.Single(row => row.FormatName == "Word Document" && row.PrimaryExtension == ".docx")
            .Should()
            .Match<DocumentFormatCapabilityRow>(row =>
                row.Kind == DocumentFormatCapabilityKind.OpenSave &&
                row.Description.Contains("drops macro parts", StringComparison.Ordinal) &&
                row.Description.Contains("VBA project bytes are not written", StringComparison.Ordinal));
        rows.Single(row => row.FormatName == "Word Macro-Enabled Document" && row.PrimaryExtension == ".docm")
            .Should()
            .Match<DocumentFormatCapabilityRow>(row =>
                row.Kind == DocumentFormatCapabilityKind.OpenSave &&
                !row.OpensAsTemplate &&
                row.Description.Contains("preserves existing VBA project bytes", StringComparison.Ordinal) &&
                row.Description.Contains("does not inspect or execute macros", StringComparison.Ordinal) &&
                row.Description.Contains("drops macro parts", StringComparison.Ordinal));
        rows.Single(row => row.FormatName == "OpenDocument Text" && row.PrimaryExtension == ".odt")
            .Description.Should().Contain("Unsupported ODF constructs");
        rows.Single(row => row.FormatName == "OpenDocument Text Template" && row.PrimaryExtension == ".ott")
            .Should()
            .Match<DocumentFormatCapabilityRow>(row =>
                row.Kind == DocumentFormatCapabilityKind.Template &&
                row.OpensAsTemplate &&
                row.Description.Contains("new unsaved document", StringComparison.Ordinal) &&
                row.Description.Contains("unsupported ODF constructs", StringComparison.OrdinalIgnoreCase));
        rows.Single(row => row.FormatName == "Word Template" && row.PrimaryExtension == ".dotx")
            .Should()
            .Match<DocumentFormatCapabilityRow>(row =>
                row.Kind == DocumentFormatCapabilityKind.Template &&
                row.OpensAsTemplate &&
                row.Description.Contains("new unsaved document", StringComparison.Ordinal) &&
                row.Description.Contains("drops macro parts", StringComparison.Ordinal) &&
                row.Description.Contains("VBA project bytes are not written", StringComparison.Ordinal));
        rows.Single(row => row.FormatName == "Word Macro-Enabled Template" && row.PrimaryExtension == ".dotm")
            .Should()
            .Match<DocumentFormatCapabilityRow>(row =>
                row.Kind == DocumentFormatCapabilityKind.Template &&
                row.OpensAsTemplate &&
                row.Description.Contains("new unsaved document", StringComparison.Ordinal) &&
                row.Description.Contains("preserves existing VBA project bytes", StringComparison.Ordinal) &&
                row.Description.Contains("does not inspect or execute macros", StringComparison.Ordinal) &&
                row.Description.Contains("drops macro parts", StringComparison.Ordinal));
        rows.Single(row => row.FormatName == "Word 97-2003 Document" && row.PrimaryExtension == ".doc")
            .Should()
            .Match<DocumentFormatCapabilityRow>(row =>
                row.Kind == DocumentFormatCapabilityKind.LegacyCompatibility &&
                row.IsLegacy &&
                row.Description.Contains("Compatibility format", StringComparison.Ordinal));

        var pdfImport = rows.Single(row =>
            row.FormatName == "PDF Document" &&
            row.PrimaryExtension == ".pdf" &&
            row.Kind == DocumentFormatCapabilityKind.ImportOnly);
        pdfImport.CanOpen.Should().BeTrue();
        pdfImport.CanSave.Should().BeFalse();
        pdfImport.CanExport.Should().BeFalse();
        pdfImport.Description.Should().Contain("import-only");

        var fixedLayout = rows.Where(row => row.Kind == DocumentFormatCapabilityKind.ExportOnly).ToArray();
        fixedLayout.Select(row => row.PrimaryExtension).Should().Equal(".pdf", ".xps");
        fixedLayout.Should().OnlyContain(row => row.CanExport && !row.CanOpen && !row.CanSave);
    }

    [Fact]
    public void ImportPdfText_UsesExplicitImportAdaptersOutsideNormalOpenSaveCatalog()
    {
        var documentAdapter = new FakeDocumentAdapter(
            [new FileFormatDescriptor(".docx", "Word Document")]);
        var pdfAdapter = new FakeDocumentAdapter(
            [new FileFormatDescriptor(".pdf", "PDF Document", CanOpen: true, CanSave: false)]);
        var workflow = new DocumentPersistenceWorkflow([documentAdapter], [pdfAdapter]);
        var path = WriteText("Imported.pdf", "PDF body text");

        var result = workflow.ImportPdfText(path);

        result.Document.PlainText.Should().Be("PDF body text");
        result.Adapter.Should().BeSameAs(pdfAdapter);
        result.Format!.CanSave.Should().BeFalse();
        workflow.CanOpenPath(path).Should().BeFalse("PDF import is not a normal editable Open path");
        workflow.BuildPdfImportDialogPlan().Filter.Should().Contain("PDF Document (*.pdf)|*.pdf");
    }

    [Fact]
    public void BuildSavePickerPlan_UsesPreferredExtensionBeforeCurrentPath()
    {
        var adapter = new FakeDocumentAdapter(
            [
                new FileFormatDescriptor(".docx", "Word Document"),
                new FileFormatDescriptor(".rtf", "Rich Text Format"),
                new FileFormatDescriptor(".txt", "Plain Text"),
            ]);
        var workflow = new DocumentPersistenceWorkflow([adapter]);

        var plan = workflow.BuildSavePickerPlan(
            currentPath: Path.Combine(_tempDir, "Draft.rtf"),
            currentFileName: "Draft.rtf",
            fallbackDisplayName: "Document",
            preferredExtension: ".txt");

        plan.DefaultExtensionWithDot.Should().Be(".txt");
        plan.DefaultExtensionWithoutDot.Should().Be("txt");
        plan.SuggestedFileName.Should().Be("Draft.txt");
        plan.FileTypes[0].Patterns.Should().Contain("*.txt");
    }

    [Fact]
    public void TryResolveCurrentSaveTarget_ReturnsFalseForReadOnlyFormats()
    {
        var adapter = new FakeDocumentAdapter(
            [new FileFormatDescriptor(".doc", "Word 97-2003 Document", CanOpen: true, CanSave: false)]);
        var workflow = new DocumentPersistenceWorkflow([adapter]);

        workflow.TryResolveCurrentSaveTarget(Path.Combine(_tempDir, "Legacy.doc"), out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void TryResolveSaveTarget_UsesSelectedFilterIndexForDuplicateExtensionAdapters()
    {
        var transitional = new FakeDocumentAdapter([new FileFormatDescriptor(".docx", "Word Document")]);
        var strict = new FakeDocumentAdapter([new FileFormatDescriptor(".docx", "Strict Open XML Document")]);
        var flatOpc = new FakeDocumentAdapter([new FileFormatDescriptor(".xml", "Word XML Document")]);
        var word2003 = new FakeDocumentAdapter([new FileFormatDescriptor(".xml", "Word 2003 XML Document")]);
        var workflow = new DocumentPersistenceWorkflow([transitional, strict, flatOpc, word2003]);

        workflow.TryResolveSaveTarget(Path.Combine(_tempDir, "Strict.docx"), filterIndex: 2, out var strictTarget)
            .Should()
            .BeTrue();
        workflow.TryResolveSaveTarget(Path.Combine(_tempDir, "Word2003.xml"), filterIndex: 4, out var word2003Target)
            .Should()
            .BeTrue();

        strictTarget.Adapter.Should().BeSameAs(strict);
        strictTarget.Format!.FormatName.Should().Be("Strict Open XML Document");
        word2003Target.Adapter.Should().BeSameAs(word2003);
        word2003Target.Format!.FormatName.Should().Be("Word 2003 XML Document");
    }

    [Fact]
    public void TryResolveSaveTarget_SelectedHtmlAdapterWinsForSiblingExtension()
    {
        var filtered = new FakeDocumentAdapter(
            [
                new FileFormatDescriptor(".html", "Web Page, Filtered"),
                new FileFormatDescriptor(".htm", "Web Page, Filtered"),
            ]);
        var full = new FakeDocumentAdapter(
            [
                new FileFormatDescriptor(".html", "Web Page"),
                new FileFormatDescriptor(".htm", "Web Page"),
            ]);
        var workflow = new DocumentPersistenceWorkflow([filtered, full]);

        workflow.TryResolveSaveTarget(Path.Combine(_tempDir, "Full.htm"), filterIndex: 3, out var target)
            .Should()
            .BeTrue();

        target.Adapter.Should().BeSameAs(full);
        target.Format!.FormatName.Should().Be("Web Page");
    }

    [Fact]
    public void Save_WritesThroughSiblingTempSoFailuresDoNotTruncateExistingTarget()
    {
        var path = WriteText("Existing.docx", "original");
        var adapter = new FakeDocumentAdapter(
            [new FileFormatDescriptor(".docx", "Word Document")])
        {
            SaveAction = (_, stream) =>
            {
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write("partial");
                writer.Flush();
                throw new IOException("simulated write failure");
            },
        };
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        workflow.TryResolveCurrentSaveTarget(path, out var target).Should().BeTrue();

        var act = () => workflow.Save(Document("updated"), target);

        act.Should().Throw<IOException>().WithMessage("simulated write failure");
        File.ReadAllText(path).Should().Be("original");
        Directory.GetFiles(_tempDir, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void Save_ReplacesTargetAfterAdapterSucceeds()
    {
        var path = WriteText("Existing.docx", "old");
        var adapter = new FakeDocumentAdapter(
            [new FileFormatDescriptor(".docx", "Word Document")]);
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        workflow.TryResolveCurrentSaveTarget(path, out var target).Should().BeTrue();

        workflow.Save(Document("new"), target);

        File.ReadAllText(path).Should().Be("new");
    }

    private string WriteText(string name, string text)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static TextDocument Document(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private sealed class FakeDocumentAdapter(IReadOnlyList<FileFormatDescriptor> formats) : IDocumentFileAdapter
    {
        public string Extension => formats[0].Extension;

        public string FormatName => formats[0].FormatName;

        public IReadOnlyList<FileFormatDescriptor> Formats => formats;

        public Action<TextDocument, Stream>? SaveAction { get; init; }

        public Func<Stream, TextDocument>? LoadAction { get; init; }

        public TextDocument Load(Stream stream)
        {
            if (LoadAction is not null)
                return LoadAction(stream);
            using var reader = new StreamReader(stream, leaveOpen: true);
            return Document(reader.ReadToEnd());
        }

        public void Save(TextDocument document, Stream stream)
        {
            if (SaveAction is not null)
            {
                SaveAction(document, stream);
                return;
            }

            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write(document.PlainText);
        }
    }
}
