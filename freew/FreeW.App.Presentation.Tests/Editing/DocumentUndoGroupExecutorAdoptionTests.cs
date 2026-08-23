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
}
