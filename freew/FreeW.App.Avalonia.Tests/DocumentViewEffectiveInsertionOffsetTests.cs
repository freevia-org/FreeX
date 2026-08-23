using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewEffectiveInsertionOffsetTests
{
    [Fact]
    public void ContentControlInsertionInsideControl_AdvancesCaretPastBothSemanticRuns()
    {
        var (view, paragraph, controlRun) = ViewWithContentControl();
        view.MoveCaretToBlockForTest(0, 5);

        view.InsertCheckBoxControl();

        var inserted = paragraph.Runs.Single(run => !ReferenceEquals(run, controlRun));
        paragraph.Runs.Should().Equal(controlRun, inserted);
        view.CaretPositionForTest.Should().Be((0, controlRun.Text.Length + inserted.Text.Length));
    }

    [Fact]
    public void CitationInsertionInsideControl_AdvancesCaretPastAdjustedCitationRun()
    {
        var (view, paragraph, controlRun) = ViewWithContentControl();
        var source = new Source
        {
            Tag = "Do24",
            Author = "Jane Q. Doe",
            Title = "A Work",
            Year = "2024",
        };
        view.MoveCaretToBlockForTest(0, 5);

        view.InsertCitation(source);

        var citationRun = paragraph.Runs.Single(run => run.ComplexField is not null);
        paragraph.Runs.Should().Equal(controlRun, citationRun);
        view.CaretPositionForTest.Should().Be((0, controlRun.Text.Length + citationRun.Text.Length));
    }

    [Fact]
    public void FieldInsertionInsideControl_AdvancesCaretPastAdjustedFieldRun()
    {
        var (view, paragraph, controlRun) = ViewWithContentControl();
        view.MoveCaretToBlockForTest(0, 5);

        view.InsertField(RunFieldKind.Date);

        var fieldRun = paragraph.Runs.Single(run => run.FieldKind == RunFieldKind.Date);
        paragraph.Runs.Should().Equal(controlRun, fieldRun);
        view.CaretPositionForTest.Should().Be((0, controlRun.Text.Length + fieldRun.Text.Length));
    }

    private static (DocumentView View, Paragraph Paragraph, Run ControlRun) ViewWithContentControl()
    {
        var controlRun = Run.PlainTextControl("Controlled text", tag: "Customer");
        var paragraph = new Paragraph { Runs = { controlRun } };
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);
        return (view, paragraph, controlRun);
    }
}
