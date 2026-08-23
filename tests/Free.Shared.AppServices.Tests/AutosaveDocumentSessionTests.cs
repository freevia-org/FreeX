namespace Free.Shared.AppServices.Tests;

public sealed class AutosaveDocumentSessionTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "SharedAutosaveDocumentSessionTests_" + Guid.NewGuid().ToString("N"));

    public AutosaveDocumentSessionTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Snapshot_UsesInjectedDocumentWriterAndSharedGenerationGate()
    {
        var generation = 4;
        var writes = 0;
        var document = new TestDocument("first");
        var store = new AutosaveSnapshotStore(_directory);
        using var session = CreateSession(
            store,
            "writer",
            () => document,
            () => generation,
            writeObserver: () => writes++);

        session.Interval.Should().Be(TimeSpan.FromSeconds(45));
        session.Snapshot();
        session.Snapshot();

        writes.Should().Be(1);
        ReadDocument(store.GetSnapshotPath("writer")).Should().Be(new TestDocument("first"));

        document = new TestDocument("second");
        generation++;
        session.Snapshot();

        writes.Should().Be(2);
        ReadDocument(store.GetSnapshotPath("writer")).Should().Be(new TestDocument("second"));
    }

    [Fact]
    public void CompleteDocumentRecovery_UsesInjectedReaderAndPreservesOriginalPath()
    {
        var store = new AutosaveSnapshotStore(_directory);
        var writer = CreateSession(
            store,
            "candidate",
            () => new TestDocument("recovered content"),
            () => 1,
            originalPath: "C:\\Docs\\Draft.test");
        writer.Snapshot();
        writer.Dispose();

        using var reader = CreateSession(
            store,
            "reader",
            () => new TestDocument("current"),
            () => 0,
            isDirty: false);
        var plan = AutosaveRecoveryPlannerCore.PlanLatest(
            store,
            "an untitled test",
            static (candidate, displayName) => new TestPlan(candidate, displayName));

        TestDocument? recovered = null;
        string? recoveredPath = null;
        var result = reader.CompleteDocumentRecovery(
            plan!,
            accepted: true,
            (document, originalPath) =>
            {
                recovered = document;
                recoveredPath = originalPath;
            });

        result.Should().BeTrue();
        recovered.Should().Be(new TestDocument("recovered content"));
        recoveredPath.Should().Be("C:\\Docs\\Draft.test");
        File.Exists(plan!.Candidate.SnapshotPath).Should().BeFalse();
        File.Exists(plan.Candidate.SidecarPath).Should().BeFalse();
    }

    [Fact]
    public void CompleteRecovery_QuarantinePolicyConvertsRestoreExceptionToFailedRecovery()
    {
        var store = new AutosaveSnapshotStore(_directory);
        var candidate = CreateCandidate(store, "failed");
        var plan = new TestPlan(candidate, "Failed");
        using var session = CreateSession(
            store,
            "reader",
            () => new TestDocument("current"),
            () => 0,
            isDirty: false);

        var recovered = session.CompleteRecovery(
            plan,
            accepted: true,
            (_, _) => throw new InvalidOperationException("broken snapshot"),
            AutosaveRecoveryRestoreExceptionPolicy.QuarantineCandidate);

        recovered.Should().BeFalse();
        File.Exists(candidate.SnapshotPath).Should().BeFalse();
        File.Exists(candidate.SidecarPath).Should().BeFalse();
        Directory.GetFiles(Path.Combine(_directory, "Quarantine")).Should().HaveCount(2);
    }

    [Fact]
    public void CompleteCleanExit_DeletesCurrentSnapshotAndReleasesItsOwnership()
    {
        var store = new AutosaveSnapshotStore(_directory);
        var session = CreateSession(
            store,
            "clean-exit",
            () => new TestDocument("dirty"),
            () => 1);
        session.Snapshot();

        session.CompleteCleanExit();

        File.Exists(store.GetSnapshotPath("clean-exit")).Should().BeFalse();
        File.Exists(store.GetSidecarPath("clean-exit")).Should().BeFalse();
        File.Exists(store.GetLockPath("clean-exit")).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static AutosaveDocumentSession<TestDocument> CreateSession(
        AutosaveSnapshotStore store,
        string snapshotId,
        Func<TestDocument> getDocument,
        Func<int> getGeneration,
        Action? writeObserver = null,
        string? originalPath = null,
        bool isDirty = true) =>
        new(
            new AutosaveDocumentPorts<TestDocument>(
                () => originalPath,
                () => "Draft.test",
                () => isDirty,
                getGeneration,
                write => write(getDocument())),
            new AutosaveDocumentSessionOptions<TestDocument>(
                TimeSpan.FromSeconds(45),
                (document, path) =>
                {
                    writeObserver?.Invoke();
                    File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(document));
                },
                ReadDocument),
            store,
            snapshotId);

    private static AutosaveRecoveryCandidate CreateCandidate(
        AutosaveSnapshotStore store,
        string snapshotId)
    {
        var snapshotPath = store.GetSnapshotPath(snapshotId);
        File.WriteAllText(snapshotPath, "content");
        var sidecarPath = store.GetSidecarPath(snapshotId);
        var sidecar = new AutosaveSidecar
        {
            SnapshotId = snapshotId,
            DisplayName = "Failed",
            TimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));
        return new AutosaveRecoveryCandidate(snapshotPath, sidecarPath, sidecar);
    }

    private sealed record TestDocument(string Content);

    private static TestDocument ReadDocument(string path) =>
        System.Text.Json.JsonSerializer.Deserialize<TestDocument>(File.ReadAllText(path))!;

    private sealed record TestPlan(
        AutosaveRecoveryCandidate Candidate,
        string DisplayName) : IAutosaveRecoveryPlan;
}
