using System;
using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.IO;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// freew-mail-merge F1: Ctrl+S / File &gt; Save while Mailings &gt; Preview Results is showing a
/// previewed record must save the mail-merge TEMPLATE, not the merged, single-record document the
/// preview loaded into the editor for on-screen display. Before the fix, FileCommands' GetDocument
/// port read <c>_editor.Model</c> unconditionally -- which, while previewing, IS that merged document
/// (<see cref="MailMergeSessionWorkflow.EnsurePreviewing"/> -&gt; Realize -&gt; <c>editor.LoadModel</c>,
/// exactly as <c>FreeWRibbonCommands.PreviewMergeRecordCommand</c> drives it) -- so Save/Save As/
/// Save a Copy permanently replaced every merge-field placeholder in the user's template with the
/// previewed recipient's literal values. The fix threads <see cref="DocumentView.MailMergeSession"/>
/// (published once by <c>FreeWRibbonCommands.BuildCore</c>, mirrored here to reproduce that wiring)
/// through to <c>FileCommands</c>' GetDocument port, which now prefers the still-live
/// <see cref="MailMergeSession.Template"/> whenever a preview is active -- the same
/// <c>session.Template ?? editor.Model</c> guard <c>FreeWRibbonCommands.CurrentMailMergeDocument</c>
/// already uses for every other mail-merge operation.
/// </summary>
public sealed class R163_MailMergeSavePreservesTemplateTests : IDisposable
{
    private const string MergeFieldName = "«Name»";

    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.R163MailMergeSave-");

    private string TempDirectory => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    // The exact user gesture from the finding: Start Mail Merge, select recipients, click Preview
    // Results (baking the current record's values into the live editor model exactly as the ribbon
    // command does), then Save. The SAVED BYTES must still contain the merge field, not "Alice".
    [StaFact]
    public void SaveWhilePreviewing_SavesTemplateNotBakedPreview()
    {
        var (file, editor, session) = CreateHarness();
        editor.LoadModel(DocumentWith($"Dear {MergeFieldName}, welcome."));

        var workflow = new MailMergeSessionWorkflow(session);
        workflow.LoadRecipients(MergeData.FromCsv("Name\nAlice\nBob"));

        // Mirrors FreeWRibbonCommands.PreviewMergeRecordCommand.Execute followed by
        // Realize(editor, execution): the merged document for the current record is loaded straight
        // into the live editor, with no marker that it came from a preview.
        var execution = workflow.EnsurePreviewing(editor.Model);
        Assert.True(execution.Success);
        editor.LoadModel(execution.DocumentToLoad!);
        Assert.True(session.IsPreviewing);
        Assert.Contains("Alice", editor.Model.PlainText);
        Assert.DoesNotContain(MergeFieldName, editor.Model.PlainText);

        var savePath = Path.Combine(TempDirectory, "Letter.docx");
        Assert.True(file.SaveCopyToPath(savePath));

        var saved = DocxReader.Read(savePath).PlainText;
        Assert.Contains(MergeFieldName, saved);
        Assert.DoesNotContain("Alice", saved);
    }

    // Sibling/no-regression: Save while NOT previewing -- the overwhelmingly common case, including
    // right after Select Recipients but before ever clicking Preview Results -- must keep saving
    // whatever is actually on screen. The fix must not make Save start ignoring live edits whenever
    // a mail-merge session merely exists.
    [StaFact]
    public void SaveWhileNotPreviewing_StillSavesLiveEditorContent()
    {
        var (file, editor, session) = CreateHarness();
        editor.LoadModel(DocumentWith("Plain letter, no merge fields yet."));

        var workflow = new MailMergeSessionWorkflow(session);
        workflow.LoadRecipients(MergeData.FromCsv("Name\nAlice\nBob"));
        Assert.False(session.IsPreviewing);

        editor.LoadModel(DocumentWith("Edited after loading recipients."));

        var savePath = Path.Combine(TempDirectory, "Draft.docx");
        Assert.True(file.SaveCopyToPath(savePath));

        Assert.Equal(
            "Edited after loading recipients.",
            DocxReader.Read(savePath).PlainText.Trim());
    }

    // r163 remediation. MailMergeSession is built once per WINDOW; its Template is set per DOCUMENT.
    // Nothing cleared it when a different document was loaded, so with the save port preferring
    // Template whenever one exists, a preview left active on document A was written over document B
    // the moment the user opened B and pressed Ctrl+S -- destroying a file that has nothing to do
    // with the mail merge. That is a wider blast radius than the bug the save port was added to fix.
    [StaFact]
    public void OpeningAnotherDocumentAfterPreviewing_SavesTheNewDocumentNotTheStaleTemplate()
    {
        var (file, editor, session) = CreateHarness();
        editor.LoadModel(DocumentWith($"Dear {MergeFieldName}, welcome."));

        var workflow = new MailMergeSessionWorkflow(session);
        workflow.LoadRecipients(MergeData.FromCsv("Name\nAlice\nBob"));
        var execution = workflow.EnsurePreviewing(editor.Model);
        Assert.True(execution.Success);
        editor.LoadModel(execution.DocumentToLoad!);
        Assert.True(session.IsPreviewing);

        // The user abandons the merge by simply opening an unrelated document.
        var otherPath = Path.Combine(TempDirectory, "Unrelated.docx");
        DocxWriter.Write(DocumentWith("A completely unrelated document."), otherPath);
        Assert.True(file.OpenPath(otherPath));

        Assert.False(session.IsPreviewing, "loading another document ends the previous document's preview");

        var savePath = Path.Combine(TempDirectory, "UnrelatedSaved.docx");
        Assert.True(file.SaveCopyToPath(savePath));

        var saved = DocxReader.Read(savePath).PlainText;
        Assert.Contains("A completely unrelated document.", saved);
        Assert.DoesNotContain(MergeFieldName, saved);
    }

    // Sibling/no-regression: File > New must not leave a stale template able to overwrite the new
    // empty document either.
    [StaFact]
    public void FileNewAfterPreviewing_EndsThePreview()
    {
        var (file, editor, session) = CreateHarness();
        editor.LoadModel(DocumentWith($"Dear {MergeFieldName}, welcome."));

        var workflow = new MailMergeSessionWorkflow(session);
        workflow.LoadRecipients(MergeData.FromCsv("Name\nAlice\nBob"));
        var execution = workflow.EnsurePreviewing(editor.Model);
        Assert.True(execution.Success);
        editor.LoadModel(execution.DocumentToLoad!);
        Assert.True(session.IsPreviewing);

        Assert.True(file.New());

        Assert.False(session.IsPreviewing);
        // The recipient data survives, so re-running a merge in this window need not start over.
        Assert.NotNull(session.Data);
    }

    private (FileCommands File, DocumentView Editor, MailMergeSession Session) CreateHarness()
    {
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var session = new MailMergeSession();
        // Production wiring: FreeWRibbonCommands.BuildCore publishes the session onto the editor
        // right after constructing it (see "editor.MailMergeSession = mergeSession" there).
        editor.MailMergeSession = session;
        var file = new FileCommands(
            window,
            editor,
            () => { },
            loadRecentFilesStore: () => RecentFilesStore.Load(
                Path.Combine(TempDirectory, Guid.NewGuid().ToString("N") + ".json")),
            messageService: new RecordingUserMessageService(),
            confirmSaveCompatibility: _ => true);
        return (file, editor, session);
    }

    private static TextDocument DocumentWith(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));
        return document;
    }
}
