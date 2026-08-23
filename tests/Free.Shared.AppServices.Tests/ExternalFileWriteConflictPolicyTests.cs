namespace Free.Shared.AppServices.Tests;

public sealed class ExternalFileWriteConflictPolicyTests
{
    private static readonly DateTime Expected =
        new(2026, 8, 23, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SelectExpectedLastWriteTimeUtc_GuardsOnlyTheCurrentTarget()
    {
        ExternalFileWriteConflictPolicy.SelectExpectedLastWriteTimeUtc(
                "C:/docs/report.fx",
                "C:/docs/report.fx",
                Expected)
            .Should().Be(Expected);
        ExternalFileWriteConflictPolicy.SelectExpectedLastWriteTimeUtc(
                "C:/docs/report.fx",
                "C:/docs/copy.fx",
                Expected)
            .Should().BeNull();
    }

    [Fact]
    public void Prepare_WithoutExpectedVersion_DoesNotTouchTheFileSystemOrPrompt()
    {
        var result = ExternalFileWriteConflictPolicy.Prepare(
            "report.fx",
            expectedLastWriteTimeUtc: null,
            confirmOverwrite: _ => throw new InvalidOperationException("prompt must not run"),
            fileExists: _ => throw new InvalidOperationException("filesystem must not run"),
            getLastWriteTimeUtc: _ => throw new InvalidOperationException("filesystem must not run"));

        result.CanWrite.Should().BeTrue();
        result.ExpectedLastWriteTimeUtc.Should().BeNull();
    }

    [Fact]
    public void Prepare_UnchangedTarget_PreservesBaselineWithoutPrompting()
    {
        var readCount = 0;

        var result = ExternalFileWriteConflictPolicy.Prepare(
            "report.fx",
            Expected,
            confirmOverwrite: _ => throw new InvalidOperationException("prompt must not run"),
            fileExists: _ => true,
            getLastWriteTimeUtc: _ =>
            {
                readCount++;
                return Expected;
            });

        result.CanWrite.Should().BeTrue();
        result.ExpectedLastWriteTimeUtc.Should().Be(Expected);
        readCount.Should().Be(1);
    }

    [Fact]
    public void Prepare_MissingTarget_RetainsBaselineForTheFinalRaceRecheck()
    {
        var result = ExternalFileWriteConflictPolicy.Prepare(
            "report.fx",
            Expected,
            confirmOverwrite: _ => throw new InvalidOperationException("prompt must not run"),
            fileExists: _ => false,
            getLastWriteTimeUtc: _ => throw new InvalidOperationException("timestamp must not be read"));

        result.CanWrite.Should().BeTrue();
        result.ExpectedLastWriteTimeUtc.Should().Be(
            Expected,
            "a file created after preparation must still conflict with the prior observation");
    }

    [Fact]
    public void Prepare_ChangedTarget_DeclinesSafelyAndReadsObservedVersionOnce()
    {
        var observed = Expected.AddMinutes(1);
        var readCount = 0;
        string? promptedPath = null;

        var result = ExternalFileWriteConflictPolicy.Prepare(
            "report.fx",
            Expected,
            confirmOverwrite: path =>
            {
                promptedPath = path;
                return false;
            },
            fileExists: _ => true,
            getLastWriteTimeUtc: _ =>
            {
                readCount++;
                return observed;
            });

        result.Outcome.Should().Be(ExternalFileWritePreparationOutcome.OverwriteDeclined);
        result.CanWrite.Should().BeFalse();
        result.ExpectedLastWriteTimeUtc.Should().Be(Expected);
        promptedPath.Should().Be("report.fx");
        readCount.Should().Be(1);
    }

    [Fact]
    public async Task PrepareAsync_AcceptedChange_RebasesToTheVersionShownByThePrompt()
    {
        var observed = Expected.AddMinutes(1);
        var readCount = 0;

        var result = await ExternalFileWriteConflictPolicy.PrepareAsync(
            "report.fx",
            Expected,
            (path, token) =>
            {
                path.Should().Be("report.fx");
                token.Should().Be(CancellationToken.None);
                return ValueTask.FromResult(true);
            },
            fileExists: _ => true,
            getLastWriteTimeUtc: _ =>
            {
                readCount++;
                return observed;
            });

        result.CanWrite.Should().BeTrue();
        result.ExpectedLastWriteTimeUtc.Should().Be(observed);
        readCount.Should().Be(1, "the baseline must match the version the user accepted");
    }

    [Fact]
    public async Task PrepareAsync_ChangedTargetWithoutPrompt_DefaultsToDecline()
    {
        var result = await ExternalFileWriteConflictPolicy.PrepareAsync(
            "report.fx",
            Expected,
            confirmOverwriteAsync: null,
            fileExists: _ => true,
            getLastWriteTimeUtc: _ => Expected.AddMinutes(1));

        result.Outcome.Should().Be(ExternalFileWritePreparationOutcome.OverwriteDeclined);
    }

    [Fact]
    public async Task PrepareAsync_CanceledBeforeObservation_DoesNotTouchTheFileSystem()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = async () => await ExternalFileWriteConflictPolicy.PrepareAsync(
            "report.fx",
            Expected,
            confirmOverwriteAsync: null,
            cancellationToken: cancellation.Token,
            fileExists: _ => throw new InvalidOperationException("filesystem must not run"));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void ThrowIfChangedSince_UsesProductExceptionFactoryOnlyForAConflict()
    {
        var exception = new IOException("product conflict");

        var act = () => ExternalFileWriteConflictPolicy.ThrowIfChangedSince(
            "report.fx",
            Expected,
            _ => exception,
            fileExists: _ => true,
            getLastWriteTimeUtc: _ => Expected.AddMinutes(1));

        act.Should().Throw<IOException>().Which.Should().BeSameAs(exception);
    }
}

public sealed class SavePersistenceDedupAdoptionTests
{
    [Fact]
    public void AllThreeAppsDelegateExternalWritePreparationAndRecheckToTheSharedPolicy()
    {
        var workbookCoordinator = Read("src", "FreeX.App.Services", "WorkbookSaveExecutionCoordinator.cs");
        var workbookPersistence = Read("src", "FreeX.App.Services", "WorkbookSaveService.cs");
        var documentCoordinator = Read(
            "freew", "FreeW.App.Presentation", "Shell", "DocumentFileExecutionCoordinator.cs");
        var documentPersistence = Read(
            "freew", "FreeW.App.Presentation", "Shell", "DocumentPersistenceWorkflow.cs");
        var presentationSession = Read(
            "freep", "FreeP.App.Presentation", "PresentationFileCommandSession.cs");
        var presentationPersistence = Read(
            "freep", "FreeP.App.Presentation", "PresentationFilePersistenceWorkflow.cs");

        workbookCoordinator.Should().Contain("ExternalFileWriteConflictPolicy.Prepare(")
            .And.Contain("ExternalFileWriteConflictPolicy.SelectExpectedLastWriteTimeUtc(");
        workbookPersistence.Should().Contain("ExternalFileWriteConflictPolicy.ThrowIfChangedSince(");
        documentCoordinator.Should().Contain("ExternalFileWriteConflictPolicy.PrepareAsync(");
        documentPersistence.Should().Contain("ExternalFileWriteConflictPolicy.ThrowIfChangedSince(");
        presentationSession.Should().Contain("ExternalFileWriteConflictPolicy.PrepareAsync(")
            .And.Contain("ExternalFileWriteConflictPolicy.SelectExpectedLastWriteTimeUtc(");
        presentationPersistence.Should().Contain("ExternalFileWriteConflictPolicy.ThrowIfChangedSince(");
    }

    [Fact]
    public void FreeWAndFreePDelegateSuccessfulSaveStampRollbackToTheSharedTransaction()
    {
        var documentPersistence = Read(
            "freew", "FreeW.App.Presentation", "Shell", "DocumentPersistenceWorkflow.cs");
        var presentationPersistence = Read(
            "freep", "FreeP.App.Presentation", "PresentationFilePersistenceWorkflow.cs");

        foreach (var source in new[] { documentPersistence, presentationPersistence })
        {
            source.Should().Contain("DocumentPropertiesSaveStampTransaction.Begin(")
                .And.Contain("saveStamp.Commit();")
                .And.NotContain("var previousModified =")
                .And.NotContain("var previousLastModifiedBy =");
        }
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(TestWorkspaceFileLocator.FindFromWorkspaceRoot(parts));
}
