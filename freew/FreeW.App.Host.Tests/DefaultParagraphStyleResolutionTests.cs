using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using Xunit;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfRun = System.Windows.Documents.Run;

namespace FreeW.App.Host.Tests;

/// <summary>
/// A paragraph with no <c>w:pStyle</c> inherits the package's default paragraph style
/// (<c>w:default="1"</c>) whatever its id — stress003.docx / drawing.docx use id <c>a</c> (Times New
/// Roman 14pt) and previously rendered unstyled paragraphs with only the docDefaults font.
/// </summary>
public sealed class DefaultParagraphStyleResolutionTests
{
    private static TextDocument BuildDocument(out FreeW.Core.Model.Paragraph unstyled, out FreeW.Core.Model.Paragraph styled)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Styles.Clear();
        doc.DefaultRun = RunFormatting.Default with { FontFamily = "Calibri", FontSizePt = 11 };
        doc.Styles["a"] = new DocumentStyle
        {
            Id = "a",
            Name = "Normal",
            IsDefault = true,
            Run = RunFormatting.Default with { FontFamily = "Times New Roman", FontSizePt = 14 },
            Paragraph = ParagraphFormatting.Default with
            {
                SpaceAfterPt = 0, SpaceAfterIsSet = true,
                LineSpacing = 1.0, LineRule = LineSpacingRule.Multiple, LineSpacingIsSet = true
            }
        };
        // Default styles of other kinds must never be picked for a paragraph.
        doc.Styles["a0"] = new DocumentStyle
        {
            Id = "a0", Name = "Default Paragraph Font", Type = StyleType.Character, IsDefault = true,
            Run = RunFormatting.Default with { FontFamily = "Wingdings" }
        };
        // No basedOn: inherits docDefaults only, NOT the default paragraph style (Word's cascade).
        doc.Styles["Plain"] = new DocumentStyle { Id = "Plain", Name = "Plain" };

        unstyled = new FreeW.Core.Model.Paragraph("unstyled")
        {
            Formatting = ParagraphFormatting.Default with { SpaceAfterPt = 10, LineSpacing = 1.15 }
        };
        styled = new FreeW.Core.Model.Paragraph("styled") { StyleId = "Plain" };
        doc.Blocks.Add(unstyled);
        doc.Blocks.Add(styled);
        return doc;
    }

    [Fact]
    public void UnstyledParagraph_ResolvesThroughTheDefaultParagraphStyle()
    {
        var doc = BuildDocument(out var unstyled, out var styled);

        var run = DocumentRunFormattingResolver.Resolve(doc, unstyled, unstyled.Runs[0]);
        run.FontFamily.Should().Be("Times New Roman");
        run.FontSizePt.Should().Be(14);

        var paragraph = DocumentParagraphFormattingResolver.Resolve(doc, unstyled);
        paragraph.SpaceAfterPt.Should().Be(0);
        paragraph.LineSpacing.Should().Be(1.0);

        var other = DocumentRunFormattingResolver.Resolve(doc, styled, styled.Runs[0]);
        other.FontFamily.Should().Be("Calibri");
        other.FontSizePt.Should().Be(11);
    }

    [Fact]
    public void WithoutADeclaredDefaultStyle_UnstyledParagraphsKeepDocumentDefaults()
    {
        var doc = BuildDocument(out var unstyled, out _);
        doc.Styles["a"] = new DocumentStyle
        {
            Id = "a", Name = "Normal",
            Run = RunFormatting.Default with { FontFamily = "Times New Roman", FontSizePt = 14 }
        };

        var run = DocumentRunFormattingResolver.Resolve(doc, unstyled, unstyled.Runs[0]);
        run.FontFamily.Should().Be("Calibri");
        run.FontSizePt.Should().Be(11);
    }

    [StaFact]
    public void RenderedUnstyledParagraph_UsesTheDefaultStyleFont_AndCommitKeepsTheModelUnstyled()
    {
        var doc = BuildDocument(out _, out _);
        var view = new DocumentView();
        view.LoadModel(doc);

        var wpfRun = view.Document.Blocks.OfType<WpfParagraph>().First()
            .Inlines.OfType<WpfRun>().Single(run => run.Text == "unstyled");
        wpfRun.FontFamily.Source.Should().Be("Times New Roman");
        wpfRun.FontSize.Should().BeApproximately(14 * 96.0 / 72.0, 0.01);

        view.CommitToModel();
        var committed = view.Model.Blocks.OfType<FreeW.Core.Model.Paragraph>().First();
        committed.StyleId.Should().BeNull();
        committed.Runs.Single().Formatting.FontFamily.Should().BeNull();
        committed.Runs.Single().Formatting.FontSizePt.Should().BeNull();
    }
}
