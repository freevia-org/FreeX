using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for DocumentView's per-property paragraph style cascade: a paragraph that sets some direct
/// formatting (e.g. alignment) but leaves spacing unset must inherit the style's spacing, rather than the
/// old all-or-nothing rule that fell back to FreeW's hardcoded 8pt-after default. Runs on STA.
/// </summary>
public sealed class ParagraphStyleCascadeTests
{
    private const double PxPerPoint = 96.0 / 72.0;

    [StaFact]
    public void StyledParagraph_WithDirectAlignment_InheritsStyleSpacing()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Styles["Quote"] = new DocumentStyle
        {
            Id = "Quote",
            Name = "Quote",
            Type = StyleType.Paragraph,
            Paragraph = ParagraphFormatting.Default with { SpaceAfterPt = 24, SpaceAfterIsSet = true },
        };
        // The paragraph sets alignment (non-default) but leaves space-after at the model default — under the
        // old all-or-nothing rule it would render at 8pt; the cascade inherits the style's explicit 24pt.
        doc.Blocks.Add(new Paragraph("styled text")
        {
            StyleId = "Quote",
            Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Center },
        });

        var view = new DocumentView();
        view.LoadModel(doc);

        var wpf = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        Assert.Equal(24 * PxPerPoint, wpf.Margin.Bottom, 1);
        Assert.Equal(System.Windows.TextAlignment.Center, wpf.TextAlignment);
    }

    [StaFact]
    public void StyledParagraph_WithoutDirectLineSpacing_InheritsStyleLineSpacing()
    {
        var doc = TextDocument.CreateEmpty();
        doc.DefaultRun = doc.DefaultRun with { FontFamily = "Calibri", FontSizePt = 11 };
        doc.Blocks.Clear();
        doc.Styles["Body"] = new DocumentStyle
        {
            Id = "Body",
            Name = "Body",
            Type = StyleType.Paragraph,
            // The style sets 1.5-line spacing explicitly (LineSpacingIsSet).
            Paragraph = ParagraphFormatting.Default with
            {
                LineSpacing = 1.5,
                LineRule = LineSpacingRule.Multiple,
                LineSpacingIsSet = true,
            },
        };
        // The paragraph sets alignment (so it goes through the per-property cascade, not the all-default
        // fast path) but leaves line spacing unset — it must inherit the style's 1.5, not FreeW's default.
        doc.Blocks.Add(new Paragraph("body text")
        {
            StyleId = "Body",
            Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Center },
        });

        var view = new DocumentView();
        view.LoadModel(doc);

        var wpf = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        var ratio = new System.Windows.Media.FontFamily("Calibri").LineSpacing;
        Assert.Equal(1.5 * ratio * 11 * PxPerPoint, wpf.LineHeight, 1);
    }

    [StaFact]
    public void DirectSpacing_WinsOverStyle()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Styles["Quote"] = new DocumentStyle
        {
            Id = "Quote",
            Name = "Quote",
            Type = StyleType.Paragraph,
            Paragraph = ParagraphFormatting.Default with { SpaceAfterPt = 24, SpaceAfterIsSet = true },
        };
        doc.Blocks.Add(new Paragraph("styled text")
        {
            StyleId = "Quote",
            Formatting = ParagraphFormatting.Default with { SpaceAfterPt = 4, SpaceAfterIsSet = true }, // explicit
        });

        var view = new DocumentView();
        view.LoadModel(doc);

        var wpf = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        Assert.Equal(4 * PxPerPoint, wpf.Margin.Bottom, 1);
    }

    [StaFact]
    public void CreateParagraphStyleAndApply_AddsStyle_AppliesIt_AndUndoRevertsBoth()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("styled text"));

        var view = new DocumentView();
        view.LoadModel(doc);

        var created = view.CreateParagraphStyleAndApply(
            "Callout",
            basedOnId: "Normal",
            RunFormatting.Default with { Bold = true, FontSizePt = 16 },
            ParagraphFormatting.Default,
            nextStyleId: "Normal");

        Assert.NotNull(created);
        Assert.Equal("Callout", created.Id);
        Assert.True(view.Model.Styles.ContainsKey("Callout"));
        Assert.Equal("Callout", ((Paragraph)view.Model.Blocks[0]).StyleId);

        Assert.True(view.Commands.Undo());
        Assert.False(view.Model.Styles.ContainsKey("Callout"));
        Assert.Null(((Paragraph)view.Model.Blocks[0]).StyleId);
    }
}
