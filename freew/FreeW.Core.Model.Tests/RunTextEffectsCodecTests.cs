using System.Xml.Linq;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// The typed interpretation of the stored w14 text-effect XML: what Word writes must read into the
/// expected values, edits must serialise back to schema-ordered w14 XML that reads back equal, and
/// out-of-range or malformed input must be refused rather than guessed at.
/// </summary>
public sealed class RunTextEffectsCodecTests
{
    private const string Ns = "xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\"";
    private static readonly XNamespace W14 = RunTextEffects.Namespace;

    private const string WordGlow =
        $"<w14:glow w14:rad=\"63500\" {Ns}><w14:schemeClr w14:val=\"accent1\"><w14:alpha w14:val=\"60000\"/><w14:satMod w14:val=\"175000\"/></w14:schemeClr></w14:glow>";

    private const string WordShadow =
        $"<w14:shadow w14:blurRad=\"38100\" w14:dist=\"19050\" w14:dir=\"2700000\" w14:sx=\"100000\" w14:sy=\"100000\" w14:kx=\"0\" w14:ky=\"0\" w14:algn=\"tl\" {Ns}><w14:schemeClr w14:val=\"dk1\"><w14:alpha w14:val=\"60000\"/></w14:schemeClr></w14:shadow>";

    private const string WordReflection =
        $"<w14:reflection w14:blurRad=\"6350\" w14:stA=\"55000\" w14:stPos=\"0\" w14:endA=\"300\" w14:endPos=\"45500\" w14:dist=\"0\" w14:dir=\"5400000\" w14:fadeDir=\"5400000\" w14:sx=\"100000\" w14:sy=\"-100000\" w14:kx=\"0\" w14:ky=\"0\" w14:algn=\"bl\" {Ns}/>";

    private const string WordOutline =
        $"<w14:textOutline w14:w=\"9525\" w14:cap=\"rnd\" w14:cmpd=\"sng\" w14:algn=\"ctr\" {Ns}><w14:solidFill><w14:srgbClr w14:val=\"FF0000\"/></w14:solidFill><w14:prstDash w14:val=\"sysDash\"/><w14:bevel/></w14:textOutline>";

    private const string WordGradientFill =
        $"<w14:textFill {Ns}><w14:gradFill><w14:gsLst><w14:gs w14:pos=\"0\"><w14:srgbClr w14:val=\"00B0F0\"/></w14:gs><w14:gs w14:pos=\"100000\"><w14:schemeClr w14:val=\"accent2\"><w14:lumMod w14:val=\"75000\"/></w14:schemeClr></w14:gs></w14:gsLst><w14:lin w14:ang=\"5400000\" w14:scaled=\"0\"/></w14:gradFill></w14:textFill>";

    [Fact]
    public void Glow_ReadsRadiusColourAndTransparency()
    {
        var glow = RunTextEffectsCodec.ReadGlow(WordGlow)!;

        glow.RadiusEmu.Should().Be(63500);
        glow.RadiusPt.Should().Be(5);
        glow.Color.SchemeName.Should().Be("accent1");
        glow.Color.TransparencyPercent.Should().Be(60);
        glow.Color.Transforms.Select(transform => transform.Name).Should().Equal("alpha", "satMod");
    }

    [Fact]
    public void Shadow_ReadsBlurDistanceAngleAndColour()
    {
        var shadow = RunTextEffectsCodec.ReadShadow(WordShadow)!;

        shadow.BlurRadiusPt.Should().Be(3);
        shadow.DistancePt.Should().Be(1.5);
        shadow.DirectionDegrees.Should().Be(45);
        shadow.Alignment.Should().Be("tl");
        shadow.Color.SchemeName.Should().Be("dk1");
        shadow.Color.TransparencyPercent.Should().Be(60);
    }

    [Fact]
    public void Reflection_ReadsBlurFadeDistanceAndSize()
    {
        var reflection = RunTextEffectsCodec.ReadReflection(WordReflection)!;

        reflection.BlurRadiusPt.Should().Be(0.5);
        reflection.StartOpacity.Should().Be(55000);
        reflection.EndOpacity.Should().Be(300);
        reflection.EndPosition.Should().Be(45500);
        reflection.DistanceEmu.Should().Be(0);
        reflection.ScaleY.Should().Be(-100000);
        reflection.Alignment.Should().Be("bl");
    }

