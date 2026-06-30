using System.IO;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class TextLayoutPlannerTests
{
    [Fact]
    public void PlanTableCellText_MiddleAnchor_UsesInsetsAndMeasuredParagraphs()
    {
        var text = new ResolvedTextLayout
        {
            InsetLeftDip = 5,
            InsetTopDip = 6,
            InsetRightDip = 7,
            InsetBottomDip = 8
        };
        var bounds = new LayoutRect(10, 20, 200, 100);
        var measures = new[]
        {
            new TextParagraphMeasure(0, 20, 2, 3),
            new TextParagraphMeasure(2, 10, 1, 0)
        };

        var plan = TextLayoutPlanner.PlanTableCellText(
            text,
            bounds,
            TableCellAnchor.Middle,
            measures);

        plan.Area.Should().Be(new TextLayoutArea(15, 26, 188, 86));
        plan.Paragraphs.Should().Equal(
            new TextParagraphPlacement(0, 0, 15, 53, 188),
            new TextParagraphPlacement(2, 0, 15, 77, 188));
    }

    [Fact]
    public void GetColumnLayout_UsesDefaultSpacingAndLineSpacingScale()
    {
        var text = new ResolvedTextLayout
        {
            ColumnCount = 3,
            ColumnSpacingDip = 0,
            LnSpcReduction = 0.25,
            InsetLeftDip = 6,
            InsetRightDip = 6
        };

        var layout = TextLayoutPlanner.GetColumnLayout(
            text,
            new LayoutRect(0, 0, 396, 100));

        layout.ColumnCount.Should().Be(3);
        layout.ColumnSpacingDip.Should().Be(TextLayoutPlanner.DefaultColumnSpacingDip);
        layout.ColumnWidthDip.Should().BeApproximately((384 - 97) / 3.0, 0.001);
        layout.LineSpacingScale.Should().BeApproximately(0.75, 0.001);
    }

    [Fact]
    public void PlanColumns_GreedilyFlowsParagraphsAcrossColumns()
    {
        var text = new ResolvedTextLayout
        {
            ColumnCount = 2,
            ColumnSpacingDip = 20,
            InsetLeftDip = 10,
            InsetTopDip = 5,
            InsetRightDip = 10,
            InsetBottomDip = 5,
            Paragraphs = new[]
            {
                Paragraph(),
                Paragraph(indent: 12),
                Paragraph()
            }
        };
        var layout = TextLayoutPlanner.GetColumnLayout(text, new LayoutRect(0, 0, 300, 100));
        var measures = new[]
        {
            new TextParagraphMeasure(0, 40, 0, 0),
            new TextParagraphMeasure(1, 60, 0, 0),
            new TextParagraphMeasure(2, 20, 0, 0)
        };

        var plan = TextLayoutPlanner.PlanColumns(text, layout, measures);

        layout.ColumnWidthDip.Should().Be(130);
        plan.Paragraphs.Should().Equal(
            new TextParagraphPlacement(0, 0, 10, 5, 130),
            new TextParagraphPlacement(1, 1, 172, 5, 118),
            new TextParagraphPlacement(2, 1, 160, 65, 130));
    }

    [Fact]
    public void PlanBodyText_BottomAnchor_UsesInsetsIndentAndLineSpacingScale()
    {
        var text = new ResolvedTextLayout
        {
            Anchor = VerticalAnchor.Bottom,
            LnSpcReduction = 0.25,
            InsetLeftDip = 5,
            InsetTopDip = 6,
            InsetRightDip = 7,
            InsetBottomDip = 8,
            Paragraphs = new[]
            {
                Paragraph(),
                Paragraph(indent: 12)
            }
        };
        var measures = new[]
        {
            new TextParagraphMeasure(0, 40, 4, 8),
            new TextParagraphMeasure(1, 20, 2, 6)
        };

        var plan = TextLayoutPlanner.PlanBodyText(
            text,
            new LayoutRect(10, 20, 200, 100),
            measures);

        plan.Area.Should().Be(new TextLayoutArea(15, 26, 188, 86));
        plan.Paragraphs.Should().HaveCount(2);
        plan.Paragraphs[0].Should().Be(new TextParagraphPlacement(0, 0, 15, 35, 188));
        plan.Paragraphs[1].ParagraphIndex.Should().Be(1);
        plan.Paragraphs[1].X.Should().BeApproximately(27, 0.001);
        plan.Paragraphs[1].Y.Should().BeApproximately(72.5, 0.001);
        plan.Paragraphs[1].MaxWidthDip.Should().BeApproximately(176, 0.001);
    }

    [Fact]
    public void CreateParagraphMeasure_AppliesPointAndLineSpacingScale()
    {
        var measure = TextLayoutPlanner.CreateParagraphMeasure(
            paragraphIndex: 4,
            heightDip: 24,
            spaceBeforePt: 6,
            spaceAfterPt: 3,
            lineSpacingScale: 0.5);

        measure.Should().Be(new TextParagraphMeasure(4, 12, 4, 2));
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_DelegateTextLayoutMathToSharedPlanner()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");

        wpf.Should().Contain("TextLayoutPlanner.GetTextArea");
        wpf.Should().Contain("TextLayoutPlanner.PlanTableCellText");
        wpf.Should().Contain("TextLayoutPlanner.PlanBodyText");
        wpf.Should().Contain("TextLayoutPlanner.GetColumnLayout");
        wpf.Should().Contain("TextLayoutPlanner.PlanColumns");
        wpf.Should().NotContain("const double DefaultSpacingDip");
        wpf.Should().NotContain("TableCellAnchor.Middle => bounds.Y");
        wpf.Should().NotContain("VerticalAnchor.Middle => bounds.Y");

        avalonia.Should().Contain("TextLayoutPlanner.GetTextArea");
        avalonia.Should().Contain("TextLayoutPlanner.PlanTableCellText");
        avalonia.Should().Contain("TextLayoutPlanner.PlanBodyText");
        avalonia.Should().Contain("TextLayoutPlanner.GetColumnLayout");
        avalonia.Should().Contain("TextLayoutPlanner.PlanColumns");
        avalonia.Should().NotContain("const double DefaultSpacingDip");
        avalonia.Should().NotContain("TableCellAnchor.Middle => bounds.Y");
        avalonia.Should().NotContain("VerticalAnchor.Middle => bounds.Y");
    }

    private static ResolvedParagraph Paragraph(double indent = 0) => new()
    {
        IndentDip = indent,
        Runs = new[] { new ResolvedRun { Text = "P" } }
    };

    private static string ReadWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var parts = new string[relativeParts.Length + 1];
            parts[0] = directory.FullName;
            relativeParts.CopyTo(parts, 1);

            var candidate = Path.Combine(parts);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate workspace file.",
            Path.Combine(relativeParts));
    }
}
