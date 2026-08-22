using FreeP.App.Compositor;
using FreeP.Core.Model;
using Free.Shared.AppServices;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// shared-proofing F4: a word added via "Add to Dictionary" must survive closing the presentation
/// (a fresh <see cref="PresentationReviewWorkflowSession"/> over a fresh <see cref="EditingSession"/>,
/// as happens on reopen/app restart) instead of living only in the in-memory
/// <see cref="PresentationProofingDictionaryState"/> for the lifetime of the session that added it.
/// Uses an in-memory fake file system (mirrors FreeW's CustomDictionaryStoreTests) so persistence is
/// verifiable without touching the real user data folder.
/// </summary>
public sealed class PresentationCustomDictionaryPersistenceTests
{
    private const string StorePath = @"C:\fake\FreeP\customdictionary.lex";

    [Fact]
    public void WordAddedToDictionary_SurvivesAFreshSessionOverTheSameStore()
    {
        var fs = new FakeDictionaryFileSystem();
        var firstStore = new PresentationCustomDictionaryStore(StorePath, fs);
        var firstSession = CreateSession(BuildTypoPresentation(), firstStore);

        var opened = firstSession.ShowProofingPane();
        var typoRowIndex = opened.Rows.Single(row => row.Text == "teh").RowIndex;
        firstSession.SelectProofingIssueRow(typoRowIndex).SelectedRow!.Text.Should().Be("teh");
        var afterAdd = firstSession.AddSelectedProofingWordToDictionary();
        afterAdd.Rows.Should().NotContain(row => row.Text == "teh");

        // Simulate closing the presentation (or the app) and reopening it: a brand-new session over
        // a brand-new store instance pointed at the same backing file, exactly as the two production
        // hosts construct PresentationCustomDictionaryStore.Load() fresh on each launch.
        var secondStore = new PresentationCustomDictionaryStore(StorePath, fs);
        var secondSession = CreateSession(BuildTypoPresentation(), secondStore);

        secondSession.ProofingDictionaryState.NormalizedWords.Should().Contain("TEH");
        var reopened = secondSession.ShowProofingPane();
        reopened.Rows.Should().NotContain(
            row => row.Text == "teh",
            "the word was already in the persisted dictionary before this session ran Spelling");
    }

    [Fact]
    public void SessionsWithoutAnExplicitStore_StayIndependentInMemoryDictionaries()
    {
        // Sibling/no-regression case: two sessions that do NOT share a persistence backing (the
        // default used by every existing unit test) must remain isolated from each other, exactly
        // as before this fix -- adding a word in one session must never leak into another.
        var firstSession = CreateSession(BuildTypoPresentation());
        var opened = firstSession.ShowProofingPane();
        var typoRowIndex = opened.Rows.Single(row => row.Text == "teh").RowIndex;
        firstSession.SelectProofingIssueRow(typoRowIndex);
        firstSession.AddSelectedProofingWordToDictionary();
        firstSession.ProofingDictionaryState.NormalizedWords.Should().Contain("TEH");

        var secondSession = CreateSession(BuildTypoPresentation());
        secondSession.ProofingDictionaryState.NormalizedWords.Should().BeEmpty();
        secondSession.ShowProofingPane().Rows.Should().ContainSingle(row => row.Text == "teh");
    }

    [Fact]
    public void Store_PreservesFreePOrdinalDuplicateAndBlankLineSemantics()
    {
        var fs = new FakeDictionaryFileSystem();
        fs.Files[StorePath] = ["TEH", "TEH", "teh", "", " "];

        var store = new PresentationCustomDictionaryStore(StorePath, fs);

        store.Words.Should().Equal("TEH", "teh", " ");
        store.Add("TEH").Should().BeFalse();
        store.Add("Teh").Should().BeTrue();
    }

    [Fact]
    public void Store_FileSystemFailuresNeverBlockInMemoryProofing()
    {
        var fs = new FakeDictionaryFileSystem { ThrowOnAccess = true };

        var action = () => new PresentationCustomDictionaryStore(StorePath, fs);

        action.Should().NotThrow();
        var store = action();
        store.Add("TEH").Should().BeTrue();
        store.Words.Should().Equal("TEH");
    }

    [Fact]
    public void Store_NullPathRemainsInMemoryOnlyWithoutTouchingTheFileSystem()
    {
        var fs = new FakeDictionaryFileSystem { ThrowOnAccess = true };
        var store = new PresentationCustomDictionaryStore(storePath: null, fs);

        store.Add("TEH").Should().BeTrue();
        store.Words.Should().Equal("TEH");
    }

    private static Presentation BuildTypoPresentation()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 4, Name = "Caption", Text = "teh" });
        return presentation;
    }

    private static PresentationReviewWorkflowSession CreateSession(
        Presentation presentation,
        PresentationCustomDictionaryStore? dictionaryStore = null)
    {
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        return new PresentationReviewWorkflowSession(
            () => editor,
            new PresentationReviewWorkflowSessionCallbacks(
                MarkDirty: () => { },
                RefreshCanvas: () => { },
                RefreshNotesPane: () => { },
                RenderAccessibilityCheckerPaneIfVisible: _ => { },
                PresentAccessibilityCheckerPane: _ => { },
                OpenAltTextPane: () => { },
                OpenHyperlinkDialog: () => { },
                OpenMediaCaptionPane: () => { },
                RenderCommentPane: _ => { },
                RenderAltTextPaneIfVisible: _ => { },
                RenderReadingOrderPaneIfVisible: _ => { },
                PresentReadingOrderPane: _ => { },
                RenderProofingPaneIfVisible: _ => { },
                PresentProofingPane: _ => { },
                UpdateAfterCommentMutation: () => { },
                UpdateAfterCommentNavigation: () => { },
                UpdateAfterProofingCorrection: () => { }),
            dictionaryStore);
    }

    private sealed class FakeDictionaryFileSystem : IAtomicLineSetFileSystem
    {
        public Dictionary<string, string[]> Files { get; } = [];
        public bool ThrowOnAccess { get; init; }

        public bool FileExists(string path)
        {
            ThrowIfRequested();
            return Files.ContainsKey(path);
        }

        public string[] ReadAllLines(string path)
        {
            ThrowIfRequested();
            return Files.TryGetValue(path, out var lines) ? lines : [];
        }

        public void WriteAllTextAtomically(string path, string content)
        {
            ThrowIfRequested();
            Files[path] = ParseLines(content);
        }

        public void CreateDirectory(string path)
        {
            ThrowIfRequested();
            // The fake keeps files in a flat dictionary; no directory structure to create.
        }

        private void ThrowIfRequested()
        {
            if (ThrowOnAccess)
                throw new IOException("simulated dictionary file-system failure");
        }

        private static string[] ParseLines(string content) =>
            content.Length == 0
                ? []
                : content[..^Environment.NewLine.Length]
                    .Split(Environment.NewLine, StringSplitOptions.None);
    }
}