    [Fact]
    public void Outline_ReadsWidthDashJoinAndFill()
    {
        var outline = RunTextEffectsCodec.ReadOutline(WordOutline)!;

        outline.WidthPt.Should().Be(0.75);
        outline.Cap.Should().Be("rnd");
        outline.Dash.Should().Be("sysDash");
        outline.Join.Should().Be(TextOutlineJoin.Bevel);
        outline.Fill!.Kind.Should().Be(TextFillKind.Solid);
        outline.Fill.Color!.RgbHex.Should().Be("FF0000");
        outline.IsInvisible.Should().BeFalse();
    }

    [Fact]
    public void GradientFill_ReadsStopsAngleAndThemeColours()
    {
        var fill = RunTextEffectsCodec.ReadFill(WordGradientFill)!;

        fill.Kind.Should().Be(TextFillKind.Gradient);
        fill.GradientKind.Should().Be(TextGradientKind.Linear);
        fill.LinearAngleDegrees.Should().Be(90);
        fill.Stops.Select(stop => stop.Position).Should().Equal(0, 100000);
        fill.Stops[0].Color.RgbHex.Should().Be("00B0F0");
        fill.Stops[1].Color.SchemeName.Should().Be("accent2");
        fill.Stops[1].Color.Transforms.Should().Equal(new TextEffectColorTransform("lumMod", 75000));
    }

    [Fact]
    public void PathGradientAndNoFill_AreInterpreted()
    {
        var path = RunTextEffectsCodec.ReadFill(
            $"<w14:textFill {Ns}><w14:gradFill><w14:gsLst><w14:gs w14:pos=\"0\"><w14:srgbClr w14:val=\"FFFFFF\"/></w14:gs><w14:gs w14:pos=\"100000\"><w14:srgbClr w14:val=\"000000\"/></w14:gs></w14:gsLst><w14:path w14:path=\"circle\"><w14:fillToRect w14:l=\"50000\" w14:t=\"50000\" w14:r=\"50000\" w14:b=\"50000\"/></w14:path></w14:gradFill></w14:textFill>")!;

        path.GradientKind.Should().Be(TextGradientKind.Circle);
        path.FillToRectLeft.Should().Be(50000);
        RunTextEffectsCodec.ReadFill($"<w14:textFill {Ns}><w14:noFill/></w14:textFill>")!.Kind.Should().Be(TextFillKind.None);
        RunTextEffectsCodec.ReadOutline($"<w14:textOutline w14:w=\"9525\" {Ns}><w14:noFill/></w14:textOutline>")!
            .IsInvisible.Should().BeTrue();
    }

    [Theory]
    [InlineData(WordGlow)]
    [InlineData(WordShadow)]
    [InlineData(WordReflection)]
    [InlineData(WordOutline)]
    [InlineData(WordGradientFill)]
    public void WordXml_SurvivesReadThenWrite_ElementForElement(string xml)
    {
        var rewritten = XElement.Parse(xml).Name.LocalName switch
        {
            "glow" => RunTextEffectsCodec.WriteGlow(RunTextEffectsCodec.ReadGlow(xml)!),
            "shadow" => RunTextEffectsCodec.WriteShadow(RunTextEffectsCodec.ReadShadow(xml)!),
            "reflection" => RunTextEffectsCodec.WriteReflection(RunTextEffectsCodec.ReadReflection(xml)!),
            "textOutline" => RunTextEffectsCodec.WriteOutline(RunTextEffectsCodec.ReadOutline(xml)!),
            _ => RunTextEffectsCodec.WriteFill(RunTextEffectsCodec.ReadFill(xml)!),
        };

        XNode.DeepEquals(Detach(XElement.Parse(rewritten)), Detach(XElement.Parse(xml))).Should().BeTrue(rewritten);
    }

