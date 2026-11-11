using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Avalonia.Tests;

/// <summary>
/// Behaviour of the shared <see cref="AvaloniaAutosaveHost{TDocument}"/> that FreeW's and FreeP's
/// <c>AutosaveAdapter</c> both bind: startup vs manual prompt mode, the explicit-document routing,
/// the dirty gate, the restore-exception policy, the emergency fan-out and the clean-exit stop. The
/// per-app test projects only prove their binding (ports, restore delegate, gate).
/// </summary>
public sealed class AvaloniaAutosaveHostTests : IDisposable
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ShellHeadlessApp).Assembly);

    private static readonly AutosaveRecoveryProfile Recovery = new(
        new AutosaveRecoveryPromptText("TestApp", "widgets"),
        "a widget",
        new AutosaveRecoveryTextDefaults(
            "TestApp - Recover",
            "Recover",
            "Skip",
            "No unsaved widgets were found.",
            "Could not recover the widget.\n\n{0}"));

    private static readonly AutosaveApplicationProfile<HostTestDocument> Profile = new(
        Recovery,
        Interval: TimeSpan.FromSeconds(45),
        WriteSnapshot: WriteDocument,
        ReadSnapshot: ReadDocument);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "AvaloniaAutosaveHostTests_" + Guid.NewGuid().ToString("N"));

    private readonly AutosaveSnapshotStore _store;
    private readonly List<AvaloniaAutosaveHost<HostTestDocument>> _hosts = [];

    public AvaloniaAutosaveHostTests()
    {
        Directory.CreateDirectory(_directory);
        _store = new AutosaveSnapshotStore(_directory);
    }

    public void Dispose()
    {
        foreach (var host in _hosts)
            host.Dispose(); // releases the snapshot ownership lock file

        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public Task StartupOffer_FreshWindow_RestoresIntoCurrentWindowInQuotedStartupMode() => OnUiThread(async owner =>
    {
        var candidate = CreateCandidate("fresh", "Alpha");
        var prompts = new List<string>();
        var applied = new List<(HostTestDocument Document, string? Original)>();
        var host = NewHost(
            hasExplicitDocument: false,
            restore: AutosaveRecoveryRestore<HostTestDocument>.FromDocument(
                (document, original) => applied.Add((document, original))),
            promptOverride: message =>
            {
                prompts.Add(message);
                return true;
            });

        (await host.OfferRecoveryAsync(owner)).Should().BeTrue();

        applied.Should().ContainSingle().Which.Document.Should().Be(new HostTestDocument("fresh"));
        prompts.Should().Equal(Recovery.CreateOffer(
            new AutosaveRecoveryPlan(candidate, "Alpha"),
            1,
            AutosaveRecoveryPromptMode.StartupQuotedDisplayName).Prompt);
        _store.EnumerateCandidates().Should().BeEmpty("an accepted, restored candidate is deleted");
    });

    [Fact]
    public Task StartupOffer_WindowWithExplicitDocument_NeverRestoresIntoTheCurrentWindow() => OnUiThread(async owner =>
    {
        CreateCandidate("explicit", "Alpha");
        var restoredIntoCurrent = 0;
        var newWindow = new List<string>();
        var host = NewHost(
            hasExplicitDocument: true,
            restore: AutosaveRecoveryRestore<HostTestDocument>.FromDocument((_, _) => restoredIntoCurrent++),
            promptOverride: _ => true,
            recoverInNewWindowAsync: candidate =>
            {
                newWindow.Add(candidate.SnapshotPath);
                return Task.FromResult(true);
            });

        (await host.OfferRecoveryAsync(owner)).Should().BeTrue();

        restoredIntoCurrent.Should().Be(0, "a just-opened, not-yet-dirty document must not be replaced");
        newWindow.Should().ContainSingle();
    });

    [Fact]
    public Task StartupOffer_WithoutANewWindowCallback_TreatsTheRecoveryAsFailed() => OnUiThread(async owner =>
    {
        var candidate = CreateCandidate("nowindow", "Alpha");
        var host = NewHost(
            hasExplicitDocument: true,
            restore: AutosaveRecoveryRestore<HostTestDocument>.FromDocument((_, _) => { }),
            promptOverride: _ => true);

        await host.OfferRecoveryAsync(owner);

        Directory.GetFiles(Path.Combine(_directory, "Quarantine")).Should().NotBeEmpty(
            "an accepted candidate that could not be recovered anywhere is quarantined, not retried forever");
        File.Exists(candidate.SnapshotPath).Should().BeFalse();
    });

    [Fact]
    public Task StartupOffer_RestoreThatThrows_QuarantinesTheCandidate() => OnUiThread(async owner =>
    {
        var candidate = CreateCandidate("poison", "Alpha");
        var host = NewHost(
            hasExplicitDocument: false,
            restore: AutosaveRecoveryRestore<HostTestDocument>.FromSnapshotPath(
                (_, _) => throw new InvalidDataException("corrupt snapshot")),
            promptOverride: _ => true);

        await host.OfferRecoveryAsync(owner);

        File.Exists(candidate.SnapshotPath).Should().BeFalse();
        Directory.GetFiles(Path.Combine(_directory, "Quarantine")).Should().NotBeEmpty(
            "a snapshot whose restore throws is quarantined so it cannot crash-loop the next launch");
    });

    [Fact]
    public Task ManualRecovery_DeclinedDirtyGate_KeepsTheCandidateAndNeverRestores() => OnUiThread(async owner =>
    {
        var candidate = CreateCandidate("declined", "Alpha");
        var gateCalls = 0;
        var restored = 0;
        var host = NewHost(
            hasExplicitDocument: false,
            restore: AutosaveRecoveryRestore<HostTestDocument>.FromDocument((_, _) => restored++),
            promptOverride: _ => true,
            confirmDiscardOrSaveAsync: () =>
            {
                gateCalls++;
                return Task.FromResult(false);
            });

        (await host.RecoverManuallyAsync(owner)).Should().BeFalse();

        gateCalls.Should().Be(1);
        restored.Should().Be(0);
        File.Exists(candidate.SnapshotPath).Should().BeTrue("declining the gate is not a failed restore");
        Directory.Exists(Path.Combine(_directory, "Quarantine")).Should().BeFalse();
    });

    [Fact]
    public Task ManualRecovery_AcceptedGate_RestoresInManualModeEvenWithAnExplicitDocument() => OnUiThread(async owner =>
    {
        var candidate = CreateCandidate("accepted", "Alpha");
        var prompts = new List<string>();
        var restored = 0;
        var host = NewHost(
            hasExplicitDocument: true, // the explicit-document rule is a STARTUP-only concern
            restore: AutosaveRecoveryRestore<HostTestDocument>.FromDocument((_, _) => restored++),
            promptOverride: message =>
            {
                prompts.Add(message);
                return true;
            },
            confirmDiscardOrSaveAsync: () => Task.FromResult(true));

        (await host.RecoverManuallyAsync(owner)).Should().BeTrue();

        restored.Should().Be(1);
        prompts.Should().Equal(Recovery.CreateOffer(
            new AutosaveRecoveryPlan(candidate, "Alpha"),
            1,
            AutosaveRecoveryPromptMode.Manual).Prompt);
    });

    [Fact]
    public Task ManualRecovery_WithoutADirtyGate_RestoresDirectly() => OnUiThread(async owner =>
    {
        CreateCandidate("nogate", "Alpha");
        var restored = 0;
        var host = NewHost(
            hasExplicitDocument: false,
            restore: AutosaveRecoveryRestore<HostTestDocument>.FromDocument((_, _) => restored++),
            promptOverride: _ => true);

        (await host.RecoverManuallyAsync(owner)).Should().BeTrue();

        restored.Should().Be(1);
    });

    [Fact]
    public Task ManualRecovery_RestoreThatThrows_QuarantinesLikeStartup() => OnUiThread(async owner =>
    {
        var candidate = CreateCandidate("broken", "Alpha");
        var host = NewHost(
            hasExplicitDocument: false,
            restore: AutosaveRecoveryRestore<HostTestDocument>.FromSnapshotPath(
                (_, _) => throw new InvalidDataException("corrupt snapshot")),
            promptOverride: _ => true);

        (await host.RecoverManuallyAsync(owner)).Should().BeFalse();

        File.Exists(candidate.SnapshotPath).Should().BeFalse();
        Directory.GetFiles(Path.Combine(_directory, "Quarantine")).Should().NotBeEmpty();
    });

    [Fact]
    public Task EmergencyFanOut_RegistersLiveHostsAndSnapshotsEachOne() => OnUiThread(async _ =>
    {
        var before = AvaloniaAutosaveHost<HostTestDocument>.ActiveCount;
        var first = NewHost(hasExplicitDocument: false, restore: NoRestore());
        var second = NewHost(hasExplicitDocument: false, restore: NoRestore());
        AvaloniaAutosaveHost<HostTestDocument>.ActiveCount.Should().Be(before + 2);

        AvaloniaAutosaveHost<HostTestDocument>.TrySnapshotAll();

        File.Exists(_store.GetSnapshotPath(first.Session.SnapshotId)).Should().BeTrue();
        File.Exists(_store.GetSnapshotPath(second.Session.SnapshotId)).Should().BeTrue();

        first.Dispose();
        AvaloniaAutosaveHost<HostTestDocument>.ActiveCount.Should().Be(before + 1);
        await Task.CompletedTask;
    });

    [Fact]
    public Task StopAsync_RemovesTheCurrentSessionSnapshot() => OnUiThread(async _ =>
    {
        var host = NewHost(hasExplicitDocument: false, restore: NoRestore());
        var snapshotPath = _store.GetSnapshotPath(host.Session.SnapshotId);

        host.TryEmergencySnapshot();
        File.Exists(snapshotPath).Should().BeTrue("the emergency path snapshots a dirty document");

        await host.StopAsync();
        File.Exists(snapshotPath).Should().BeFalse("a clean exit deletes the current-session snapshot");
    });

    [Fact]
    public Task Constructor_UsesTheSuppliedSessionFactory() => OnUiThread(async _ =>
    {
        AutosaveApplicationSession<HostTestDocument>? created = null;
        var host = NewHost(
            hasExplicitDocument: false,
            restore: NoRestore(),
            onSessionCreated: session => created = session);

        host.Session.Should().BeSameAs(created);
        await Task.CompletedTask;
    });

    private AvaloniaAutosaveHost<HostTestDocument> NewHost(
        bool hasExplicitDocument,
        AutosaveRecoveryRestore<HostTestDocument> restore,
        Func<string, bool?>? promptOverride = null,
        Func<AutosaveRecoveryCandidate, Task<bool>>? recoverInNewWindowAsync = null,
        Func<Task<bool>>? confirmDiscardOrSaveAsync = null,
        Action<AutosaveApplicationSession<HostTestDocument>>? onSessionCreated = null)
    {
        var ports = new AutosaveDocumentPorts<HostTestDocument>(
            GetOriginalFilePath: () => null,
            GetDisplayName: () => "Current",
            GetIsDirty: () => true,
            GetDirtyGeneration: () => 1,
            ExecuteWithDocument: write => write(new HostTestDocument("live")));

        var host = new AvaloniaAutosaveHost<HostTestDocument>(
            Profile,
            ports,
            new AvaloniaAutosaveHostCallbacks<HostTestDocument>(
                () => hasExplicitDocument,
                restore,
                recoverInNewWindowAsync,
                confirmDiscardOrSaveAsync,
                promptOverride),
            getText: null,
            sessionFactory: sessionPorts =>
            {
                var session = new AutosaveApplicationSession<HostTestDocument>(Profile, sessionPorts, _store);
                onSessionCreated?.Invoke(session);
                return session;
            });
        _hosts.Add(host);
        return host;
    }

    private static AutosaveRecoveryRestore<HostTestDocument> NoRestore() =>
        AutosaveRecoveryRestore<HostTestDocument>.FromDocument((_, _) => { });

    private AutosaveRecoveryCandidate CreateCandidate(string snapshotId, string displayName)
    {
        var snapshotPath = _store.GetSnapshotPath(snapshotId);
        WriteDocument(new HostTestDocument(snapshotId), snapshotPath);

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

    private static Task OnUiThread(Func<Window, Task> action) => Session.Dispatch(async () =>
    {
        var owner = new Window();
        await action(owner);
        return true;
    }, CancellationToken.None);

    private sealed record HostTestDocument(string Text);

    private static void WriteDocument(HostTestDocument document, string path)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        archive.CreateEntry("[Content_Types].xml");
        using var writer = new StreamWriter(archive.CreateEntry("payload.txt").Open());
        writer.Write(document.Text);
    }

    private static HostTestDocument ReadDocument(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry("payload.txt")!.Open());
        return new HostTestDocument(reader.ReadToEnd());
    }
}
