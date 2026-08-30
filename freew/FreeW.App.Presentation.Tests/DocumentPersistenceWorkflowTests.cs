using Free.Shared.IO;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentPersistenceWorkflowTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.DocumentPersistenceWorkflowTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    [Theory]
    [InlineData("draft.DOCX", true)]
    [InlineData("draft", false)]
    [InlineData("bad\0draft.docx", false)]
    public void CanOpenPath_ClassifiesExtensionWithoutThrowing(string path, bool expected)
    {
        var adapter = new FakeDocumentAdapter(
            [new FileFormatDescriptor(".docx", "Word Document")]);
        var workflow = new DocumentPersistenceWorkflow([adapter]);

        workflow.CanOpenPath(path).Should().Be(expected);
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

        var pickerPlan = workflow.BuildPdfImportPickerPlan();
        pickerPlan.FileTypes.Select(type => type.DisplayName).Should().Equal("PDF Document");
        pickerPlan.FileTypes.Should().OnlyContain(type => type.Patterns.SequenceEqual(new[] { "*.pdf" }));
        pickerPlan.FileTypes.Should().OnlyContain(type => type.MimeTypes!.SequenceEqual(new[] { "application/pdf" }));
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

    // ── r137-freew-persistence-external-modification ───────────────────────────────────────────
    //
    // FreeW had NO external-modification detection at all, unlike FreeX's WorkbookSaveService: if
    // the file changed on disk since it was opened (another app, another FreeW/Word instance, a
    // sync client), Save silently overwrote it with zero warning. These port FreeX's
    // expectedLastWriteTimeUtc check (WorkbookSaveServiceTests.SaveAsync_FileModifiedSinceOpen_...)
    // onto DocumentPersistenceWorkflow.

    [Fact]
    public void Open_CapturesSourceLastWriteTimeUtcMatchingFileSystem()
    {
        var adapter = new FakeDocumentAdapter([new FileFormatDescriptor(".docx", "Word Document")]);
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        var path = WriteText("Existing.docx", "content");

        var result = workflow.Open(path);

        result.SourceLastWriteTimeUtc.Should().Be(File.GetLastWriteTimeUtc(path));
    }

    // ── r174-freew-persistence-readonly-open ────────────────────────────────────────────────────
    //
    // FreeX's WorkbookReadOnlySession.IsFileWriteRestricted (round 149) checks the OS read-only
    // attribute at Open time specifically because, before that fix, "opened fully editable with
    // zero indication until the first Save failed". FreeW's Open had no equivalent check at all.

    [Fact]
    public void Open_OsReadOnlyAttributeSet_FlagsResultAsFileSystemReadOnly()
    {
        // FAIL-BEFORE: before the fix, DocumentOpenResult had no IsFileSystemReadOnly member and
        // Open never inspected File.GetAttributes at all, so a read-only file opened with no way
        // for a caller to distinguish it from any other editable file until Save later failed.
        var adapter = new FakeDocumentAdapter([new FileFormatDescriptor(".docx", "Word Document")]);
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        var path = WriteText("ReadOnly.docx", "content");
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

        try
        {
            var result = workflow.Open(path);

            result.IsFileSystemReadOnly.Should().BeTrue();
        }
        finally
        {
            // Clear the attribute so the temp-directory cleanup in Dispose() can delete the file.
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }
    }

    // r174 remediation. The Open fix above left the recovery door open: OpenSnapshot in the same file
    // returns originalPath as the save target -- the caller wires it straight into MarkDirtyWithPath --
    // but never performed the write-restriction check. So crashing with a read-only original and then
    // accepting crash recovery produced a fully editable document with no indication, which is exactly
    // the defect Open was fixed for, reached through a different door. A scope auditor found this; it
    // was not disclosed in the partial status.
    [Fact]
    public void OpenSnapshot_OriginalIsOsReadOnly_FlagsResultAsFileSystemReadOnly()
    {
        var workflow = new DocumentPersistenceWorkflow([new FakeDocumentAdapter([new FileFormatDescriptor(".docx", "Word Document")])]);
        var snapshotPath = WriteDocx("Snapshot.docx");
        var originalPath = WriteDocx("ReadOnlyOriginal.docx");
        File.SetAttributes(originalPath, File.GetAttributes(originalPath) | FileAttributes.ReadOnly);

        try
        {
            var result = workflow.OpenSnapshot(snapshotPath, originalPath);

            result.TargetPath.Should().Be(originalPath, "recovery adopts the original as the save target");
            result.IsFileSystemReadOnly.Should().BeTrue(
                "the save target is write-restricted, and the recovery path must say so just as Open does");
        }
        finally
        {
            File.SetAttributes(originalPath, File.GetAttributes(originalPath) & ~FileAttributes.ReadOnly);
        }
    }

    [Fact]
    public void OpenSnapshot_WritableOriginal_IsNotFlaggedFileSystemReadOnly()
    {
        // No-regression sibling: ordinary crash recovery onto a writable original stays editable.
        var workflow = new DocumentPersistenceWorkflow([new FakeDocumentAdapter([new FileFormatDescriptor(".docx", "Word Document")])]);
        var snapshotPath = WriteDocx("Snapshot2.docx");
        var originalPath = WriteDocx("WritableOriginal.docx");

        var result = workflow.OpenSnapshot(snapshotPath, originalPath);

        result.IsFileSystemReadOnly.Should().BeFalse();
    }

    private string WriteDocx(string name)
    {
        var path = Path.Combine(_tempDir, name);
        DocxWriter.Write(TextDocument.CreateEmpty(), path);
        return path;
    }

    [Fact]
    public void Open_OrdinaryWritableFile_IsNotFlaggedFileSystemReadOnly()
    {
        // No-regression sibling: an ordinary writable file must still open as fully editable, with
        // the new flag reporting false rather than the check misfiring on the common case.
        var adapter = new FakeDocumentAdapter([new FileFormatDescriptor(".docx", "Word Document")]);
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        var path = WriteText("Writable.docx", "content");

        var result = workflow.Open(path);

        result.IsFileSystemReadOnly.Should().BeFalse();
    }

    [Fact]
    public void Open_MissingFile_StillThrowsFileNotFoundExceptionUnaffectedByReadOnlyCheck()
    {
        // Adjacent-case regression: the read-only probe now runs before File.OpenRead. It must not
        // change Open's existing contract of throwing when the source file does not exist at all --
        // the probe itself hits the missing file first (via File.GetAttributes), swallows it
        // internally (treated the same as "not restricted"), and Open then goes on to the normal
        // File.OpenRead call, which still throws exactly as it always did.
        var adapter = new FakeDocumentAdapter([new FileFormatDescriptor(".docx", "Word Document")]);
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        var path = Path.Combine(_tempDir, "DoesNotExist.docx");

        var act = () => workflow.Open(path);

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Save_FileModifiedSinceOpen_ThrowsAndLeavesTargetAndTempUntouched()
    {
        // FAIL-BEFORE: before the fix, DocumentPersistenceWorkflow.Save had no
        // expectedLastWriteTimeUtc parameter and no external-modification check at all -- it always
        // overwrote the target unconditionally, so the second writer's content on disk would be
        // clobbered with "clobbered" and this assertion would fail (both the exception assertion
        // and the "content preserved" assertion).
        var adapter = new FakeDocumentAdapter([new FileFormatDescriptor(".docx", "Word Document")]);
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        var path = WriteText("Existing.docx", "original");
        workflow.TryResolveCurrentSaveTarget(path, out var target).Should().BeTrue();
        var openedAtUtc = File.GetLastWriteTimeUtc(path);

        // Simulate a second writer (another app instance, a sync client, a colleague) touching the
        // file after we "opened" it.
        Thread.Sleep(20);
        File.WriteAllText(path, "someone else's edit");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);

        var act = () => workflow.Save(Document("clobbered"), target, expectedLastWriteTimeUtc: openedAtUtc);

        act.Should().Throw<DocumentExternallyModifiedException>();
        File.ReadAllText(path).Should().Be(
            "someone else's edit",
            "the save must bail out before writing over the other writer's change");
        Directory.GetFiles(_tempDir, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void Save_FileUnmodifiedSinceOpen_SavesNormallyWithExpectedWriteTimePassed()
    {
        // No-regression sibling: when the file on disk still has the write time captured at open,
        // the save must proceed exactly as before.
        var adapter = new FakeDocumentAdapter([new FileFormatDescriptor(".docx", "Word Document")]);
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        var path = WriteText("Existing.docx", "original");
        workflow.TryResolveCurrentSaveTarget(path, out var target).Should().BeTrue();
        var openedAtUtc = File.GetLastWriteTimeUtc(path);

        workflow.Save(Document("updated"), target, expectedLastWriteTimeUtc: openedAtUtc);

        File.ReadAllText(path).Should().Be("updated");
        Directory.GetFiles(_tempDir, "*.tmp").Should().BeEmpty();
    }

    // ── r138-freew-persistence-modified-metadata ────────────────────────────────────────────────
    //
    // Save never refreshed the document's Modified/LastModifiedBy core properties, so
    // docProps/core.xml (and the Document Properties dialog, and SAVEDATE/LASTSAVEDBY fields, which
    // all read the same TextDocument.Properties model) stayed frozen at whatever they were at
    // document creation/open forever, even after real saves.

    [Fact]
    public void Save_RefreshesModifiedAndLastModifiedByOnSuccess()
    {
        // FAIL-BEFORE: before the fix, Save wrote the adapter's output but never touched
        // document.Properties.Modified/LastModifiedBy, so both assertions below would fail (Modified
        // would still equal the stale seeded value; LastModifiedBy would still be "Stale Author").
        var path = WriteText("Existing.docx", "old");
        var adapter = new FakeDocumentAdapter(
            [new FileFormatDescriptor(".docx", "Word Document")]);
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        workflow.TryResolveCurrentSaveTarget(path, out var target).Should().BeTrue();

        var document = Document("new");
        var staleModified = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        document.Properties.Modified = staleModified;
        document.Properties.LastModifiedBy = "Stale Author";
        document.Properties.Author = "Doc Author";
        var beforeSave = DateTimeOffset.Now;

        workflow.Save(document, target);

        document.Properties.Modified.Should().NotBeNull();
        document.Properties.Modified.Should().NotBe(staleModified);
        document.Properties.Modified!.Value.Should().BeOnOrAfter(beforeSave.AddSeconds(-2));
        document.Properties.LastModifiedBy.Should().Be(
            "Doc Author",
            "the resolved authorship falls back to the document's own author when it is set");
    }

    [Fact]
    public void Save_LeavesModifiedAndLastModifiedByUnchangedWhenAdapterThrows()
    {
        // No-regression sibling: a save that fails partway through must not leave the in-memory
        // model claiming a save that never actually reached disk.
        var path = WriteText("Existing.docx", "original");
        var adapter = new FakeDocumentAdapter(
            [new FileFormatDescriptor(".docx", "Word Document")])
        {
            SaveAction = (_, _) => throw new IOException("simulated write failure"),
        };
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        workflow.TryResolveCurrentSaveTarget(path, out var target).Should().BeTrue();

        var document = Document("updated");
        var staleModified = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        document.Properties.Modified = staleModified;
        document.Properties.LastModifiedBy = "Stale Author";

        var act = () => workflow.Save(document, target);

        act.Should().Throw<IOException>();
        document.Properties.Modified.Should().Be(staleModified);
        document.Properties.LastModifiedBy.Should().Be("Stale Author");
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
