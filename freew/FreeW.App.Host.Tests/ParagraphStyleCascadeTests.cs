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
            Paragraph = ParagraphFormatting.Default with { SpaceAfterPt = 24 },
        };
        // The paragraph sets alignment (non-default) but leaves space-after at the model default — under the
        // old all-or-nothing rule it would render at 8pt; the cascade inherits the style's 24pt.
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
    public void DirectSpacing_WinsOverStyle()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Styles["Quote"] = new DocumentStyle
        {
            Id = "Quote",
            Name = "Quote",
            Type = StyleType.Paragraph,
            Paragraph = ParagraphFormatting.Default with { SpaceAfterPt = 24 },
        };
        doc.Blocks.Add(new Paragraph("styled text")
        {
            StyleId = "Quote",
            Formatting = ParagraphFormatting.Default with { SpaceAfterPt = 4 }, // explicit, non-default
        });

        var view = new DocumentView();
        view.LoadModel(doc);

        var wpf = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        Assert.Equal(4 * PxPerPoint, wpf.Margin.Bottom, 1);
    }
}
