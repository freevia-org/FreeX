namespace FreeW.Core.Model.Tests;

public class MailMergeTests
{
    [Fact]
    public void FieldNames_FromText_AreDistinctInFirstAppearanceOrder()
    {
        const string text = "Dear «First» «Last», your code is «First».";

        var names = MailMerge.FieldNames(text);

        names.Should().Equal("First", "Last");
    }

    [Fact]
    public void FieldNames_AreDistinctCaseInsensitively_FirstSpellingWins()
    {
        const string text = "«City» then «city» then «CITY»";

        MailMerge.FieldNames(text).Should().Equal("City");
    }

    [Fact]
    public void FieldNames_TrimsWhitespaceAndIgnoresEmptyPlaceholders()
    {
        const string text = "«  Name  » and «» and «   »";

        MailMerge.FieldNames(text).Should().Equal("Name");
    }

    [Fact]
    public void FieldNames_FromDocument_ScansParagraphsAndTableCells()
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run("Hello «Name», "));
        para.Runs.Add(new Run("from «Company»"));
        doc.Blocks.Add(para);

        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("«Item»"));
        table.Rows[0].Cells[1].Paragraphs[0].Runs.Add(new Run("«Name»")); // duplicate, dropped
        doc.Blocks.Add(table);

        MailMerge.FieldNames(doc).Should().Equal("Name", "Company", "Item");
    }

    [Fact]
    public void Substitute_ReplacesPresentField()
    {
        var row = new Dictionary<string, string> { ["Name"] = "Ada" };

        MailMerge.Substitute("Hello «Name»!", row).Should().Be("Hello Ada!");
    }

    [Fact]
    public void Substitute_MissingField_BecomesEmptyString()
    {
        var row = new Dictionary<string, string> { ["Name"] = "Ada" };

        MailMerge.Substitute("Hi «Name» «Title»!", row).Should().Be("Hi Ada !");
    }

    [Fact]
    public void Substitute_IsCaseInsensitive_WhenDictionarySupportsIt()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["name"] = "Ada" };

        MailMerge.Substitute("Hello «Name»!", row).Should().Be("Hello Ada!");
    }

    [Fact]
    public void Substitute_UnterminatedDelimiter_NoClosing_IsLeftLiteral()
    {
        var row = new Dictionary<string, string> { ["Name"] = "Ada" };

        // No closing » anywhere → the opening « and the rest are emitted verbatim.
        MailMerge.Substitute("Hello «Name and the rest", row)
            .Should().Be("Hello «Name and the rest");
    }

    [Fact]
    public void Substitute_NoPlaceholders_ReturnsInputUnchanged()
    {
        MailMerge.Substitute("plain text", new Dictionary<string, string>())
            .Should().Be("plain text");
    }

    [Fact]
    public void MergeRecord_SubstitutesEveryRun_AndDoesNotMutateTemplate()
    {
        var template = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run("Dear ", new RunFormatting { Bold = true }));
        para.Runs.Add(new Run("«First» «Last»"));
        template.Blocks.Add(para);

        var row = new Dictionary<string, string> { ["First"] = "Grace", ["Last"] = "Hopper" };
        var merged = MailMerge.MergeRecord(template, row);

        merged.PlainText.Should().Be("Dear Grace Hopper");
        // The bold formatting on the leading run is preserved.
        var firstRun = ((Paragraph)merged.Blocks[0]).Runs[0];
        firstRun.Formatting.Bold.Should().BeTrue();
        // Template is untouched.
        template.PlainText.Should().Be("Dear «First» «Last»");
    }

    [Fact]
    public void MergeRecord_SubstitutesInsideTableCells()
    {
        var template = new TextDocument();
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("«Item»"));
        template.Blocks.Add(table);

        var merged = MailMerge.MergeRecord(template,
            new Dictionary<string, string> { ["Item"] = "Widget" });

        var cell = ((Table)merged.Blocks[0]).Rows[0].Cells[0];
        cell.PlainText.Should().Be("Widget");
    }

    [Fact]
    public void MergeAll_ProducesOneDocumentPerRow_InOrder()
    {
        var template = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run("Hello «Name»"));
        template.Blocks.Add(para);

        var data = new MergeData(
            ["Name"],
            [["Ada"], ["Grace"], ["Linus"]]);

        var merged = MailMerge.MergeAll(template, data);

        merged.Should().HaveCount(3);
        merged[0].PlainText.Should().Be("Hello Ada");
        merged[1].PlainText.Should().Be("Hello Grace");
        merged[2].PlainText.Should().Be("Hello Linus");
    }

    [Fact]
    public void MergeAll_EmptyData_YieldsEmptyList()
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph("«Name»"));

        MailMerge.MergeAll(template, MergeData.FromCsv(string.Empty)).Should().BeEmpty();
    }

    [Fact]
    public void CombineMergedRecords_Letters_StartsEachAdditionalRecordOnANewPage()
    {
        var docs = new[]
        {
            new TextDocument { Blocks = { new Paragraph("Ada") } },
            new TextDocument { Blocks = { new Paragraph("Grace") } }
        };

        var combined = MailMerge.CombineMergedRecords(docs, MailMergeOutputMode.Letters);

        combined.Blocks.Should().HaveCount(2);
        ((Paragraph)combined.Blocks[0]).Formatting.PageBreakBefore.Should().BeFalse();
        ((Paragraph)combined.Blocks[1]).Formatting.PageBreakBefore.Should().BeTrue();
        combined.PlainText.Should().Be("Ada\nGrace");
    }

    [Fact]
    public void CombineMergedRecords_Directory_AppendsRecordsContinuously()
    {
        var docs = new[]
        {
            new TextDocument { Blocks = { new Paragraph("Ada") } },
            new TextDocument { Blocks = { new Paragraph("Grace") } }
        };

        var combined = MailMerge.CombineMergedRecords(docs, MailMergeOutputMode.Directory);

        combined.Blocks.Should().HaveCount(2);
        ((Paragraph)combined.Blocks[1]).Formatting.PageBreakBefore.Should().BeFalse();
        combined.PlainText.Should().Be("Ada\nGrace");
    }

    [Fact]
    public void FromCsv_ParsesHeaderAndRows()
    {
        const string csv = "First,Last\nAda,Lovelace\nGrace,Hopper";

        var data = MergeData.FromCsv(csv);

        data.Header.Should().Equal("First", "Last");
        data.Count.Should().Be(2);
        data.Rows[0]["First"].Should().Be("Ada");
        data.Rows[0]["Last"].Should().Be("Lovelace");
        data.Rows[1]["First"].Should().Be("Grace");
    }

    [Fact]
    public void FromCsv_HonoursQuotedFields_WithEmbeddedCommasAndQuotes()
    {
        const string csv = "Name,Note\n\"Doe, Jane\",\"She said \"\"hi\"\"\"";

        var data = MergeData.FromCsv(csv);

        data.Rows.Should().HaveCount(1);
        data.Rows[0]["Name"].Should().Be("Doe, Jane");
        data.Rows[0]["Note"].Should().Be("She said \"hi\"");
    }

    [Fact]
    public void FromCsv_LookupIsCaseInsensitive()
    {
        var data = MergeData.FromCsv("Name\nAda");

        data.Rows[0]["name"].Should().Be("Ada");
        data.Rows[0]["NAME"].Should().Be("Ada");
    }

    [Fact]
    public void FromCsv_ShortRow_PadsMissingCellsWithEmpty()
    {
        var data = MergeData.FromCsv("A,B,C\n1,2");

        data.Rows[0]["A"].Should().Be("1");
        data.Rows[0]["B"].Should().Be("2");
        data.Rows[0]["C"].Should().Be(string.Empty);
    }

    [Fact]
    public void FromCsv_HandlesCrlfLineEndings()
    {
        var data = MergeData.FromCsv("First,Last\r\nAda,Lovelace\r\n");

        data.Count.Should().Be(1);
        data.Rows[0]["First"].Should().Be("Ada");
    }

    [Fact]
    public void EndToEnd_MergeAll_OverCsv_FillsTemplate()
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph("Dear «First» «Last»,"));

        var data = MergeData.FromCsv("First,Last\nAda,Lovelace\nGrace,Hopper");
        var docs = MailMerge.MergeAll(template, data);

        docs.Should().HaveCount(2);
        docs[0].PlainText.Should().Be("Dear Ada Lovelace,");
        docs[1].PlainText.Should().Be("Dear Grace Hopper,");
    }

    [Fact]
    public void MergeRecord_PreservesEndnoteAndCellMergeMarks()
    {
        // Regression: the clone path used to drop EndnoteId/HyperlinkTooltip from runs and
        // GridSpan/VerticalMerge from cells, orphaning endnotes and collapsing merged cells.
        var template = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.EndnoteReference(1));
        template.Blocks.Add(para);
        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0] = new TableCell("m") { GridSpan = 2, VerticalMerge = VerticalMergeState.Restart };
        template.Blocks.Add(table);

        var merged = MailMerge.MergeRecord(template, new Dictionary<string, string>());

        merged.Paragraphs.First().Runs.Single().EndnoteId.Should().Be(1);
        var mergedTable = merged.Blocks.OfType<Table>().Single();
        mergedTable.Rows[0].Cells[0].GridSpan.Should().Be(2);
        mergedTable.Rows[0].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Restart);
    }
}
