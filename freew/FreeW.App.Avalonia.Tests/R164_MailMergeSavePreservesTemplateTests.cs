using Avalonia.Headless;
using Free.Shared.AppServices;
using Free.Shared.IO;
using FreeW.App.Presentation.Options;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// round164/meta-F2: round 163 fixed Ctrl+S baking a mail-merge preview's single-record values over
/// the user's template (<see cref="FreeW.App.Host.FileCommands"/>'s GetDocument port preferring
/// <c>DocumentView.MailMergeSession.Template</c>) but only on the WPF host. The Avalonia shell wires
/// <see cref="MainWindow"/>'s equivalent <c>FreeWDocumentFilePorts.GetDocument</c> port off the SAME
/// <see cref="FreeW.App.Presentation.Ribbon.MailMergeSession"/> type (<see cref="MainWindow.MailMergeForTests"/>
/// exposes the shared <c>MailMergeEngine</c>, whose <c>TogglePreview</c>/<c>NavigatePreview</c> stash the
/// template exactly as the WPF ribbon commands do), so the identical Ctrl+S-while-previewing bug is still
/// live on Linux/macOS. This drives the real, production <see cref="MainWindow"/> Save path (not just the
/// portable workflow types underneath it) and asserts the SAVED BYTES on disk.
/// </summary>
public sealed class R164_MailMergeSavePreservesTemplateTests : IDisposable
{
    // Guillemet merge-field delimiters, written as escapes to avoid any editor/tool re-encoding risk.
    private const string MergeFieldName = "«Name»";

    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.R164MailMergeSave-");

    private string TempDirectory => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    // The exact user gesture from the finding, on the Avalonia shell: Select Recipients, Preview
    // Results (baking record 0's values into the live editor document exactly as the ribbon command
    // does), then Save. Before the r164 fix, GetDocument read _editor.Document unconditionally, so the
    // saved bytes contained "Alice", not the merge field.
    [Fact]
    public async Task SaveWhilePreviewing_SavesTemplateNotBakedPreview()
    {
        var savePath = Path.Combine(TempDirectory, "Letter.docx");
        var saveResult = false;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow();
            window.Editor.LoadDocument(DocumentWith($"Dear {MergeFieldName}, welcome."));

            window.MailMergeForTests.LoadRecipientsCsv("Name\nAlice\nBob");
            window.MailMergeForTests.TogglePreview();

            window.MailMergeForTests.Session.IsPreviewing.Should().BeTrue();
            window.Editor.Document.PlainText.Should().Contain("Alice");
            window.Editor.Document.PlainText.Should().NotContain(MergeFieldName);

            saveResult = await window.SaveCopyToPathAsync(savePath);
        });

        saveResult.Should().BeTrue();
        var saved = DocxReader.Read(savePath).PlainText;
        saved.Should().Contain(
            MergeFieldName,
            "Save while Mailings > Preview Results is active must write the mail-merge TEMPLATE, " +
            "not the previewed record's baked-in values, on the Avalonia shell exactly as it already " +
            "does on WPF");
        saved.Should().NotContain("Alice");
    }

    // Sibling/no-regression: Save while NOT previewing -- including right after loading recipients but
    // before ever clicking Preview Results -- must keep saving whatever is actually on screen. The fix
    // must not make Save start ignoring live edits merely because a mail-merge session exists.
    [Fact]
    public async Task SaveWhileNotPreviewing_StillSavesLiveEditorContent()
    {
        var savePath = Path.Combine(TempDirectory, "Draft.docx");
        var saveResult = false;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow();
            window.Editor.LoadDocument(DocumentWith("Plain letter, no merge fields yet."));

            window.MailMergeForTests.LoadRecipientsCsv("Name\nAlice\nBob");
            window.MailMergeForTests.Session.IsPreviewing.Should().BeFalse();

            window.Editor.LoadDocument(DocumentWith("Edited after loading recipients."));

            saveResult = await window.SaveCopyToPathAsync(savePath);
        });

        saveResult.Should().BeTrue();
        DocxReader.Read(savePath).PlainText.Trim().Should().Be("Edited after loading recipients.");
    }

    // r163-remediation carryover, now proven on Avalonia: MailMergeEngine/session is built once per
    // WINDOW; Session.Template is set per DOCUMENT. With GetDocument now preferring Template whenever
    // one is set, a preview left active on document A must not survive a File > New into document B --
    // otherwise Ctrl+S on the fresh document B would still write stale document A's template over it.
    [Fact]
    public async Task LoadingADifferentDocumentAfterPreviewing_EndsThePreview_SoSaveWritesTheNewDocument()
    {
        var savePath = Path.Combine(TempDirectory, "AfterNew.docx");
        var saveResult = false;
        var isPreviewingAfterNew = true;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow();
            window.Editor.LoadDocument(DocumentWith($"Dear {MergeFieldName}, welcome."));

            window.MailMergeForTests.LoadRecipientsCsv("Name\nAlice\nBob");
            window.MailMergeForTests.TogglePreview();
            window.MailMergeForTests.Session.IsPreviewing.Should().BeTrue();

            // The user abandons the merge by simply starting a new document -- the same "a different
            // document is loaded" gesture the finding calls out, routed through the Avalonia shell's
            // single document-swap choke point (MainWindow.LoadDocumentContent).
            await window.NewDocumentAsyncForTests();
            isPreviewingAfterNew = window.MailMergeForTests.Session.IsPreviewing;

            saveResult = await window.SaveCopyToPathAsync(savePath);
        });

        isPreviewingAfterNew.Should().BeFalse(
            "loading a different document must end the previous document's preview, or its stale " +
            "template would get written over the new document on the next save");
        saveResult.Should().BeTrue();
        var saved = DocxReader.Read(savePath).PlainText;
        saved.Should().NotContain(MergeFieldName);
        saved.Trim().Should().BeEmpty("File > New must save the fresh empty document, not the stale template");
    }

    private MainWindow CreateWindow() =>
        new(
            Array.Empty<string>(),
            new FreeWOptions(),
            ApplicationOptionsStore<FreeWOptions>.ForPath(UniqueSettingsPath()),
            promptSaveChangesAsync: _ => Task.FromResult(SaveChangesPrompt.DontSave));

    private string UniqueSettingsPath() =>
        Path.Combine(TempDirectory, Guid.NewGuid().ToString("N"), "settings.json");

    private static TextDocument DocumentWith(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private static async Task RunOnUiThread(Func<Task> action) =>
        await Session.Dispatch(
            async () =>
            {
                await action();
                return true;
            },
            CancellationToken.None);
}
