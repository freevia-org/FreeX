using System.Windows;
using System.Windows.Documents;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;
using ModelParagraph = FreeP.Core.Model.Paragraph;
using ModelRun = FreeP.Core.Model.Run;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfRun = System.Windows.Documents.Run;

namespace FreeP.App.Host.Tests;

public sealed class RightToLeftTextEditorTests
{
    private const string HebrewSample = "\u05D0\u05D1\u05D2";

    private static TextBody BodyWithDirections() => new()
    {
        Paragraphs =
        {
            new ModelParagraph { RightToLeft = true, Runs = { new ModelRun { Text = HebrewSample } } },
            new ModelParagraph { RightToLeft = false, Runs = { new ModelRun { Text = "abc" } } },
            new ModelParagraph { Runs = { new ModelRun { Text = "inherit" } } },
        },
    };

    [StaFact]
    public void WpfFlowDocument_ConsumesExplicitDirection_AndPreservesAbsentDirection()
    {
        var source = BodyWithDirections();

        var document = TextBodyFlowDocumentConverter.ToFlowDocument(source);

        document.Blocks.OfType<WpfParagraph>()
            .Select(paragraph => paragraph.FlowDirection)
            .Should().ContainInOrder(
                FlowDirection.RightToLeft,
                FlowDirection.LeftToRight,
                FlowDirection.LeftToRight);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(document, source);
        restored.Paragraphs.Select(paragraph => paragraph.RightToLeft)
            .Should().ContainInOrder(true, false, null);
    }

    [StaFact]
    public void WpfFlowDocument_NewRtlParagraphWithoutSource_BecomesExplicitRtl()
    {
        var document = new FlowDocument();
        document.Blocks.Add(new WpfParagraph
        {
            FlowDirection = FlowDirection.RightToLeft,
            Inlines = { new WpfRun(HebrewSample) },
        });

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(document);

        restored.Paragraphs.Single().RightToLeft.Should().BeTrue();
    }

    [StaFact]
    public void WpfFlowDocument_InheritedDirection_RemainsAbsentWhenUnchanged()
    {
        var source = new TextBody
        {
            DefaultParaRightToLeft = true,
            Paragraphs =
            {
                new ModelParagraph { Runs = { new ModelRun { Text = HebrewSample } } },
            },
        };

        var document = TextBodyFlowDocumentConverter.ToFlowDocument(source);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(document, source);

        restored.DefaultParaRightToLeft.Should().BeTrue();
        restored.Paragraphs.Single().RightToLeft.Should().BeNull();
    }
}
