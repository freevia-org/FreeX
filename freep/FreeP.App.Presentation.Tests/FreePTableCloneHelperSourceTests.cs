using System.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class FreePTableCloneHelperSourceTests
{
    [Fact]
    public void TableRowClone_DetachesNestedTextBodyAndInlineTableGraph()
    {
        var nestedBody = new TextBody
        {
            Paragraphs =
            {
                new Paragraph { Runs = { new Run { Text = "Nested" } } },
            },
        };
        var nestedTable = new TableShape();
        nestedTable.Rows.Add(new TableRow
        {
            Cells = { new TableCell { TextBody = nestedBody } },
        });
        var sourceBody = new TextBody
        {
            Paragraphs =
            {
                new Paragraph
                {
                    Runs =
                    {
                        new Run
                        {
                            Text = "\uFFFC",
                            InlineTable = new InlineTableInfo { Table = nestedTable },
                        },
                    },
                },
            },
        };
        var source = new TableRow
        {
            HeightEmu = 685800,
            Cells = { new TableCell { TextBody = sourceBody } },
        };

        var clone = source.Clone();

        clone.Should().NotBeSameAs(source);
        clone.Cells.Should().NotBeSameAs(source.Cells);
        clone.Cells[0].Should().NotBeSameAs(source.Cells[0]);
        clone.Cells[0].TextBody.Should().NotBeSameAs(sourceBody);
        clone.Cells[0].TextBody!.Paragraphs[0].Should().NotBeSameAs(sourceBody.Paragraphs[0]);
        var clonedInlineTable = clone.Cells[0].TextBody!.Paragraphs[0].Runs[0].InlineTable!;
        clonedInlineTable.Should().NotBeSameAs(sourceBody.Paragraphs[0].Runs[0].InlineTable);
        clonedInlineTable.Table.Should().NotBeSameAs(nestedTable);
        clonedInlineTable.Table.Rows[0].Should().NotBeSameAs(nestedTable.Rows[0]);
        clonedInlineTable.Table.Rows[0].Cells[0].Should().NotBeSameAs(nestedTable.Rows[0].Cells[0]);
        clonedInlineTable.Table.Rows[0].Cells[0].TextBody.Should().NotBeSameAs(nestedBody);

        clonedInlineTable.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Text = "Changed";
        nestedBody.Paragraphs[0].Runs[0].Text.Should().Be("Nested");
    }

    [Fact]
    public void TableRowClone_DetachesBordersAndRowGraphOwnership()
    {
        var borders = new TableCellBorders
        {
            Left = ShapeOutline.None.Instance,
            Top = ShapeOutline.None.Instance,
        };
        var source = new TableRow
        {
            HeightEmu = 457200,
            HeightRule = TableRowHeightRule.Exact,
            HorizontalAlignment = TableRowHorizontalAlignment.Center,
            Cells =
            {
                new TableCell
                {
                    Borders = borders,
                    GridSpan = 2,
                    TextBody = new TextBody
                    {
                        Paragraphs = { new Paragraph { Runs = { new Run { Text = "Cell" } } } },
                    },
                },
            },
        };

        var clone = source.Clone();

        clone.HeightEmu.Should().Be(source.HeightEmu);
        clone.HeightRule.Should().Be(source.HeightRule);
        clone.HorizontalAlignment.Should().Be(source.HorizontalAlignment);
        clone.Cells.Should().NotBeSameAs(source.Cells);
        clone.Cells[0].Should().NotBeSameAs(source.Cells[0]);
        clone.Cells[0].Borders.Should().NotBeSameAs(borders);

        clone.Cells[0].Borders!.Left = null;
        clone.Cells[0].TextBody!.Paragraphs[0].Runs[0].Text = "Changed";
        clone.Cells.Add(new TableCell());

        borders.Left.Should().BeSameAs(ShapeOutline.None.Instance);
        source.Cells[0].TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Cell");
        source.Cells.Should().ContainSingle();
    }

    [Fact]
    public void TableCommandsAndSlideCloner_UseSharedCoreModelCloneHelper()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var commandSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.Core.Model",
            "PresentationCommands.Table.cs"));
        var clonerSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.Core.Model",
            "SlideCloner.cs"));
        var helperSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.Core.Model",
            "PresentationModelCloneHelper.cs"));
        var avaloniaSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "AvaloniaRichTextEditor.cs"));

        commandSource.Should().NotContain("file static class TableCommandHelper");
        commandSource.Should().NotContain("file static class TableGridHelper");
        commandSource.Should().Contain("PresentationModelCloneHelper.FindTable");
        commandSource.Should().Contain("PresentationModelCloneHelper.CloneTable");
        commandSource.Should().Contain("PresentationModelCloneHelper.RestoreTableState");
        commandSource.Should().Contain("PresentationModelCloneHelper.CloneTableCellBorders");
        commandSource.Should().NotContain("private static TableCellBorders CloneBorders");
        commandSource.Should().Contain("TextBodyModelCloner.CloneTextBody");

        clonerSource.Should().Contain("TextBodyModelCloner.CloneTextBody");
        clonerSource.Should().Contain("PresentationModelCloneHelper.CloneTable");
        clonerSource.Should().NotContain("private static TextBody CloneTextBody");
        clonerSource.Should().NotContain("private static TableShape CloneTable");
        clonerSource.Should().NotContain("private static TableCell CloneTableCell");
        helperSource.Should().NotContain("RowGridWidth(");

        avaloniaSource.Should().Contain("pending.Rows[index].Clone()");
        avaloniaSource.Should().NotContain("private static TableRow CloneTableRow");
    }

}
