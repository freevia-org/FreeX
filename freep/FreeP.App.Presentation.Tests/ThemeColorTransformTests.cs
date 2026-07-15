using System.Xml.Linq;
using Free.Shared.Drawing;
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
        DrawingMlColorTransform.ApplyTint(new DrawingMlRgbColor(101, 151, 201), 0.5)
            .Should().Be(new DrawingMlRgbColor(178, 203, 228));
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

    [Fact]
    public void ThemeColorTransform_AdaptsSharedDrawingMlTransform()
    {
        var baseColor = new SrgbColor(96, 128, 160);
        var sharedBaseColor = new DrawingMlRgbColor(baseColor.R, baseColor.G, baseColor.B);
        var sharedResolved = DrawingMlColorTransform.Apply(
            sharedBaseColor,
            lumMod: 0.7,
            lumOff: 0.1,
            tint: 0.8,
            shade: 0.6);

        ThemeColorTransform.Apply(baseColor, 0.7, 0.1, 0.8, 0.6)
            .Should().Be(new SrgbColor(sharedResolved.R, sharedResolved.G, sharedResolved.B));
    }

    [Fact]
    public void OutlineReader_ZeroWidthLine_IsNoOutline()
    {
        var outline = PptxColorReader.TryReadOutline(
            new XElement(A + "ln", new XAttribute("w", "0")),
            PresentationColorScheme.CreateDefault());

        outline.Should().BeSameAs(ShapeOutline.None.Instance);
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

        var transformSource = File.ReadAllText(Path.Combine(root, "freep", "FreeP.Core.Model", "ThemeColorTransform.cs"));
        transformSource.Should().Contain("DrawingMlColorTransform.Apply(");
        transformSource.Should().Contain("DrawingMlColorTransform.ApplyLuminance");
        transformSource.Should().Contain("DrawingMlColorTransform.ApplyTint");
        transformSource.Should().Contain("DrawingMlColorTransform.ApplyShade");
        transformSource.Should().NotContain("RgbToHls(");

        readerSource.Should().NotContain("ApplyLumModOff(");
        resolverSource.Should().NotContain("ApplyLumModOff(");
        readerSource.Should().NotContain("private static SrgbColor ApplyTint");
        resolverSource.Should().NotContain("private static SrgbColor ApplyTint");
        readerSource.Should().NotContain("private static SrgbColor ApplyShade");
        resolverSource.Should().NotContain("private static SrgbColor ApplyShade");
    }

    [Theory]
    [InlineData("tx1", ThemeColorSlot.Dk1, DrawingMlThemeColorSlot.Dark1)]
    [InlineData("bg1", ThemeColorSlot.Lt1, DrawingMlThemeColorSlot.Light1)]
    [InlineData("accent6", ThemeColorSlot.Accent6, DrawingMlThemeColorSlot.Accent6)]
    [InlineData("folHlink", ThemeColorSlot.FolHLink, DrawingMlThemeColorSlot.FollowedHyperlink)]
    public void ThemeColorSlotMapper_AdaptsSharedOfficeRoles(
        string roleName,
        ThemeColorSlot expectedSlot,
        DrawingMlThemeColorSlot expectedSharedSlot)
    {
        DrawingMlThemeColorSlotMapper.TryMapRole(roleName, out var sharedSlot).Should().BeTrue();
        sharedSlot.Should().Be(expectedSharedSlot);

        ThemeColorSlotMapper.TryMapRole(roleName, out var slot).Should().BeTrue();
        slot.Should().Be(expectedSlot);
    }

    [Fact]
    public void ThemeColorConsumers_UseSharedRoleMapHelper()
    {
        var root = FindRepositoryRoot();
        var readerSource = File.ReadAllText(Path.Combine(root, "freep", "FreeP.Core.IO", "PptxColorReader.cs"));
        var resolverSource = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Presentation", "ThemeColorResolver.cs"));

        readerSource.Should().Contain("ThemeColorSlotMapper.TryMapRole(");
        readerSource.Should().Contain("ThemeColorSlotMapper.ToSchemeColorString(");
        resolverSource.Should().Contain("ThemeColorSlotMapper.MapRoleToSlot(");
        resolverSource.Should().NotContain("DefaultClrMap");
        readerSource.Should().NotContain("\"tx1\" or");

        var mapperSource = File.ReadAllText(Path.Combine(root, "freep", "FreeP.Core.Model", "ThemeColorSlotMapper.cs"));
        mapperSource.Should().Contain("DrawingMlThemeColorSlotMapper.TryMapRole");
        mapperSource.Should().Contain("DrawingMlThemeColorSlotMapper.MapRoleToSlot");
        mapperSource.Should().Contain("DrawingMlThemeColorSlotMapper.ToSchemeColorValue");
        mapperSource.Should().NotContain("DefaultRoleMap");
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
