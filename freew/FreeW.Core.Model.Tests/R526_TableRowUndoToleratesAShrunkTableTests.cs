using FluentAssertions;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// r526: <c>List.Insert</c> throws once the index exceeds <c>Count</c>, which makes "insert at an
/// index captured earlier" its own signature within the capture-then-revert class -- distinct from
/// reading at a stale index, because the legal upper bound is <c>Count</c> rather than
/// <c>Count - 1</c>.
///
/// <para>Sweeping it across all three apps found nine sites and eight already correct: FreeP clamps
/// with <c>Math.Clamp</c> and <c>Math.Min</c>, FreeX derives its position by walking a bounded loop,
/// and two more in this very file start at <c>Count</c> and only ever move down. DeleteTableRow's
/// Revert was the exception -- it checked <c>_removedAt &lt; 0</c> and stopped there, so a table that
/// lost rows between Apply and Revert turned undo into an exception.</para>
/// </summary>
public class R526_TableRowUndoToleratesAShrunkTableTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    private static (TextDocument doc, Table table, DocumentCommandBus bus) NewWithTable(int rows)
    {
        var doc = new TextDocument();
        doc.Blocks.Clear();
        var table = Table.Create(rows, 2);
        doc.Blocks.Add(table);
        return (doc, table, new DocumentCommandBus(new Context(doc)));
    }

    [Fact]
    public void UndoAfterTheTableLostRowsDoesNotThrow()
    {
        var (_, table, bus) = NewWithTable(4);
        bus.Execute(new DeleteTableRowCommand(0, 3));
        table.Rows.Count.Should().Be(3);

        // The table shrinks underneath the captured index before undo runs.
        while (table.Rows.Count > 1)
            table.Rows.RemoveAt(table.Rows.Count - 1);

        var act = () => bus.Undo();

        act.Should().NotThrow();
    }

    [Fact]
    public void AnOrdinaryUndoStillRestoresTheRow()
    {
        // The guard must not have turned a working undo into a no-op.
        var (_, table, bus) = NewWithTable(3);

        bus.Execute(new DeleteTableRowCommand(0, 1));
        table.Rows.Count.Should().Be(2);

        bus.Undo();
        table.Rows.Count.Should().Be(3);
    }
}
