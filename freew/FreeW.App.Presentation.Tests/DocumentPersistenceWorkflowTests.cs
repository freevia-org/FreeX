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

        public TextDocument Load(Stream stream)
        {
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
