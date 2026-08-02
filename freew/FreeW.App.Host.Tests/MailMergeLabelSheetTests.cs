using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class MailMergeLabelSheetTests
{
    [StaFact]
    public void ApplyLabelSheet_populates_the_new_mid_document_table_and_preserves_trailing_content()
    {
        var template = new Paragraph();
        template.Runs.Add(new Run("Dear "));
        template.Runs.Add(new Run(
            $"{MailMerge.FieldOpen}Name{MailMerge.FieldClose}",
            RunFormatting.Default with { Bold = true }));

        var existingTable = Table.Create(1, 1);
        existingTable.Rows[0].Cells[0].Paragraphs.Clear();
        existingTable.Rows[0].Cells[0].Paragraphs.Add(new Paragraph("Existing table"));

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(template);
        document.Blocks.Add(existingTable);
        document.Blocks.Add(new Paragraph("Trailing paragraph"));

        var editor = new DocumentView();
        editor.LoadModel(document);
        editor.MoveCaretToBlockForTest(0, 0);

        var data = new MergeData(["Name"], [["Ada"], ["Grace"]]);
        var session = new FreeWRibbonCommands.MailMergeSession
        {
            Data = data,
            Mapping = MailMerge.AutoMatchFields(data.Header),
        };

        FreeWRibbonCommands.ApplyLabelSheet(
            editor,
            session,
            new LabelSetupResult(1, 2, 612, 792, 18, Landscape: false));

        editor.Model.Blocks.Should().HaveCount(4);
        var labels = editor.Model.Blocks[1].Should().BeOfType<Table>().Subject;
        labels.Rows[0].Cells[0].PlainText.Should().Be("Dear Ada\nTrailing paragraph");
        labels.Rows[0].Cells[1].PlainText.Should().Be("Dear Grace\nTrailing paragraph");
        labels.Rows[0].Cells[0].Paragraphs[0].Runs[1].Formatting.Bold.Should().BeTrue();

        editor.Model.Blocks[2].Should().BeOfType<Table>()
            .Which.Rows[0].Cells[0].PlainText.Should().Be("Existing table");
        editor.Model.Blocks[3].Should().BeOfType<Paragraph>()
            .Which.PlainText.Should().Be("Trailing paragraph");
        editor.Model.Page.MarginLeftPt.Should().Be(18);
    }

    [StaFact]
    public void ApplyLabelSheet_skipped_recipient_does_not_consume_a_cell()
    {
        var skip = MergeRuleEvaluator.BuildSkipRecordIfInstruction(
            "Skip", MergeConditionOperator.Equal, "Yes");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(
            $"{MailMerge.FieldOpen}{skip}{MailMerge.FieldClose}" +
            $"{MailMerge.FieldOpen}Name{MailMerge.FieldClose}"));

        var editor = new DocumentView();
        editor.LoadModel(document);
        editor.MoveCaretToBlockForTest(0, 0);

        var data = new MergeData(
            ["Name", "Skip"],
            [["Ada", "Yes"], ["Grace", "No"]]);
        var session = new FreeWRibbonCommands.MailMergeSession { Data = data };

        FreeWRibbonCommands.ApplyLabelSheet(
            editor,
            session,
            new LabelSetupResult(1, 2, 612, 792, 18, Landscape: false));

        var labels = editor.Model.Blocks[1].Should().BeOfType<Table>().Subject;
        labels.Rows[0].Cells[0].PlainText.Should().Be("Grace");
        labels.Rows[0].Cells[1].PlainText.Should().BeEmpty();
    }
}
