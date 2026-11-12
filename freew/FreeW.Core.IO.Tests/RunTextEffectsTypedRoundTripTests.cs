namespace FreeW.Core.IO.Tests;

/// <summary>
/// Effects authored through the typed layer (what the Text Effects gallery produces) must be written
/// as w14 XML that the reader stores back as the very same strings, so an edited run compares equal to
/// itself after a save and reload and repeated saves stay byte-stable.
/// </summary>
public sealed class RunTextEffectsTypedRoundTripTests
{
    [Fact]
    public void TypedEffects_ReloadEqual_AndStayInterpretable()
    {
        var effects = ((RunTextEffects?)null)
            .WithGlow(new TextGlowEffect(TextEffectUnits.PointsToEmu(8), TextEffectColor.FromScheme("accent1", 60)))
            .WithShadow(new TextShadowEffect
            {
                BlurRadiusEmu = 50800,
                DistanceEmu = 38100,
                DirectionAngle = TextEffectUnits.DegreesToAngle(45),
                Color = TextEffectColor.FromRgb("000000", 60),
            })
            .WithReflection(new TextReflectionEffect { BlurRadiusEmu = 6350, StartOpacity = 50000, EndPosition = 35000 })
            .WithOutline(new TextOutlineEffect
            {
                WidthEmu = 12700,
                Fill = TextFillEffect.Solid(TextEffectColor.FromScheme("accent2")),
                Dash = "sysDot",
            })
            .WithFill(TextFillEffect.LinearGradient(
                90,
                new TextGradientStop(0, TextEffectColor.FromScheme("accent1").With("lumMod", 60000)),
                new TextGradientStop(100000, TextEffectColor.FromRgb("FFC000"))))!;
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Styled", new RunFormatting { TextEffects = effects }));
        document.Blocks.Add(paragraph);

        var first = Save(document);
        var reloaded = DocxReader.Read(new MemoryStream(first));
        var reloadedEffects = reloaded.Paragraphs.SelectMany(p => p.Runs).Single(run => run.Text == "Styled").Formatting.TextEffects;

        reloadedEffects.Should().Be(effects);
        reloadedEffects.GetGlow()!.RadiusPt.Should().Be(8);
        reloadedEffects.GetShadow()!.DirectionDegrees.Should().Be(45);
        reloadedEffects.GetOutline()!.Dash.Should().Be("sysDot");
        reloadedEffects.GetFill()!.Stops.Should().HaveCount(2);
        reloadedEffects.GetReflection()!.EndPosition.Should().Be(35000);
        Save(reloaded).Should().Equal(first);
    }

    private static byte[] Save(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }
}
