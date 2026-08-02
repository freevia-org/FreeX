using FreeW.App.Host.Editing;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfRun = System.Windows.Documents.Run;

namespace FreeW.App.Host.Tests;

public sealed class DoubleStrikethroughRenderTests
{
    [StaFact]
    public void Load_and_commit_renders_two_strikes_without_changing_single_strike_control()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("double", RunFormatting.Default with { DoubleStrikethrough = true }));
        paragraph.Runs.Add(new Run(" single", RunFormatting.Default with { Strikethrough = true }));
        document.Blocks.Add(paragraph);
        var view = new DocumentView();

        view.LoadModel(document);

        var rendered = view.Document.Blocks.OfType<WpfParagraph>().Single().Inlines.OfType<WpfRun>().ToArray();
        var doubleLines = rendered[0].TextDecorations!
            .Where(decoration => decoration.Location == System.Windows.TextDecorationLocation.Strikethrough)
            .ToArray();
        doubleLines.Should().HaveCount(2);
        doubleLines.Select(decoration => decoration.PenOffset).Should().OnlyHaveUniqueItems();
        rendered[1].TextDecorations!
            .Count(decoration => decoration.Location == System.Windows.TextDecorationLocation.Strikethrough)
            .Should().Be(1);

        view.CommitToModel();

        var committed = ((Paragraph)view.Model.Blocks[0]).Runs;
        committed[0].Formatting.DoubleStrikethrough.Should().BeTrue();
        committed[0].Formatting.Strikethrough.Should().BeFalse();
        committed[1].Formatting.DoubleStrikethrough.Should().BeFalse();
        committed[1].Formatting.Strikethrough.Should().BeTrue();
    }

    [StaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Effective_double_strike_honors_based_on_style_and_document_default(bool useDefault)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph("double");
        if (useDefault)
        {
            document.DefaultRun = document.DefaultRun with { DoubleStrikethrough = true };
        }
        else
        {
            document.Styles["DoubleBase"] = new DocumentStyle
            {
                Id = "DoubleBase",
                Name = "Double base",
                Run = RunFormatting.Default with { DoubleStrikethrough = true },
            };
            document.Styles["DoubleChild"] = new DocumentStyle
            {
                Id = "DoubleChild",
                Name = "Double child",
                BasedOnStyleId = "DoubleBase",
            };
            paragraph.StyleId = "DoubleChild";
        }
        document.Blocks.Add(paragraph);
        var view = new DocumentView();

        view.LoadModel(document);

        view.Document.Blocks.OfType<WpfParagraph>().Single().Inlines.OfType<WpfRun>().Single()
            .TextDecorations!.Count(decoration =>
                decoration.Location == System.Windows.TextDecorationLocation.Strikethrough)
            .Should().Be(2);
    }
}
