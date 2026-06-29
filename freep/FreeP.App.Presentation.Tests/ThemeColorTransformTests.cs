using System.Xml.Linq;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ThemeColorTransformTests
{
    private static readonly XNamespace A = PptxColorReader.A;

    [Fact]
    public void ThemeColorTransform_AppliesDrawingMlFractions()
    {
        ThemeColorTransform.ApplyTint(new SrgbColor(101, 151, 201), 0.5)
            .Should().Be(new SrgbColor(178, 203, 228));
        ThemeColorTransform.ApplyTint(new SrgbColor(10, 20, 30), 0.0)
            .Should().Be(SrgbColor.White);

        ThemeColorTransform.ApplyShade(new SrgbColor(100, 150, 200), 0.5)
            .Should().Be(new SrgbColor(50, 75, 100));
        ThemeColorTransform.ApplyShade(new SrgbColor(10, 20, 30), 0.0)
            .Should().Be(SrgbColor.Black);

        ThemeColorTransform.ApplyLuminance(new SrgbColor(64, 64, 64), 0.5, 0.25)
            .Should().Be(new SrgbColor(96, 96, 96));
    }

    [Theory]
    [InlineData("50000", null, null, null)]
    [InlineData(null, "25000", null, null)]
    [InlineData(null, null, "50000", null)]
    [InlineData(null, null, null, "50000")]
    [InlineData("70000", "10000", "80000", "65000")]
    public void SchemeTransformsResolveIdenticallyThroughReaderAndResolver(
        string? lumMod,
        string? lumOff,
        string? tint,
        string? shade)
    {
        var scheme = PresentationColorScheme.CreateDefault();
        var theme = new PresentationTheme { ColorScheme = scheme };
        var parsed = PptxColorReader.TryReadColor(
            BuildSchemeFill(lumMod, lumOff, tint, shade),
            scheme);

        parsed.Should().NotBeNull();
        parsed!.SchemeColor.Should().NotBeNull();

        var schemeRef = parsed.SchemeColor!;
        var expected = ThemeColorTransform.Apply(
            scheme[ThemeColorSlot.Accent1],
            schemeRef.LumMod,
            schemeRef.LumOff,
            schemeRef.Tint,
            schemeRef.Shade);

        parsed.Resolved.Should().Be(expected, "PPTX import should apply the shared transform math");
        ThemeColorResolver.Resolve(parsed, theme).Should().Be(
            expected,
            "render-time scheme resolution should apply the same transform math");
    }

    [Fact]
    public void ThemeColorConsumers_UseSharedTransformHelper()
    {
        var root = FindRepositoryRoot();
        var readerSource = File.ReadAllText(Path.Combine(root, "freep", "FreeP.Core.IO", "PptxColorReader.cs"));
        var resolverSource = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Presentation", "ThemeColorResolver.cs"));

        readerSource.Should().Contain("ThemeColorTransform.Apply(");
        resolverSource.Should().Contain("ThemeColorTransform.Apply(");

        readerSource.Should().NotContain("ApplyLumModOff(");
        resolverSource.Should().NotContain("ApplyLumModOff(");
        readerSource.Should().NotContain("private static SrgbColor ApplyTint");
        resolverSource.Should().NotContain("private static SrgbColor ApplyTint");
        readerSource.Should().NotContain("private static SrgbColor ApplyShade");
        resolverSource.Should().NotContain("private static SrgbColor ApplyShade");
    }

    private static XElement BuildSchemeFill(string? lumMod, string? lumOff, string? tint, string? shade)
    {
        var schemeClr = new XElement(A + "schemeClr", new XAttribute("val", "accent1"));
        AddTransform(schemeClr, "lumMod", lumMod);
        AddTransform(schemeClr, "lumOff", lumOff);
        AddTransform(schemeClr, "tint", tint);
        AddTransform(schemeClr, "shade", shade);
        return new XElement(A + "solidFill", schemeClr);
    }

    private static void AddTransform(XElement schemeClr, string name, string? value)
    {
        if (value is not null)
            schemeClr.Add(new XElement(A + name, new XAttribute("val", value)));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
