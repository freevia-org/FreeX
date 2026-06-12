using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Tests for the DirtyGeneration counter added to <see cref="WorkbookSession"/>
/// and the associated <see cref="WorkbookSession.TryMarkSavedIfNoEditsArrived"/> method.
/// These are the core session-layer semantics that the Avalonia save path relies on
/// to detect mid-save edits without losing data.
/// </summary>
public sealed class WorkbookSessionDirtyGenerationTests
{
    // ── Initial state ────────────────────────────────────────────────────────

    [Fact]
    public void NewSession_DirtyGenerationStartsAtZero()
    {
        var session = CreateSession();

        session.DirtyGeneration.Should().Be(0);
        session.IsDirty.Should().BeFalse();
    }

    // ── Generation increments on each edit ───────────────────────────────────

    [Fact]
    public void AddSheet_IncrementsDirtyGeneration()
    {
        var session = CreateSession();

        session.AddSheet();

        session.IsDirty.Should().BeTrue();
        session.DirtyGeneration.Should().Be(1);
    }

    [Fact]
    public void MultipleEdits_IncrementGenerationEachTime()
    {
        var session = CreateSession();

        session.AddSheet();
        session.AddSheet();
        session.AddSheet();

        session.DirtyGeneration.Should().Be(3);
    }

    [Fact]
    public void RenameActiveSheet_IncrementsDirtyGeneration()
    {
        var session = CreateSession();
        var before = session.DirtyGeneration;

        session.RenameActiveSheet("Renamed");

        session.DirtyGeneration.Should().Be(before + 1);
        session.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void ReplaceAllValues_IncrementsDirtyGeneration()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("hello"));
        var session = CreateSession(workbook);

        session.ReplaceAllValues("hello", "world");

        session.DirtyGeneration.Should().Be(1);
        session.IsDirty.Should().BeTrue();
    }

    // ── MarkSaved resets dirty but not generation ─────────────────────────────

    [Fact]
    public void MarkSaved_ClearsDirtyFlagButDoesNotResetGeneration()
    {
        var session = CreateSession();
        session.AddSheet();
        session.AddSheet();
        var generationBeforeSave = session.DirtyGeneration;

        session.MarkSaved("/tmp/Book.fxl");

        session.IsDirty.Should().BeFalse();
        // Generation does not reset — it keeps increasing monotonically so that
        // the next save can still detect new edits relative to a newly captured snapshot.
        session.DirtyGeneration.Should().Be(generationBeforeSave);
    }

    // ── TryMarkSavedIfNoEditsArrived: clean save ─────────────────────────────

    [Fact]
    public void TryMarkSavedIfNoEditsArrived_NoEdits_ReturnsTrueAndClearsDirty()
    {
        var session = CreateSession();
        session.AddSheet(); // makes it dirty, generation=1
        var snapshot = session.DirtyGeneration;

        // No more edits arrive — generation still matches snapshot.
        var saved = session.TryMarkSavedIfNoEditsArrived(snapshot, "/tmp/Book.fxl");

        saved.Should().BeTrue();
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void TryMarkSavedIfNoEditsArrived_NoEdits_UpdatesCurrentFilePath()
    {
        var session = CreateSession();
        session.AddSheet();
        var snapshot = session.DirtyGeneration;

        session.TryMarkSavedIfNoEditsArrived(snapshot, "/tmp/SavedBook.fxl");

        session.CurrentFilePath.Should().Be("/tmp/SavedBook.fxl");
    }

    [Fact]
    public void TryMarkSavedIfNoEditsArrived_NoEdits_UpdatesWorkbookName()
    {
        var session = CreateSession();
        session.AddSheet();
        var snapshot = session.DirtyGeneration;

        session.TryMarkSavedIfNoEditsArrived(snapshot, "/tmp/MyBook.fxl");

        session.Workbook.Name.Should().Be("MyBook.fxl");
    }

    // ── TryMarkSavedIfNoEditsArrived: mid-save edit detected ─────────────────

    [Fact]
    public void TryMarkSavedIfNoEditsArrived_EditDuringSave_ReturnsFalse()
    {
        var session = CreateSession();
        session.AddSheet();                    // generation=1
        var snapshot = session.DirtyGeneration; // snapshot=1
        session.AddSheet();                    // mid-save edit: generation=2

        var saved = session.TryMarkSavedIfNoEditsArrived(snapshot, "/tmp/Book.fxl");

        saved.Should().BeFalse(
            "an edit arrived after the snapshot was captured — dirty flag must be preserved");
    }

    [Fact]
    public void TryMarkSavedIfNoEditsArrived_EditDuringSave_KeepsDirtyFlag()
    {
        var session = CreateSession();
        session.AddSheet();
        var snapshot = session.DirtyGeneration;
        session.AddSheet(); // mid-save edit

        session.TryMarkSavedIfNoEditsArrived(snapshot, "/tmp/Book.fxl");

        session.IsDirty.Should().BeTrue(
            "the mid-save edit must not be silently discarded");
    }

    [Fact]
    public void TryMarkSavedIfNoEditsArrived_EditDuringSave_StillUpdatesFilePath()
    {
        // The save completed and the file was written — file context must be applied
        // even though we cannot clear the dirty flag.
        var session = CreateSession();
        session.AddSheet();
        var snapshot = session.DirtyGeneration;
        session.AddSheet();

        session.TryMarkSavedIfNoEditsArrived(snapshot, "/tmp/SavedWithEdits.fxl");

        session.CurrentFilePath.Should().Be("/tmp/SavedWithEdits.fxl",
            "file path must update so subsequent saves default to the correct location");
    }

    // ── Generation monotonicity across save/edit cycles ──────────────────────

    [Fact]
    public void GenerationMonotonicallyIncreases_AcrossSaveCycles()
    {
        var session = CreateSession();

        // Cycle 1
        session.AddSheet();
        int gen1 = session.DirtyGeneration;
        session.MarkSaved("/tmp/Book.fxl");

        // Cycle 2
        session.RenameActiveSheet("Cycle2");
        int gen2 = session.DirtyGeneration;

        // Cycle 3
        session.AddSheet();
        int gen3 = session.DirtyGeneration;

        gen1.Should().Be(1);
        gen2.Should().BeGreaterThan(gen1);
        gen3.Should().BeGreaterThan(gen2);
    }

    // ── TryMarkSavedIfNoEditsArrived argument guard ───────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryMarkSavedIfNoEditsArrived_NullOrWhiteSpacePath_Throws(string? path)
    {
        var session = CreateSession();
        var act = () => session.TryMarkSavedIfNoEditsArrived(0, path!);

        act.Should().Throw<ArgumentException>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static WorkbookSession CreateSession()
        => new WorkbookSessionFactory().CreateNew(viewportHeight: 240, viewportWidth: 320);

    private static WorkbookSession CreateSession(Workbook workbook)
        => new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
