using System.Xml.Linq;
using FluentAssertions;
using FreeW.Core.IO;
using Xunit;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r352: every SmartArt layout template loaded with a category must have the element that carries
/// it.
///
/// <para>The writer stamps the diagram's gallery category into <c>dgm:catLst/dgm:cat</c> of an
/// embedded template. That was applied through a <c>?.</c> chain, so a template authored without a
/// <c>catLst</c> would have kept the stock category silently and filed the diagram under the wrong
/// gallery in Word. All four templates on that path carry one today, which is why the writer now
/// throws instead of skipping -- the guard can only fire on a template added later.</para>
///
/// <para>This test is the other half: it keeps the guard from EVER firing in front of a user, by
/// failing here first if a template loses its category element or a new one arrives without it. A
/// guard without a tripwire just moves a silent bug to a crash.</para>
/// </summary>
public sealed class R352_SmartArtLayoutTemplatesCarryACategoryTests
{
    private static readonly XNamespace Dgm =
        "http://schemas.openxmlformats.org/drawingml/2006/diagram";

    [Theory]
    [InlineData("process1.xml")]
    [InlineData("orgChart1.xml")]
    [InlineData("hierarchy1.xml")]
    [InlineData("pyramid1.xml")]
    public void ATemplateLoadedWithACategoryCanCarryOne(string fileName)
    {
        var resourceName = "FreeW.Core.IO.SmartArtLayoutTemplates." + fileName;
        using var stream = typeof(DocxWriter).Assembly.GetManifestResourceStream(resourceName);
        stream.Should().NotBeNull(resourceName);

        var root = XDocument.Load(stream!).Root;
        root.Should().NotBeNull(resourceName);

        // Deliberately NOT `root.Element(catLst)?.Element(cat).Should().NotBeNull()`: that chain
        // short-circuits to null when catLst is absent, so the assertion never runs and the test
        // passes on exactly the input it exists to catch. The first draft of this test did that and
        // went green against a template with no catLst at all -- the same silent-no-op shape as the
        // defect it guards.
        var catLst = root!.Element(Dgm + "catLst");
        catLst.Should().NotBeNull("{0} must have a dgm:catLst", fileName);
        catLst!.Element(Dgm + "cat").Should().NotBeNull(
            "{0} is loaded with a gallery category, which is written into dgm:catLst/dgm:cat",
            fileName);
    }
}
