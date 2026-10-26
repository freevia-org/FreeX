using System.Reflection;
using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r413: every simple property a user can change on a shape must survive a save and reload.
///
/// <para>Generalised from r412, where rotating or flipping a shape that inherited its geometry wrote
/// nothing at all -- the edit lived in memory and vanished on save. That bug was found one property
/// at a time; this finds the whole class in one pass, and covers properties added later without
/// anyone remembering to test them.</para>
///
/// <para>Driven by reflection over <see cref="SlideShape"/>'s writable bool/long/double/string/enum
/// properties: set a distinctive value, write the deck, read it back, compare. Exclusions are named
/// and justified below rather than filtered silently -- a skip list nobody can see is how a sweep
/// quietly stops covering things.</para>
/// </summary>
public sealed class R413_EveryShapePropertyReachesTheFileTests
{
    /// <summary>
    /// Properties the pptx round trip is not expected to carry, each for a stated reason. Anything
    /// NOT listed here must survive, so adding to this list is a deliberate act.
    /// </summary>
    private static readonly Dictionary<string, string> Excluded = new(StringComparer.Ordinal)
    {
        // Preserved verbatim for the legacy .fxp format's byte-stable round trip (see FxpFormat);
        // it describes an fxp shape kind and has no pptx representation.
        ["LegacyFxpKind"] = "fxp-only preservation field, not a pptx concept",

        // Written and read only on the PICTURE path, which an auto-shape fixture cannot exercise.
        // Covered directly by PictureFrameGeometrySurvivesOnAPictureShape below.
        ["PictureFrameGeometry"] = "picture-only; covered by its own test",
    };

    private static Presentation Deck(Action<SlideShape> edit)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var shape = new SlideShape { Id = 2, Name = "Probe", TextBody = new TextBody() };

        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = "text" });
        shape.TextBody!.Paragraphs.Add(paragraph);

        edit(shape);
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);
        return presentation;
    }

    private static SlideShape RoundTrip(Presentation presentation)
    {
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        return PptxPackageReader.Read(stream).Slides[0].Shapes[0];
    }

    [Fact]
    public void EverySimpleShapePropertySurvivesASaveAndReload()
    {
        var properties = typeof(SlideShape).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property is { CanRead: true, CanWrite: true })
            .Where(property => property.PropertyType == typeof(bool) || property.PropertyType == typeof(long) ||
                               property.PropertyType == typeof(double) || property.PropertyType == typeof(string) ||
                               property.PropertyType.IsEnum)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToList();

        properties.Should().HaveCountGreaterThanOrEqualTo(
            15,
            "the reflection query must still reach the shape's properties -- a smaller number means " +
            "it stopped covering them rather than that the model shrank");

        var lost = new List<string>();

        foreach (var property in properties)
        {
            if (Excluded.ContainsKey(property.Name))
                continue;

            object? value = property.PropertyType switch
            {
                var type when type == typeof(bool) => true,
                var type when type == typeof(long) => 123456L,
                var type when type == typeof(double) => 33.5d,
                var type when type == typeof(string) => "probe-value",
                _ => Enum.GetValues(property.PropertyType).Cast<object>().Skip(1).FirstOrDefault(),
            };

            if (value is null)
                continue; // single-member enum: no value distinct from the default to test with

            var reloaded = RoundTrip(Deck(shape => property.SetValue(shape, value)));
            if (!Equals(property.GetValue(reloaded), value))
                lost.Add($"{property.Name}: wrote {value}, read {property.GetValue(reloaded) ?? "(null)"}");
        }

        lost.Should().BeEmpty(
            "a shape property the writer drops is an edit the user makes, sees applied, and loses on " +
            "reopen with no error -- exactly the r412 defect:\n" + string.Join("\n", lost));
    }

    [Fact]
    public void PictureFrameGeometrySurvivesOnAPictureShape()
    {
        // The other half of the exclusion above: excluded from the sweep because an auto-shape cannot
        // carry it, NOT because it is unsupported. Without this the exclusion would be hiding it.
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Pic",
            Kind = SlideShapeKind.Picture,
            ExtentCxEmu = 1_000_000,
            ExtentCyEmu = 1_000_000,
            Picture = new ImagePart { Bytes = new byte[64], ContentType = "image/png" },
            PictureFrameGeometry = "ellipse",
        });
        presentation.Slides.Add(slide);

        RoundTrip(presentation).PictureFrameGeometry
            .Should().Be("ellipse", "a picture cropped to a shape must keep that shape on reload");
    }
}
