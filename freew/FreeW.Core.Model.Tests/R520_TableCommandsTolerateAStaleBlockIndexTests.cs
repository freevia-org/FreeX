using FluentAssertions;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// r520: every table-row command in EditCommands.cs reached its table through an unchecked cast,
/// <c>(Table)context.Document.Blocks[index]</c>. A block index that is out of range threw
/// ArgumentOutOfRangeException, and one pointing at a paragraph rather than a table threw
/// InvalidCastException.
///
/// <para>Two things made that worse than a local slip. The cell commands in the SAME file have always
/// used a validating accessor (TryGetCell) that bounds-checks the index, type-checks the block, and
/// is re-checked inside Apply -- so the row commands were the less defensive half of one family, for
/// no reason. And FreeW's command bus does NOT wrap Apply in try/catch, unlike FreeX's and FreeP's,
/// so the throw escaped the command layer entirely rather than being turned into a failed outcome.
/// HasEffect defaults to true and these commands did not override it, so nothing gated the call
/// either.</para>
///
/// <para>The row commands now use TryGetTable and no-op on an invalid target, exactly as the cell
/// commands do. No-op rather than throw is the established policy in this file, not a new one.</para>
/// </summary>
public class R520_TableCommandsTolerateAStaleBlockIndexTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    private static (TextDocument doc, DocumentCommandBus bus) New()
    {
        var doc = new TextDocument();
        return (doc, new DocumentCommandBus(new Context(doc)));
    }

    [Fact]
    public void BlockIndexPointingAtAParagraphDoesNotThrow()
    {
        var (doc, bus) = New();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("not a table"));

        // Previously: InvalidCastException out of Apply, straight through the unguarded bus.
        var act = () => bus.Execute(new InsertTableRowCommand(0, 0));

        act.Should().NotThrow();
        doc.Blocks[0].Should().BeOfType<Paragraph>("the document must be left exactly as it was");
    }

    [Fact]
    public void BlockIndexPastTheEndDoesNotThrow()
    {
        var (doc, bus) = New();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("only block"));

        // Previously: ArgumentOutOfRangeException from indexing Blocks.
        var act = () => bus.Execute(new InsertTableRowCommand(7, 0));

        act.Should().NotThrow();
        doc.Blocks.Should().HaveCount(1);
    }

    [Fact]
    public void DeleteRowOnANonTableBlockDoesNotThrow()
    {
        var (doc, bus) = New();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("not a table"));

        var act = () => bus.Execute(new DeleteTableRowCommand(0, 0));

        act.Should().NotThrow();
    }

    [Fact]
    public void AValidTargetStillWorksExactlyAsBefore()
    {
        // The guard must not have turned a working command into a silent no-op.
        var (doc, bus) = New();
        doc.Blocks.Clear();
        var table = Table.Create(2, 3);
        doc.Blocks.Add(table);

        bus.Execute(new InsertTableRowCommand(0, 1));
        table.RowCount.Should().Be(3);

        bus.Undo();
        table.RowCount.Should().Be(2);
    }
}
