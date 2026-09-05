using System.Reflection;
using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r425: text-run properties on a shape must survive a .pptx round trip.
///
/// <para>The FreeP counterpart to r414 (FreeW runs) and r415 (FreeX cell styles), completing the
/// character-formatting sweep across all three apps. Text on a slide is what the audience reads, and
/// a dropped run property is invisible to the author until the deck is projected -- by which point
/// the file has been saved over the original many times.</para>
///
/// <para>Nearly every property here is NULLABLE with a null default, which changes what a probe has
/// to do: setting a bool? to true and reading back null is a real loss, but so is reading back false,
/// and a sweep comparing only "is it truthy" would miss the second. Values are compared exactly.</para>
/// </summary>
public sealed class R425_ShapeTextFormattingReachesTheFileTests
{
    private static Run? RoundTrip(Action<Run> configure)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id = 2,
            Name = "Body",
            OffsetXEmu = 100000,
            OffsetYEmu = 200000,
            ExtentCxEmu = 3000000,
            ExtentCyEmu = 1000000,
            TextBody = new TextBody(),
        };

        var paragraph = new Paragraph();
        var run = new Run { Text = "sample" };
        configure(run);
        paragraph.Runs.Add(run);
        shape.TextBody!.Paragraphs.Add(paragraph);
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        return PptxPackageReader.Read(stream).Slides[0].Shapes[0].TextBody?.Paragraphs
            .FirstOrDefault()?.Runs.FirstOrDefault();
    }

    [Fact]
    public void TheTextItselfSurvives()
    {
        // The control for everything below: if the run did not round-trip at all, every property
        // comparison would fail for one shared reason and the detail would mislead.
        RoundTrip(_ => { })!.Text.Should().Be("sample", "the run must survive before its properties can be judged");
    }

    [Fact]
    public void EverySimpleRunPropertySurvivesAPptxRoundTrip()
    {
        var properties = typeof(Run).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property is { CanRead: true, CanWrite: true })
            .Where(property => property.PropertyType == typeof(bool?) || property.PropertyType == typeof(int?) ||
                               property.PropertyType == typeof(string))
            .Where(property => property.Name != nameof(Run.Text))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToList();

        properties.Should().HaveCountGreaterThanOrEqualTo(
            10, "the query must still reach the run model rather than quietly matching little");

        var lost = new List<string>();
        var exercised = 0;

        foreach (var property in properties)
        {
            // Token properties carry format-defined vocabularies, so an arbitrary string would be
            // rejected on write and read back null for a legitimate reason -- a false positive.
            object? value = property.PropertyType switch
            {
                var type when type == typeof(bool?) => true,
                var type when type == typeof(int?) => 150,
                var type when property.Name.Contains("Token", StringComparison.Ordinal) =>
                    property.Name.Contains("Underline", StringComparison.Ordinal) ? "sng" : "sngStrike",
                var type when property.Name.Contains("Language", StringComparison.Ordinal) => "fr-FR",
                _ => "probe",
            };

            if (value is null)
                continue;

            var run = RoundTrip(target => property.SetValue(target, value));
            exercised++;

            if (run is null || !Equals(property.GetValue(run), value))
                lost.Add($"{property.Name}: wrote {value}, read {(run is null ? "(no run)" : property.GetValue(run)?.ToString() ?? "(null)")}");
        }

        exercised.Should().BeGreaterThanOrEqualTo(
            10, "the sweep must actually be setting and comparing properties, not skipping them");

        lost.Should().BeEmpty(
            "a run property the writer drops is invisible to the author until the deck is projected:\n" +
            string.Join("\n", lost));
    }

    [Fact]
    public void APlainRunGainsNoFormatting()
    {
        // Every assertion above checks that something SET survives; without this, a reader that
        // invented values would satisfy them all.
        var run = RoundTrip(_ => { })!;

        run.Language.Should().BeNull("a run with no language must not acquire one");
        run.CharacterSpacingHundredthsPt.Should().BeNull("a run with no spacing must not acquire one");
        run.UnderlineStyleToken.Should().BeNull("a run with no underline must not acquire one");
    }
}
