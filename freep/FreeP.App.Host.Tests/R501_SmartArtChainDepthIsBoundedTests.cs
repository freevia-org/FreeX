using System.IO;
using System.Text;
using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// r501: a SmartArt hierarchy's depth is chosen by the FILE, so rebuilding it must be bounded.
///
/// <para>A diagram's tree is reconstructed by recursing over its parent-of connections. The reader
/// already refused a CYCLE - a path set stops an id being revisited - but nothing bounded DEPTH, so
/// a chain of points recursed once per link. A 5,000-node chain is a small file, a few dozen bytes
/// per point, and opening it overflowed the stack. StackOverflowException cannot be caught: the
/// process dies with no error, no recovery and no autosave.</para>
///
/// <para>Confirmed the hard way before the fix - the test run did not fail, it ABORTED, with the
/// stack trace repeating BuildNode. That is also why this suite contains no neuter: removing the
/// guard does not turn a test red, it kills the host process. The impossibility of neutering safely
/// is the severity argument, not an omission.</para>
///
/// <para>The bound is 64, the same ceiling and the same reasoning as MaxShapeGroupNestingDepth in
/// this very reader, which already guards the shape tree. SmartArt was simply missed.</para>
/// </summary>
public sealed class R501_SmartArtChainDepthIsBoundedTests
{
    private static byte[] ChainDataModel(int length)
    {
        var builder = new StringBuilder();
        builder.Append("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\">");
        builder.Append("<dgm:ptLst>");

        for (var index = 0; index < length; index++)
        {
            builder.Append($"<dgm:pt modelId=\"n{index}\"><dgm:t>")
                   .Append("<a:p xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">")
                   .Append($"<a:r><a:t>n{index}</a:t></a:r></a:p></dgm:t></dgm:pt>");
        }

        builder.Append("</dgm:ptLst><dgm:cxnLst>");

        for (var index = 0; index < length - 1; index++)
        {
            builder.Append($"<dgm:cxn modelId=\"c{index}\" type=\"parOf\" ")
                   .Append($"srcId=\"n{index}\" destId=\"n{index + 1}\" srcOrd=\"0\" destOrd=\"0\"/>");
        }

        builder.Append("</dgm:cxnLst></dgm:dataModel>");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static Presentation RoundTripChain(int length)
    {
        var smartArt = new SmartArtShape();
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            PartPath = "ppt/diagrams/data1.xml",
            Bytes = ChainDataModel(length),
        };
        smartArt.DiagramRelIds["dm"] = "rId2";

        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 5,
            Kind = SlideShapeKind.SmartArt,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
            SmartArt = smartArt,
        });

        var presentation = new Presentation();
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        return PptxPackageReader.Read(stream);
    }

    private static int Depth(SmartArtNode node)
    {
        var depth = 1;
        var current = node;

        while (current.Children.Count > 0)
        {
            current = current.Children[0];
            depth++;
        }

        return depth;
    }

    private static SmartArtNode RootOf(Presentation presentation) =>
        presentation.Slides[0].Shapes[0].SmartArt!.Data!.Nodes.Should().ContainSingle().Subject;

    [Fact]
    public void AnOrdinaryHierarchyKeepsEveryLevel()
    {
        // Narrowness first: the bound must not touch a diagram anyone would actually author.
        Depth(RootOf(RoundTripChain(10))).Should().Be(10, "a ten-level diagram is ordinary and must survive intact");
    }

    [Fact]
    public void AChainFarDeeperThanTheBoundOpensInsteadOfKillingTheProcess()
    {
        // Before the fix this did not fail -- it aborted the test run with a stack overflow.
        var depth = Depth(RootOf(RoundTripChain(5000)));

        depth.Should().BeLessThan(100, "the chain must stop being followed near the bound, not at its 5,000th link");
        depth.Should().BeGreaterThan(60, "and it must still read the levels up to that bound rather than giving up early");
    }
}
