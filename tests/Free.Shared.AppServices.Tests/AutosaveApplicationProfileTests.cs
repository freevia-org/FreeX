using System.IO.Compression;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// Behaviour of the shared application autosave profile layer (recovery planning, offers, text,
/// the recovery sequencer, and the generic session facade). FreeW and FreeP bind this layer with a
/// profile instance only, so this is the single place the behaviour is tested.
/// </summary>
public sealed class AutosaveApplicationProfileTests : IDisposable
{
    private static readonly AutosaveRecoveryProfile Recovery = new(
        new AutosaveRecoveryPromptText("TestApp", "widgets"),
        "a widget",
        new AutosaveRecoveryTextDefaults(
            "TestApp - Recover",
            "Recover",
            "Skip",
            "No unsaved widgets were found.",
            "Could not recover the widget.\n\n{0}"));

    private static readonly AutosaveApplicationProfile<TestDocument> Profile = new(
        Recovery,
        Interval: TimeSpan.FromSeconds(45),
        WriteSnapshot: WriteDocument,
        ReadSnapshot: ReadDocument);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "SharedAutosaveApplicationProfileTests_" + Guid.NewGuid().ToString("N"));

    public AutosaveApplicationProfileTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Constructor_RejectsBlankProductNounAndFallback()
    {
        var defaults = new AutosaveRecoveryTextDefaults("t", "r", "s", "n", "f");

        Action blankProduct = () => _ = new AutosaveRecoveryProfile(
            new AutosaveRecoveryPromptText(" ", "widgets"), "a widget", defaults);
        Action blankNoun = () => _ = new AutosaveRecoveryProfile(
            new AutosaveRecoveryPromptText("TestApp", ""), "a widget", defaults);
        Action blankFallback = () => _ = new AutosaveRecoveryProfile(
            new AutosaveRecoveryPromptText("TestApp", "widgets"), " ", defaults);

        blankProduct.Should().Throw<ArgumentException>();
        blankNoun.Should().Throw<ArgumentException>();
        blankFallback.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ResolveText_UsesDefaultsAndSharedResourceKeys()
    {
        var defaults = Recovery.ResolveText();
        defaults.Title.Should().Be("TestApp - Recover");
        defaults.NoDocumentsMessage.Should().Be("No unsaved widgets were found.");
        defaults.FailureMessageFormat.Should().Be("Could not recover the widget.\n\n{0}");

        var localized = Recovery.ResolveText(key => $"localized:{key}");
        localized.Title.Should().Be("localized:Autosave_Recovery_Title");
        localized.RecoverButton.Should().Be("localized:Autosave_Recovery_Recover_Button");
        localized.SkipButton.Should().Be("localized:Autosave_Recovery_Skip_Button");
        localized.NoDocumentsMessage.Should().Be("localized:Autosave_Recovery_None_Message");
        Recovery.RequiredResourceKeys.Should().Contain("Autosave_Recovery_Title");
    }

    [Fact]
    public void PlanLatest_SelectsNewestAndFallsBackToProfileNoun()
    {
        var store = new AutosaveSnapshotStore(_directory);
        CreateCandidate(store, "older", "2026-08-05T08:00:00Z", "Older");
        var newer = CreateCandidate(store, "newer", "2026-08-05T09:00:00Z", " ");

        var plan = Recovery.PlanLatest(store);

        plan.Should().NotBeNull();
        plan!.Candidate.SnapshotPath.Should().Be(newer.SnapshotPath);
        plan.DisplayName.Should().Be("a widget");
        Recovery.PlanAll(store).Select(p => p.DisplayName).Should().Equal("a widget", "Older");
    }

    [Theory]
    [InlineData(false, false, AutosaveRecoveryDisposition.Keep, true, 0)]
    [InlineData(true, true, AutosaveRecoveryDisposition.Delete, false, 0)]
    [InlineData(true, false, AutosaveRecoveryDisposition.Quarantine, false, 2)]
    public void Complete_AppliesDispositionToCandidateFiles(
        bool accepted,
        bool recovered,
        AutosaveRecoveryDisposition expected,
        bool filesRemain,
        int quarantinedFiles)
    {
        var store = new AutosaveSnapshotStore(_directory);
        var candidate = CreateCandidate(store, "c", "2026-08-05T09:00:00Z", "Draft");

        Recovery.Complete(new AutosaveRecoveryPlan(candidate, "Draft"), accepted, recovered)
            .Should().Be(expected);

        File.Exists(candidate.SnapshotPath).Should().Be(filesRemain);
        File.Exists(candidate.SidecarPath).Should().Be(filesRemain);
        var quarantine = Path.Combine(_directory, "Quarantine");
        (Directory.Exists(quarantine) ? Directory.GetFiles(quarantine).Length : 0)
            .Should().Be(quarantinedFiles);
    }

    [Theory]
    [InlineData(
        AutosaveRecoveryPromptMode.Startup,
        "TestApp found unsaved changes to Alpha from a previous session (3 unsaved widgets found). Recover this one?",
        "TestApp found unsaved changes to Gamma from a previous session. Recover them?")]
    [InlineData(
        AutosaveRecoveryPromptMode.StartupQuotedDisplayName,
        "TestApp found unsaved changes to \"Alpha\" from a previous session (3 unsaved widgets found). Recover this one?",
        "TestApp found unsaved changes to \"Gamma\" from a previous session. Recover them?")]
    [InlineData(
        AutosaveRecoveryPromptMode.Manual,
        "Recover unsaved changes to Alpha? (3 unsaved widgets found.)",
        "Recover unsaved changes to Gamma?")]
    public async Task RunAsync_PreservesPromptModeAndUsesCurrentWindowOnlyForFirstAcceptance(
        AutosaveRecoveryPromptMode promptMode,
        string firstPrompt,
        string lastPrompt)
    {
        var recoveries = new[] { Plan("Alpha"), Plan("Beta"), Plan("Gamma") };
        var responses = new Queue<bool>([false, true, true]);
        var offers = new List<AutosaveRecoveryOffer>();
        var completions = new List<(string DisplayName, bool UseCurrentWindow)>();

        var result = await Recovery.RunAsync(
            recoveries,
            promptMode,
            offer =>
            {
                offers.Add(offer);
                return new ValueTask<bool>(responses.Dequeue());
            },
            (recovery, useCurrentWindow) =>
            {
                completions.Add((recovery.DisplayName, useCurrentWindow));
                return new ValueTask<bool>(recovery.DisplayName == "Gamma");
            });

        result.AnyAccepted.Should().BeTrue();
        result.AnyRecovered.Should().BeTrue();
        offers.Select(offer => offer.RemainingCount).Should().Equal(3, 2, 1);
        offers.Should().OnlyContain(offer => offer.PromptMode == promptMode);
        offers[0].Prompt.Should().Be(firstPrompt);
        offers[2].Prompt.Should().Be(lastPrompt);
        completions.Should().Equal(("Beta", true), ("Gamma", false));
    }

    [Fact]
    public async Task RunAsync_WithNoCandidates_DoesNotInvokeNativePorts()
    {
        var calls = 0;

        var result = await Recovery.RunAsync(
            [],
            AutosaveRecoveryPromptMode.Startup,
            _ => { calls++; return new ValueTask<bool>(true); },
            (_, _) => { calls++; return new ValueTask<bool>(true); });

        result.AnyAccepted.Should().BeFalse();
        result.AnyRecovered.Should().BeFalse();
        calls.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_WhenAcceptedRecoveryFails_ReportsAcceptedWithoutRecovered()
    {
        var result = await Recovery.RunAsync(
            [Plan("Alpha")],
            AutosaveRecoveryPromptMode.Manual,
            _ => new ValueTask<bool>(true),
            (_, _) => new ValueTask<bool>(false));

        result.AnyAccepted.Should().BeTrue();
        result.AnyRecovered.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_DiscardsDeclinedSnapshotOnStartupButKeepsItWhenManual()
    {
        var store = new AutosaveSnapshotStore(_directory);
        var startup = CreateCandidate(store, "startup", "2026-08-05T09:00:00Z", "S");
        var manual = CreateCandidate(store, "manual", "2026-08-05T09:00:00Z", "M");

        await Recovery.RunAsync(
            [new AutosaveRecoveryPlan(startup, "S")],
            AutosaveRecoveryPromptMode.StartupQuotedDisplayName,
            _ => new ValueTask<bool>(false),
            (_, _) => new ValueTask<bool>(true));
        await Recovery.RunAsync(
            [new AutosaveRecoveryPlan(manual, "M")],
            AutosaveRecoveryPromptMode.Manual,
            _ => new ValueTask<bool>(false),
            (_, _) => new ValueTask<bool>(true));

        File.Exists(startup.SnapshotPath).Should().BeFalse();
        File.Exists(manual.SnapshotPath).Should().BeTrue();
    }

    [Fact]
    public void Session_Snapshot_UsesProfileWriterAndGenerationGate()
    {
        var generation = 4;
        var writes = 0;
        var store = new AutosaveSnapshotStore(_directory);
        using var session = new AutosaveApplicationSession<TestDocument>(
            Profile,
            Ports(() => "C:\\Docs\\Draft.test", () => "Draft.test", true, () => generation, () => writes++),
            store,
            "current-session");

        session.Snapshot();
        session.Snapshot();

        writes.Should().Be(1);
        var sidecar = AutosaveSnapshotStore.TryDeserializeSidecar(
            File.ReadAllText(store.GetSidecarPath("current-session")));
        sidecar!.OriginalFilePath.Should().Be("C:\\Docs\\Draft.test");
        sidecar.DisplayName.Should().Be("Draft.test");

        generation++;
        session.Snapshot();
        writes.Should().Be(2);
    }

    [Fact]
    public void Session_PlanAndCompleteRecovery_RestoresLatestAndDeletesOnlyOfferedCandidate()
    {
        var store = new AutosaveSnapshotStore(_directory);
        var older = CreateCandidate(store, "older", "2026-08-10T08:00:00Z", "Older");
        var newer = CreateCandidate(store, "newer", "2026-08-10T09:00:00Z", "Newer", "C:\\Docs\\Newer.test");
        using var session = CreateSession(store);
        string? restoredPath = null;
        string? restoredOriginalPath = null;

        var plan = session.PlanLatestRecovery();
        var recovered = session.CompleteRecovery(
            plan!,
            accepted: true,
            restoreSnapshot: (path, originalPath) =>
            {
                restoredPath = path;
                restoredOriginalPath = originalPath;
                return true;
            });

        recovered.Should().BeTrue();
        plan!.Candidate.SnapshotPath.Should().Be(newer.SnapshotPath);
        restoredPath.Should().Be(newer.SnapshotPath);
        restoredOriginalPath.Should().Be("C:\\Docs\\Newer.test");
        File.Exists(newer.SnapshotPath).Should().BeFalse();
        File.Exists(newer.SidecarPath).Should().BeFalse();
        File.Exists(older.SnapshotPath).Should().BeTrue();
        File.Exists(older.SidecarPath).Should().BeTrue();
        session.PlanRecoveries().Should().ContainSingle()
            .Which.Candidate.SnapshotPath.Should().Be(older.SnapshotPath);
    }

    [Fact]
    public void Session_CompleteRecovery_DeclineKeepsCandidateAndSkipsRestore()
    {
        var store = new AutosaveSnapshotStore(_directory);
        var candidate = CreateCandidate(store, "declined", "2026-08-10T09:00:00Z", "Draft");
        using var session = CreateSession(store);
        var restoreCalled = false;

        var recovered = session.CompleteRecovery(
            session.PlanLatestRecovery()!,
            accepted: false,
            restoreSnapshot: (_, _) => { restoreCalled = true; return true; });

        recovered.Should().BeFalse();
        restoreCalled.Should().BeFalse();
        File.Exists(candidate.SnapshotPath).Should().BeTrue();
        File.Exists(candidate.SidecarPath).Should().BeTrue();
    }

    [Fact]
    public void Session_CompleteRecovery_FailedRestoreQuarantinesCandidate()
    {
        var store = new AutosaveSnapshotStore(_directory);
        var candidate = CreateCandidate(store, "failed", "2026-08-10T09:00:00Z", "Draft");
        using var session = CreateSession(store);

        session.CompleteRecovery(
            session.PlanLatestRecovery()!,
            accepted: true,
            restoreSnapshot: (_, _) => false).Should().BeFalse();

        File.Exists(candidate.SnapshotPath).Should().BeFalse();
        Directory.GetFiles(Path.Combine(_directory, "Quarantine")).Should().HaveCount(2);
    }

    [Fact]
    public void Session_CompleteRecovery_DefaultExceptionPolicyPropagatesAndKeepsCandidate()
    {
        var store = new AutosaveSnapshotStore(_directory);
        var candidate = CreateCandidate(store, "preserved", "2026-08-10T09:00:00Z", "Draft");
        using var session = CreateSession(store);

        Action act = () => session.CompleteRecovery(
            session.PlanLatestRecovery()!,
            accepted: true,
            restoreSnapshot: (_, _) => throw new InvalidOperationException("restore failed"));

        act.Should().Throw<InvalidOperationException>();
        File.Exists(candidate.SnapshotPath).Should().BeTrue();
        File.Exists(candidate.SidecarPath).Should().BeTrue();
    }

    [Fact]
    public void Session_CompleteRecovery_QuarantineExceptionPolicyMovesCandidateAside()
    {
        var store = new AutosaveSnapshotStore(_directory);
        var candidate = CreateCandidate(store, "quarantined", "2026-08-10T09:00:00Z", "Draft");
        using var session = CreateSession(store);

        var recovered = session.CompleteRecovery(
            session.PlanLatestRecovery()!,
            accepted: true,
            restoreSnapshot: (_, _) => throw new InvalidOperationException("restore failed"),
            exceptionPolicy: AutosaveRecoveryRestoreExceptionPolicy.QuarantineCandidate);

        recovered.Should().BeFalse();
        File.Exists(candidate.SnapshotPath).Should().BeFalse();
        Directory.GetFiles(Path.Combine(_directory, "Quarantine")).Should().HaveCount(2);
    }

    [Fact]
    public void Session_CompleteDocumentRecovery_ReadsWithProfileReaderAndAppliesDocument()
    {
        var store = new AutosaveSnapshotStore(_directory);
        var snapshotPath = store.GetSnapshotPath("document");
        WriteDocument(new TestDocument("payload"), snapshotPath);
        var sidecarPath = store.GetSidecarPath("document");
        var sidecar = new AutosaveSidecar
        {
            DisplayName = "Draft",
            OriginalFilePath = "C:\\Docs\\Draft.test",
            SnapshotId = "document",
            TimestampUtc = "2026-08-10T09:00:00Z",
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));
        using var session = CreateSession(store);
        TestDocument? restored = null;
        string? restoredOriginalPath = null;

        var recovered = session.CompleteDocumentRecovery(
            session.PlanLatestRecovery()!,
            accepted: true,
            applyRecoveredDocument: (document, originalPath) =>
            {
                restored = document;
                restoredOriginalPath = originalPath;
            });

        recovered.Should().BeTrue();
        restored.Should().Be(new TestDocument("payload"));
        restoredOriginalPath.Should().Be("C:\\Docs\\Draft.test");
        File.Exists(snapshotPath).Should().BeFalse();
        File.Exists(sidecarPath).Should().BeFalse();
    }

    [Fact]
    public void Session_CompleteCleanExit_DeletesCurrentSessionSnapshot()
    {
        var store = new AutosaveSnapshotStore(_directory);
        var session = new AutosaveApplicationSession<TestDocument>(
            Profile,
            Ports(() => null, () => "Draft", true, () => 1, () => { }),
            store,
            "current-session");
        session.Snapshot();

        session.CompleteCleanExit();

        File.Exists(store.GetSnapshotPath("current-session")).Should().BeFalse();
        File.Exists(store.GetSidecarPath("current-session")).Should().BeFalse();
    }

    private sealed record TestDocument(string Text);

    private static AutosaveRecoveryPlan Plan(string displayName) =>
        new(
            new AutosaveRecoveryCandidate(
                displayName + ".test",
                displayName + ".sidecar.json",
                new AutosaveSidecar { DisplayName = displayName }),
            displayName);

    private static AutosaveDocumentPorts<TestDocument> Ports(
        Func<string?> originalPath,
        Func<string> displayName,
        bool dirty,
        Func<int> generation,
        Action onWrite) =>
        new(
            originalPath,
            displayName,
            () => dirty,
            generation,
            write =>
            {
                onWrite();
                write(new TestDocument("current"));
            });

    private static AutosaveApplicationSession<TestDocument> CreateSession(AutosaveSnapshotStore store) =>
        new(Profile, Ports(() => null, () => "Current", false, () => 0, () => { }), store, "current-session");

    private static void WriteDocument(TestDocument document, string path)
    {
        // The coordinator hands the writer a pre-created temp file, so overwrite rather than CreateNew.
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        archive.CreateEntry("[Content_Types].xml");
        using var writer = new StreamWriter(archive.CreateEntry("payload.txt").Open());
        writer.Write(document.Text);
    }

    private static TestDocument ReadDocument(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry("payload.txt")!.Open());
        return new TestDocument(reader.ReadToEnd());
    }

    private static AutosaveRecoveryCandidate CreateCandidate(
        AutosaveSnapshotStore store,
        string snapshotId,
        string timestampUtc,
        string displayName,
        string? originalPath = null)
    {
        var snapshotPath = store.GetSnapshotPath(snapshotId);
        WriteDocument(new TestDocument(snapshotId), snapshotPath);

        var sidecarPath = store.GetSidecarPath(snapshotId);
        var sidecar = new AutosaveSidecar
        {
            DisplayName = displayName,
            OriginalFilePath = originalPath,
            SnapshotId = snapshotId,
            TimestampUtc = timestampUtc,
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));

        return new AutosaveRecoveryCandidate(snapshotPath, sidecarPath, sidecar);
    }
}