    [Fact]
    public void Written_Xml_IsCanonical_OneTrailingPrefixDeclaration_NoFormatting()
    {
        var xml = RunTextEffectsCodec.WriteGlow(new TextGlowEffect(101600, TextEffectColor.FromScheme("accent2", 60)));

        xml.Should().Be(
            "<w14:glow w14:rad=\"101600\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\">"
            + "<w14:schemeClr w14:val=\"accent2\"><w14:alpha w14:val=\"60000\" /></w14:schemeClr></w14:glow>");
    }

    [Fact]
    public void Outline_IsWrittenInSchemaOrder_FillThenDashThenJoin()
    {
        var xml = RunTextEffectsCodec.WriteOutline(new TextOutlineEffect
        {
            WidthEmu = 19050,
            Fill = TextFillEffect.Solid(TextEffectColor.FromRgb("#00ff00")),
            Dash = "dash",
            Join = TextOutlineJoin.Miter,
            MiterLimit = 800000,
        });

        var element = XElement.Parse(xml);
        element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).Select(attribute => attribute.Name.LocalName)
            .Should().Equal("w", "cap", "cmpd", "algn");
        element.Elements().Select(child => child.Name.LocalName).Should().Equal("solidFill", "prstDash", "miter");
        element.Element(W14 + "solidFill")!.Element(W14 + "srgbClr")!.Attribute(W14 + "val")!.Value.Should().Be("00FF00");
        element.Element(W14 + "miter")!.Attribute(W14 + "lim")!.Value.Should().Be("800000");
    }

    [Fact]
    public void ColourTransforms_AreWrittenInSchemaOrder_WhateverOrderTheyWereAdded()
    {
        var color = TextEffectColor.FromScheme("accent1").With("lumMod", 50000).With("alpha", 20000).With("tint", 80000);

        var xml = RunTextEffectsCodec.WriteFill(TextFillEffect.Solid(color));

        XElement.Parse(xml).Descendants(W14 + "schemeClr").Single().Elements().Select(child => child.Name.LocalName)
            .Should().Equal("tint", "alpha", "lumMod");
    }

    [Theory]
    [InlineData("<w14:glow w14:rad=\"-1\" " + Ns + "><w14:srgbClr w14:val=\"FF0000\"/></w14:glow>")]
    [InlineData("<w14:glow w14:rad=\"99999999999999999\" " + Ns + "><w14:srgbClr w14:val=\"FF0000\"/></w14:glow>")]
    [InlineData("<w14:glow w14:rad=\"1e9\" " + Ns + "><w14:srgbClr w14:val=\"FF0000\"/></w14:glow>")]
    [InlineData("<w14:glow w14:rad=\"63500\" " + Ns + "><w14:srgbClr w14:val=\"GG0000\"/></w14:glow>")]
    [InlineData("<w14:glow w14:rad=\"63500\" " + Ns + "/>")]
    [InlineData("<w14:shadow w14:rad=\"63500\" " + Ns + "/>")]
    [InlineData("<w14:glow")]
    [InlineData("")]
    [InlineData(null)]
    public void Glow_OutOfRangeOrMalformed_IsNotInterpreted(string? xml) =>
        RunTextEffectsCodec.ReadGlow(xml).Should().BeNull();

    [Fact]
    public void OutOfRangeAttributes_MakeTheEffectUninterpretable()
    {
        RunTextEffectsCodec.ReadShadow(WordShadow.Replace("w14:dir=\"2700000\"", "w14:dir=\"99999999\"")).Should().BeNull();
        RunTextEffectsCodec.ReadShadow(WordShadow.Replace("w14:algn=\"tl\"", "w14:algn=\"sideways\"")).Should().BeNull();
        RunTextEffectsCodec.ReadReflection(WordReflection.Replace("w14:stA=\"55000\"", "w14:stA=\"100001\"")).Should().BeNull();
        RunTextEffectsCodec.ReadOutline(WordOutline.Replace("w14:w=\"9525\"", "w14:w=\"20116801\"")).Should().BeNull();
        RunTextEffectsCodec.ReadFill(WordGradientFill.Replace("w14:pos=\"100000\"", "w14:pos=\"100001\"")).Should().BeNull();
    }

    [Fact]
    public void Writers_ClampValuesIntoSchemaRange_SoOutputAlwaysReadsBack()
    {
        var xml = RunTextEffectsCodec.WriteShadow(new TextShadowEffect
        {
            BlurRadiusEmu = -5,
            DistanceEmu = long.MaxValue,
            DirectionAngle = -5400000,
            Alignment = "bogus",
        });

        var shadow = RunTextEffectsCodec.ReadShadow(xml)!;
        shadow.BlurRadiusEmu.Should().Be(0);
        shadow.DirectionAngle.Should().Be(16200000);
        shadow.Alignment.Should().Be("tl");
    }

    [Fact]
    public void Colour_ResolvesThemeSlotsAndTransforms()
    {
        var scheme = DocumentTheme.Default.ColorScheme;

        TextEffectColor.FromRgb("3366FF").Resolve(scheme).Should().Be(new TextEffectArgb(255, 0x33, 0x66, 0xFF));
        TextEffectColor.FromScheme("accent4").Resolve(scheme).RgbHex.Should().Be("#FFC000");
        TextEffectColor.FromScheme("bg1").Resolve(scheme).RgbHex.Should().Be("#FFFFFF");
        TextEffectColor.FromScheme("tx1").Resolve(scheme).RgbHex.Should().Be("#000000");
        // w14:alpha is transparency: 60% transparent leaves 40% opacity.
        TextEffectColor.FromRgb("000000", 60).Resolve(scheme).A.Should().Be(102);
        TextEffectColor.FromRgb("808080").With("shade", 50000).Resolve(scheme).RgbHex.Should().Be("#404040");
        TextEffectColor.FromRgb("000000").With("tint", 50000).Resolve(scheme).RgbHex.Should().Be("#808080");
        TextEffectColor.FromRgb("FF0000").With("lumMod", 50000).Resolve(scheme).RgbHex.Should().Be("#800000");
    }

    [Fact]
    public void TypedSetters_ReplaceOneEffect_KeepTheRest_AndCollapseToNull()
    {
        var effects = new RunTextEffects { Glow = WordGlow, Props3D = "<raw/>", ContextualAlternates = "<alts/>" };

        var edited = effects.WithShadow(new TextShadowEffect { DistanceEmu = 38100 });

        edited!.Glow.Should().Be(WordGlow);
        edited.Props3D.Should().Be("<raw/>");
        edited.GetShadow()!.DistanceEmu.Should().Be(38100);
        edited.WithShadow(null).Should().Be(effects);
        ((RunTextEffects?)null).WithGlow(null).Should().BeNull();
        new RunTextEffects { Glow = WordGlow }.WithGlow(null).Should().BeNull();
    }

    [Fact]
    public void ClearVisualEffects_KeepsContextualAlternatesOnly()
    {
        var effects = new RunTextEffects
        {
            Glow = WordGlow, Shadow = WordShadow, Reflection = WordReflection, Outline = WordOutline,
            Fill = WordGradientFill, Scene3D = "<s/>", Props3D = "<p/>", ContextualAlternates = "<alts/>",
        };

        effects.HasVisualEffects().Should().BeTrue();
        effects.ClearVisualEffects().Should().Be(new RunTextEffects { ContextualAlternates = "<alts/>" });
        new RunTextEffects { Glow = WordGlow }.ClearVisualEffects().Should().BeNull();
        new RunTextEffects { ContextualAlternates = "<alts/>" }.HasVisualEffects().Should().BeFalse();
    }

    [Fact]
    public void Values_HaveValueEquality_IncludingLists()
    {
        RunTextEffectsCodec.ReadFill(WordGradientFill).Should().Be(RunTextEffectsCodec.ReadFill(WordGradientFill));
        RunTextEffectsCodec.ReadGlow(WordGlow).Should().Be(RunTextEffectsCodec.ReadGlow(WordGlow));
        TextEffectColor.FromScheme("accent1", 10).Should().NotBe(TextEffectColor.FromScheme("accent1", 20));
    }

    private static XElement Detach(XElement element)
    {
        var copy = new XElement(element);
        foreach (var node in copy.DescendantsAndSelf())
            node.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Remove();
        return copy;
    }
}
