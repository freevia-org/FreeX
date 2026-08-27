using System;
using System.IO;
using Free.Shared.AppServices;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// R164, autosave sibling of the save-path fix: an autosave tick taken while Mailings &gt;
/// Preview Results is showing a merged record must snapshot the mail-merge TEMPLATE, not the
/// previewed, single-record document.
///
/// <para>
/// Before the fix <c>AutosaveAdapter</c>'s <c>ExecuteWithDocument</c> port read
/// <c>editor.Document</c> unconditionally -- which, while previewing, IS the merged document
/// (<see cref="MailMergeEngine.TogglePreview"/> -&gt; <c>Realize</c> -&gt;
/// <c>DocumentView.LoadDocument</c>). So a tick that landed mid-preview, or the crash-recovery
/// snapshot that reuses the same port via <c>TryEmergencySnapshot</c>, wrote a snapshot with every
/// merge field already baked away into one recipient's literal values: recovering from it after a
/// crash silently destroyed the user's template. The WPF host already guards its sibling path
/// (<c>AutosaveCoordinator</c>'s port: <c>editor.MailMergeSession?.Template ?? editor.Model</c>);
/// the Avalonia shell now threads <c>MailMergeEngine.Session.Template</c> into the adapter for the
/// same effect.
/// </para>
///
/// <para>
/// Pure-model, like <c>MailingsTabTests</c>: <see cref="DocumentView"/> and
/// <see cref="AutosaveAdapter"/> are both constructible without a headless drawing backend so long
/// as the periodic loop is never started (the snapshot is driven directly through
/// <c>SnapshotNowForTests</c>, which is exactly what a periodic tick invokes), so these assertions
/// run as plain top-level statements and cannot be turned into a false "skip".
/// </para>
/// </summary>
public sealed class R164_AutosaveAdapterPreservesMailMergeTemplateTests : IDisposable
{
    private const string MergeFieldName = "«Name»";
    private const string RecipientCsv = "Name\nAlice\nBob";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "FreeW.R164AutosaveMailMerge-" + Path.GetRandomFileName());

    public R164_AutosaveAdapterPreservesMailMergeTemplateTests() =>
        Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { /* best-effort */ }
    }

    // The exact user gesture from the finding: load recipients, click Preview Results (baking the
    // current record's values into the live editor exactly as the ribbon command does), then let an
    // autosave tick land. The SNAPSHOT BYTES must still contain the merge field, not "Alice".
    [Fact]
    public void AutosaveWhilePreviewing_SnapshotsTemplateNotBakedPreview()
    {
        var (adapter, store, editor, engine, _) = CreateHarness(
            $"Dear {MergeFieldName}, welcome.");
        using var adapterLifetime = adapter;

        engine.LoadRecipientsCsv(RecipientCsv);
        engine.TogglePreview();

        engine.Session.IsPreviewing.Should().BeTrue();
        editor.Document.PlainText.Should().Contain("Alice");
        editor.Document.PlainText.Should().NotContain(MergeFieldName);

        adapter.SnapshotNowForTests();

        var snapshotPath = store.GetSnapshotPath(adapter.SnapshotIdForTests);
        File.Exists(snapshotPath).Should().BeTrue("a dirty document must produce an autosave snapshot");

        var snapshotted = DocxReader.Read(snapshotPath).PlainText;
        snapshotted.Should().Contain(
            MergeFieldName,
            "recovering the snapshot must give the user back their template, not one merged letter");
        snapshotted.Should().NotContain("Alice");
    }

    // Sibling / no-regression: the overwhelmingly common case -- no preview active, including right
    // after Select Recipients but before ever clicking Preview Results -- must keep snapshotting
    // whatever is actually in the editor. The fix must not make autosave start ignoring live edits
    // merely because a mail-merge session exists.
    [Fact]
    public void AutosaveWhileNotPreviewing_StillSnapshotsLiveEditorContent()
    {
        var (adapter, store, editor, engine, _) = CreateHarness("Plain letter, no merge fields yet.");
        using var adapterLifetime = adapter;

        engine.LoadRecipientsCsv(RecipientCsv);
        engine.Session.IsPreviewing.Should().BeFalse();

        editor.LoadDocument(DocumentWith("Edited after loading recipients."));

        adapter.SnapshotNowForTests();

        var snapshotPath = store.GetSnapshotPath(adapter.SnapshotIdForTests);
        File.Exists(snapshotPath).Should().BeTrue();
        DocxReader.Read(snapshotPath).PlainText.Trim()
            .Should().Be("Edited after loading recipients.");
    }

    private (AutosaveAdapter Adapter, AutosaveSnapshotStore Store, DocumentView Editor,
        MailMergeEngine Engine, FileCommandWorkflow Workflow) CreateHarness(string bodyText)
    {
        var editor = new DocumentView();
        editor.LoadDocument(DocumentWith(bodyText));

        var workflow = new FileCommandWorkflow(
            maxRecentEntries: () => 10,
            onChanged: () => { },
            promptSaveChanges: _ => SaveChangesPrompt.DontSave,
            save: () => true,
            loadRecentFilesStore: () => RecentFilesStore.Load(
                Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".json")));
        // Autosave only writes for a dirty document -- the state any real mid-session tick finds.
        workflow.MarkDirty();

        var engine = new MailMergeEngine(editor, MailMergeCallbacks());
        var store = new AutosaveSnapshotStore(_directory);
        // Production wiring, mirrored: MainWindow passes `() => _mailMerge?.Session.Template` as the
        // adapter's getMailMergeTemplate accessor.
        var adapter = new AutosaveAdapter(
            editor,
            workflow,
            sessionFactory: ports => new FreeWAutosaveSession(ports, store),
            getMailMergeTemplate: () => engine.Session.Template);

        return (adapter, store, editor, engine, workflow);
    }

    private static TextDocument DocumentWith(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private static FreeWRibbonHostExecutionPorts MailMergeCallbacks() =>
        new(
            Open: () => { }, Save: () => { }, Cut: () => { }, Copy: () => { }, Paste: () => { },
            Backstage: () => { }, NewDocument: () => { }, ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { }, ToggleRevealFormatting: () => { },
            OpenFindReplaceDialog: () => { }, SetPrintLayout: () => { }, SetWebLayout: () => { },
            SetDraftView: () => { }, OpenFontDialog: () => { }, OpenParagraphDialog: () => { },
            OpenPageSetupDialog: () => { }, ToggleOrientation: () => { }, ApplyMarginPreset: _ => { },
            ApplyPaperSize: _ => { }, OpenWordCountDialog: () => { }, InsertPicture: () => { },
            ApplyZoom: (_, _) => { });
}
