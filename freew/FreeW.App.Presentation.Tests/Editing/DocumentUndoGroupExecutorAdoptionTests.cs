using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class DocumentUndoGroupExecutorAdoptionTests
{
    [Fact]
    public void ParagraphFormattingRetainsGroupedMutationAndStateNotification()
    {
        var document = new TextDocument
        {
            Blocks =
            {
                new Paragraph("first"),
                new Paragraph("second"),
            },
        };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var changed = 0;
        session.Changed += () => changed++;

        session.FormatParagraphs(
                [0, 1],
                formatting => formatting with { KeepWithNext = true },
                "Keep Pair")
            .Should().BeTrue();

        document.Paragraphs.Should().OnlyContain(paragraph => paragraph.Formatting.KeepWithNext);
        changed.Should().Be(1);
        session.Commands.Undo().Should().BeTrue();
        document.Paragraphs.Should().OnlyContain(paragraph => !paragraph.Formatting.KeepWithNext);
        changed.Should().Be(2);
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void ReferenceInsertionRetainsGroupedDomainMutationAndUndo()
    {
        var document = new TextDocument();
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var changed = 0;
        session.Changed += () => changed++;

        var result = session.References.InsertNote(-1, 0, "note text", footnote: true);

        result.Applied.Should().BeTrue();
        result.HostBlockIndex.Should().Be(0);
        document.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>();
        document.Footnotes.Should().ContainSingle();
        changed.Should().Be(1);

        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Should().BeEmpty();
        document.Footnotes.Should().BeEmpty();
        changed.Should().Be(2);
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void TableFormattingRetainsGroupedDomainMutationAndSelectionResult()
    {
        var table = Table.Create(1, 2);
        var document = new TextDocument { Blocks = { table } };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var changed = 0;
        session.Changed += () => changed++;
        var addresses = new[]
        {
            new DocumentTableCellAddress(0, 0, 0),
            new DocumentTableCellAddress(0, 0, 1),
        };

        var result = session.Tables.SetCellShading(addresses, "#ABCDEF");

        result.Applied.Should().BeTrue();
        result.Caret.Should().Be(addresses[0]);
        result.InvalidatesNativeSelection.Should().BeFalse();
        table.Rows[0].Cells.Should().OnlyContain(cell => cell.ShadingColorHex == "#ABCDEF");
        changed.Should().Be(1);

        session.Commands.Undo().Should().BeTrue();
        table.Rows[0].Cells.Should().OnlyContain(cell => cell.ShadingColorHex == null);
        changed.Should().Be(2);
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void FullyRestoredFailureDoesNotRaiseSessionChangedOrLeaveModelState()
    {
        var document = new TextDocument();
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var changed = 0;
        session.Changed += () => changed++;
        var failure = new InvalidOperationException("apply failed");

        Action act = () => DocumentUndoGroupExecutor.Execute(
            session.Commands,
            [
                new InsertParagraphCommand(0, new Paragraph("temporary")),
                new FailingCommand(failure),
            ],
            "Failing Edit");

        act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        document.Blocks.Should().BeEmpty();
        changed.Should().Be(0);
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void PartialRollbackRaisesSessionChangedAndRetainsDiagnostics()
    {
        var document = new TextDocument();
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var changed = 0;
        session.Changed += () => changed++;
        var applyFailure = new InvalidOperationException("apply failed");
        var rollbackFailure = new InvalidOperationException("rollback failed");

        Action act = () => DocumentUndoGroupExecutor.Execute(
            session.Commands,
            [
                new StickyInsertCommand(rollbackFailure),
                new FailingCommand(applyFailure),
            ],
            "Failing Edit");

        act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(applyFailure);
        document.PlainText.Should().Be("surviving state");
        changed.Should().Be(1);
        session.Commands.CanUndo.Should().BeFalse();
        applyFailure.Data[DocumentUndoGroupExecutor.RollbackFailuresDataKey]
            .Should().BeOfType<AggregateException>()
            .Which.InnerExceptions.Should().Equal(rollbackFailure);
    }

    [Fact]
    public void RectangularMergeRetainsGroupedSelectionInvalidationAndUndoRedo()
    {
        var table = Table.Create(2, 2);
        var document = new TextDocument { Blocks = { table } };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var result = session.Tables.MergeCells(
            new DocumentTableCellAddress(0, 0, 0),
            new DocumentTableCellAddress(0, 1, 1));

        result.Applied.Should().BeTrue();
        result.Caret.Should().Be(new DocumentTableCellAddress(0, 0, 0));
        result.InvalidatesNativeSelection.Should().BeTrue();
        table.Rows.Should().OnlyContain(row => row.Cells.Count == 1);

        session.Commands.Undo().Should().BeTrue();
        table.Rows.Should().OnlyContain(row => row.Cells.Count == 2);
        session.Commands.Redo().Should().BeTrue();
        table.Rows.Should().OnlyContain(row => row.Cells.Count == 1);
    }

    [Fact]
    public void AllThreeOwnersUseTheSharedExecutor()
    {
        foreach (var file in new[]
        {
            "DocumentEditingSession.cs",
            "DocumentReferenceEditingCoordinator.cs",
            "DocumentTableEditingCoordinator.cs",
        })
        {
            var source = ReadSource("freew", "FreeW.App.Presentation", "Editing", file);
            source.Should().Contain("DocumentUndoGroupExecutor.Execute(");
            source.Should().NotContain("private void ExecuteGroup(");
        }
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private sealed class FailingCommand(Exception failure) : IDocumentCommand
    {
        public string Label => "Fail";
        public void Apply(IDocumentCommandContext context) => throw failure;
        public void Revert(IDocumentCommandContext context) => throw new InvalidOperationException();
    }

    private sealed class StickyInsertCommand(Exception rollbackFailure) : IDocumentCommand
    {
        public string Label => "Sticky insert";

        public void Apply(IDocumentCommandContext context) =>
            context.Document.Blocks.Add(new Paragraph("surviving state"));

        public void Revert(IDocumentCommandContext context) => throw rollbackFailure;
    }
}
