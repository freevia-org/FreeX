namespace FreeW.Core.Model.Tests;

/// <summary>
/// r227: Distribute Rows and Distribute Columns, carried across from FreeX by the method r226
/// arrived at -- grep the SHAPE, not the family. Both had an Apply with a "nothing to do" early
/// return and no HasEffect override, so the bus pushed an undo entry either way, and the push clears
/// redo.
/// <para>
/// The trap in these two is that each already ends with <c>_applied =
/// TableLayoutOperations.DistributeRows(table)</c>, which LOOKS like a did-it-change flag and is
/// not: that bool reports whether the operation was APPLICABLE, and is true for any table with
/// rows -- including one that is already evenly distributed. The new WouldDistribute* predicates
/// answer the question the bool does not, and share the target-size calculation with the mutation
/// so the two cannot drift apart.
/// </para>
/// <para>
/// The HasEffect calls below go through IDocumentCommand deliberately. Called on the concrete
/// type, deleting the override would break the BUILD rather than the tests, and a compile error is
/// a weaker proof than a red test -- it shows only that the tests reference new code. Through the
/// interface the default (true) takes over instead, so reverting the fix makes these fail for the
/// reason they exist.
/// </para>
/// </summary>
public sealed class R227_DistributeTableNoOpTests
{
    private sealed class Ctx(TextDocument doc) : IDocumentCommandContext
    {
        public TextDocument Document => doc;
    }

    private static (TextDocument Document, Table Table) DocumentWithTable()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = new Table();
        for (var r = 0; r < 2; r++)
        {
            var row = new TableRow();
            for (var c = 0; c < 3; c++)
                row.Cells.Add(new TableCell());
            table.Rows.Add(row);
        }
        document.Blocks.Add(table);
        return (document, table);
    }

    [Fact]
    public void DistributingColumnsThatAreAlreadyEven_HasNoEffect()
    {
        var (document, table) = DocumentWithTable();
        new DistributeTableColumnsCommand(0).Apply(new Ctx(document));

        ((IDocumentCommand)new DistributeTableColumnsCommand(0)).HasEffect(new Ctx(document))
            .Should().BeFalse("the widths written by the first run are the ones the second would write");
    }

    [Fact]
    public void DistributingUnevenColumns_HasEffect()
    {
        var (document, table) = DocumentWithTable();
        table.ColumnWidthsPt.Clear();
        table.ColumnWidthsPt.AddRange([120, 40, 40]);

        ((IDocumentCommand)new DistributeTableColumnsCommand(0)).HasEffect(new Ctx(document)).Should().BeTrue();
    }

    [Fact]
    public void DistributingColumnsOnABlockThatIsNotATable_HasNoEffect()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph());

        ((IDocumentCommand)new DistributeTableColumnsCommand(0)).HasEffect(new Ctx(document)).Should().BeFalse();
    }

    [Fact]
    public void DistributingRowsThatAreAlreadyEven_HasNoEffect()
    {
        var (document, _) = DocumentWithTable();
        new DistributeTableRowsCommand(0).Apply(new Ctx(document));

        ((IDocumentCommand)new DistributeTableRowsCommand(0)).HasEffect(new Ctx(document)).Should().BeFalse();
    }

    [Fact]
    public void DistributingRowsWithDifferentHeights_HasEffect()
    {
        var (document, table) = DocumentWithTable();
        table.Rows[0].HeightPt = 30;
        table.Rows[1].HeightPt = 90;

        ((IDocumentCommand)new DistributeTableRowsCommand(0)).HasEffect(new Ctx(document)).Should().BeTrue();
    }

    [Fact]
    public void TheApplicableBoolIsNotAChangedBool()
    {
        // Pinning the trap itself, so nobody re-derives HasEffect from this return value: it stays
        // true on a run that writes exactly what is already there.
        var (document, table) = DocumentWithTable();
        TableLayoutOperations.DistributeColumns(table).Should().BeTrue();

        TableLayoutOperations.DistributeColumns(table).Should().BeTrue("applicable, but nothing changed");
        TableLayoutOperations.WouldDistributeColumns(table).Should().BeFalse("this is the honest answer");
    }
}
