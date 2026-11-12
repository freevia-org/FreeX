using System.Xml.Linq;

public class MathDocumentSettingsTests
{
    private const string Namespaces =
        "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" "
        + "xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"";

    [Fact]
    public void AbsentMathProperties_AreWordsDefaults()
    {
        var settings = MathDocumentSettings.FromSettings(XElement.Parse($"<w:settings {Namespaces}/>"));

        settings.Should().Be(MathDocumentSettings.Default);
        settings.NAryLimitLocation.Should().Be(MathLimitLocation.UnderOver);
        settings.IntegralLimitLocation.Should().Be(MathLimitLocation.SubSup);
        settings.DisplayDefaults.Should().BeTrue();
        settings.SmallFractions.Should().BeFalse();
        MathDocumentSettings.FromSettings(null).Should().Be(MathDocumentSettings.Default);
    }

    [Fact]
    public void ReadsLimitPlacementDisplayDefaultsAndSmallFractions()
    {
        var settings = MathDocumentSettings.FromSettings(XElement.Parse(
            $"<w:settings {Namespaces}><m:mathPr><m:smallFrac/><m:dispDef m:val=\"0\"/>"
            + "<m:intLim m:val=\"undOvr\"/><m:naryLim m:val=\"subSup\"/></m:mathPr></w:settings>"));

        settings.NAryLimitLocation.Should().Be(MathLimitLocation.SubSup);
        settings.IntegralLimitLocation.Should().Be(MathLimitLocation.UnderOver);
        settings.DisplayDefaults.Should().BeFalse();
        settings.SmallFractions.Should().BeTrue();
    }
}
