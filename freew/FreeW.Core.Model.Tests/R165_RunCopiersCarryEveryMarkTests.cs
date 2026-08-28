using FluentAssertions;
using FreeW.Core.Model;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// r165. The run-copier defect class, stated once so it stops being rediscovered a property at a time.
///
/// A run carries roughly thirty marks. Several places build a new run from an existing one, and every
/// one that hand-lists the marks to carry has eventually been found dropping some. Round 163 found
/// StyleId missing from three of four copiers and added StyleId to them. Round 164 found a sixth
/// copier (DropCap) and showed the two comment copiers were missing about twenty other properties.
/// Round 165 found a ninth, in the DOCX writer's header-row path, and -- in the same round -- a
/// sibling fixer adding one more property by hand to that very list.
///
/// The count grew because each repair added the mark that had been noticed. These tests pin the
/// canonical copiers against the marks most often lost, so a copier that stops carrying one fails
/// here rather than in a user's saved file. <see cref="R163_RunStyleIdSurvivesCopyTests"/> covers
/// StyleId specifically and stays as it is.
/// </summary>
public sealed class R165_RunCopiersCarryEveryMarkTests
{
    private static Run RichRun() => new("marked")
    {
        StyleId = "Strong",
        MoveRevisionId = 77,
        FieldLocked = true,
        FieldKind = RunFieldKind.PageNumber,
        FootnoteId = 3,
        CommentId = 9,
        Revision = RevisionKind.Inserted,
        RevisionAuthor = "Reviewer",
    };

    [Fact]
    public void CloneRunWithText_carries_every_mark_the_source_had()
    {
        var source = RichRun();

        var clone = RevisionEditPlanner.CloneRunWithText(source, "marked");

        clone.StyleId.Should().Be("Strong");
        clone.MoveRevisionId.Should().Be(77);
        clone.FieldLocked.Should().BeTrue("a locked field stays locked when its run is copied");
        clone.FieldKind.Should().Be(RunFieldKind.PageNumber);
        clone.FootnoteId.Should().Be(3);
        clone.CommentId.Should().Be(9);
        clone.Revision.Should().Be(RevisionKind.Inserted);
        clone.RevisionAuthor.Should().Be("Reviewer");
    }

    [Fact]
    public void DocumentModelCloner_CloneRun_carries_every_mark_the_source_had()
    {
        var source = RichRun();

        var clone = DocumentModelCloner.CloneRun(source, RevisionClonePolicy.Preserve);

        clone.StyleId.Should().Be("Strong");
        clone.MoveRevisionId.Should().Be(77);
        clone.FieldLocked.Should().BeTrue();
        clone.FieldKind.Should().Be(RunFieldKind.PageNumber);
        clone.FootnoteId.Should().Be(3);
        clone.CommentId.Should().Be(9);
    }

    [Fact]
    public void An_unmarked_run_stays_unmarked()
    {
        // Sibling/no-regression: carrying marks must not invent them.
        var clone = RevisionEditPlanner.CloneRunWithText(new Run("plain"), "plain");

        clone.StyleId.Should().BeNull();
        clone.MoveRevisionId.Should().BeNull();
        clone.FieldLocked.Should().BeFalse();
        clone.FieldKind.Should().Be(RunFieldKind.None);
    }
}
