using System.IO;
using System.IO.Compression;
using System.Windows;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Wpf.Tests;

/// <summary>
/// Behaviour of the shared <see cref="WpfAutosaveHost{TDocument}"/> that FreeW's and FreeP's
/// <c>AutosaveCoordinator</c> both bind: startup vs manual prompt mode, the explicit-document
/// routing, the dirty gate, and the per-flow restore-exception policy. The per-app test projects
/// only prove their binding (ports, restore delegate, gate).
/// </summary>
public sealed class WpfAutosaveHostTests : IDisposable
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
        "WpfAutosaveHostTests_" + Guid.NewGuid().ToString("N"));

    private readonly AutosaveSnapshotStore _store;

    public WpfAutosaveHostTests()
    {
        Directory.CreateDirectory(_directory);
        _store = new AutosaveSnapshotStore(_directory);
    }

    private readonly List<WpfAutosaveHost<TestDocument>> _hosts = [];

    public void Dispose()
    {
        foreach (var host in _hosts)
            host.Session.Dispose(); // releases the snapshot ownership lock file

        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void StartupOffer_FreshWindow_RestoresFirstAcceptedIntoCurrentWindowAndConsumesTheSnapshot()
    {
        var candidate = CreateCandidate("fresh", "Alpha");
        var dialogs = new RecordingDialogs { StartupAnswers = new Queue<bool>([true]) };
        var restored = new List<(string Snapshot, string? Original)>();
        var host = NewHost(dialogs, hasExplicitDocument: false, restore: (snapshot, original) =>
        {
            restored.Add((snapshot, original));
            return true;
        });

        host.OfferRecovery(owner: null).Should().BeTrue();

        restored.Should().ContainSingle().Which.Snapshot.Should().Be(candidate.SnapshotPath);
        _store.EnumerateCandidates().Should().BeEmpty("an accepted, restored candidate is deleted");
        dialogs.Events.Should().ContainSingle().Which.Should().StartWith("startup:");
    }

    [Fact]
    public void StartupOffer_WindowWithExplicitDocument_NeverRestoresIntoTheCurrentWindow()
    {
        CreateCandidate("explicit", "Alpha");
        var dialogs = new RecordingDialogs { StartupAnswers = new Queue<bool>([true]) };
        var restoredIntoCurrent = 0;
        var newWindow = new List<string>();
        var host = NewHost(
            dialogs,
            hasExplicitDocument: true,
            restore: (_, _) =>
            {
                restoredIntoCurrent++;
                return true;
            },
            recoverInNewWindow: candidate =>
            {
                newWindow.Add(candidate.SnapshotPath);
                return true;
            });

        host.OfferRecovery(owner: null).Should().BeTrue();

        restoredIntoCurrent.Should().Be(0, "a just-opened, not-yet-dirty document must not be replaced");
        newWindow.Should().ContainSingle();
    }

    [Fact]
    public void StartupOffer_RestoreThatThrows_QuarantinesTheCandidate()
    {
        var candidate = CreateCandidate("poison", "Alpha");
        var dialogs = new RecordingDialogs { StartupAnswers = new Queue<bool>([true]) };
        var host = NewHost(
            dialogs,
            hasExplicitDocument: false,
            restore: (_, _) => throw new InvalidDataException("corrupt snapshot"));

        host.OfferRecovery(owner: null);

        File.Exists(candidate.SnapshotPath).Should().BeFalse();
        Directory.GetFiles(Path.Combine(_directory, "Quarantine")).Should().NotBeEmpty(
            "startup recovery quarantines a snapshot whose restore throws so it cannot crash-loop the next launch");
    }

    [Fact]
    public void ManualRecovery_DeclinedDirtyGate_KeepsTheCandidateAndNeverRestores()
    {
        var candidate = CreateCandidate("declined", "Alpha");
        var dialogs = new RecordingDialogs { ManualAnswers = new Queue<bool>([true]) };
        var gateCalls = 0;
        var restored = 0;
        var host = NewHost(
            dialogs,
            hasExplicitDocument: false,
            restore: (_, _) =>
            {
                restored++;
                return true;
            },
            confirm: () =>
            {
                gateCalls++;
                return false;
            });

        host.RecoverManually(owner: null).Should().BeFalse();

        gateCalls.Should().Be(1);
        restored.Should().Be(0);
        File.Exists(candidate.SnapshotPath).Should().BeTrue("declining the gate is not a failed restore");
        Directory.Exists(Path.Combine(_directory, "Quarantine")).Should().BeFalse();
    }

    [Fact]
    public void ManualRecovery_AcceptedGate_RestoresIntoCurrentWindow()
    {
        CreateCandidate("accepted", "Alpha");
        var dialogs = new RecordingDialogs { ManualAnswers = new Queue<bool>([true]) };
        var restored = 0;
        var host = NewHost(
            dialogs,
            hasExplicitDocument: true, // the explicit-document rule is a STARTUP-only concern
            restore: (_, _) =>
            {
                restored++;
                return true;
            });

        host.RecoverManually(owner: null).Should().BeTrue();

        restored.Should().Be(1);
        _store.EnumerateCandidates().Should().BeEmpty();
    }

    [Fact]
    public void ManualRecovery_RestoreThatThrows_SurfacesTheFailureAndPreservesTheCandidate()
    {
        var candidate = CreateCandidate("broken", "Alpha");
        var dialogs = new RecordingDialogs { ManualAnswers = new Queue<bool>([true]) };
        var host = NewHost(
            dialogs,
            hasExplicitDocument: false,
            restore: (_, _) => throw new InvalidDataException("corrupt snapshot"));

        host.RecoverManually(owner: null).Should().BeFalse();

        dialogs.Events.Should().Contain(e => e.StartsWith("failure:", StringComparison.Ordinal));
        File.Exists(candidate.SnapshotPath).Should().BeTrue(
            "the user-invoked command keeps the candidate on offer when the restore throws");
        Directory.Exists(Path.Combine(_directory, "Quarantine")).Should().BeFalse();
    }

    [Fact]
    public void ManualRecovery_WithNothingToRecover_SaysSoUsingTheProfileText()
    {
        var dialogs = new RecordingDialogs();
        var host = NewHost(dialogs, hasExplicitDocument: false, restore: (_, _) => true);

        host.RecoverManually(owner: null).Should().BeFalse();

        dialogs.Events.Should().Equal("none:No unsaved widgets were found.|TestApp - Recover");
    }

    [Fact]
    public void StartupOffer_PromptsInStartupMode()
    {
        var expectedPlan = new AutosaveRecoveryPlan(CreateCandidate("mode", "Alpha"), "Alpha");

        var dialogs = new RecordingDialogs { StartupAnswers = new Queue<bool>([false]) };
        NewHost(dialogs, hasExplicitDocument: false, restore: (_, _) => true).OfferRecovery(owner: null);

        dialogs.Events.Should().ContainSingle().Which.Should().Be(
            "startup:" + Recovery.CreateOffer(expectedPlan, 1, AutosaveRecoveryPromptMode.Startup).Prompt
            + "|TestApp - Recover");
    }

    [Fact]
    public void ManualRecovery_PromptsInManualMode()
    {
        var expectedPlan = new AutosaveRecoveryPlan(CreateCandidate("mode", "Alpha"), "Alpha");

        var dialogs = new RecordingDialogs { ManualAnswers = new Queue<bool>([false]) };
        NewHost(dialogs, hasExplicitDocument: false, restore: (_, _) => true).RecoverManually(owner: null);

        dialogs.Events.Should().ContainSingle().Which.Should().Be(
            "manual:" + Recovery.CreateOffer(expectedPlan, 1, AutosaveRecoveryPromptMode.Manual).Prompt
            + "|TestApp - Recover");
    }

    [Fact]
    public void LocalizedText_IsResolvedThroughTheSuppliedLocalizer()
    {
        var dialogs = new RecordingDialogs();
        var host = NewHost(
            dialogs,
            hasExplicitDocument: false,
            restore: (_, _) => true,
            getText: key => "L:" + key);

        host.RecoverManually(owner: null);

        dialogs.Events.Should().Equal("none:L:Autosave_Recovery_None_Message|L:Autosave_Recovery_Title");
    }

    [Fact]
    public void TryEmergencySnapshot_WritesTheDirtyDocumentAndStopRemovesTheSnapshot()
    {
        var host = NewHost(new RecordingDialogs(), hasExplicitDocument: false, restore: (_, _) => true);
        var snapshotPath = _store.GetSnapshotPath(host.Session.SnapshotId);

        host.TryEmergencySnapshot();
        File.Exists(snapshotPath).Should().BeTrue("the emergency path snapshots a dirty document");

        host.Stop();
        File.Exists(snapshotPath).Should().BeFalse("a clean exit deletes the current-session snapshot");
    }

    private WpfAutosaveHost<TestDocument> NewHost(
        RecordingDialogs dialogs,
        bool hasExplicitDocument,
        Func<string, string?, bool> restore,
        Func<bool>? confirm = null,
        Func<AutosaveRecoveryCandidate, bool>? recoverInNewWindow = null,
        Func<string, string?>? getText = null)
    {
        var ports = new AutosaveDocumentPorts<TestDocument>(
            GetOriginalFilePath: () => null,
            GetDisplayName: () => "Current",
            GetIsDirty: () => true,
            GetDirtyGeneration: () => 1,
            ExecuteWithDocument: write => write(new TestDocument("live")));

        var host = new WpfAutosaveHost<TestDocument>(
            Profile,
            ports,
            new WpfAutosaveHostCallbacks<TestDocument>(
                () => hasExplicitDocument,
                AutosaveRecoveryRestore<TestDocument>.FromSnapshotPath(restore),
                confirm ?? (() => true),
                recoverInNewWindow),
            getText,
            sessionPorts => new AutosaveApplicationSession<TestDocument>(Profile, sessionPorts, _store),
            dialogs);
        _hosts.Add(host);
        return host;
    }

    private AutosaveRecoveryCandidate CreateCandidate(string snapshotId, string displayName)
    {
        var snapshotPath = _store.GetSnapshotPath(snapshotId);
        WriteDocument(new TestDocument(snapshotId), snapshotPath);

        var sidecarPath = _store.GetSidecarPath(snapshotId);
        var sidecar = new AutosaveSidecar
        {
            DisplayName = displayName,
            SnapshotId = snapshotId,
            TimestampUtc = "2026-08-10T09:00:00Z",
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));
        return new AutosaveRecoveryCandidate(snapshotPath, sidecarPath, sidecar);
    }

    private sealed record TestDocument(string Text);

    private static void WriteDocument(TestDocument document, string path)
    {
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

    private sealed class RecordingDialogs : IWpfAutosaveRecoveryDialogs
    {
        public Queue<bool> StartupAnswers { get; init; } = new();

        public Queue<bool> ManualAnswers { get; init; } = new();

        public List<string> Events { get; } = [];

        public bool AskStartup(Window? owner, string prompt, string title)
        {
            Events.Add($"startup:{prompt}|{title}");
            return StartupAnswers.Dequeue();
        }

        public bool AskManual(Window? owner, string prompt, string title)
        {
            Events.Add($"manual:{prompt}|{title}");
            return ManualAnswers.Dequeue();
        }

        public void ShowNoCandidates(Window? owner, string message, string title) =>
            Events.Add($"none:{message}|{title}");

        public void ShowFailure(Window? owner, string message, string title) =>
            Events.Add($"failure:{message}|{title}");
    }
}
