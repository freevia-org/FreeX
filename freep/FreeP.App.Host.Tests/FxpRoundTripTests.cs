using System.IO;
using Free.Shared.Drawing;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Coverage for the <c>.fxp</c> reader/writer: a presentation written, read back, and re-written must be
/// byte-identical (the round-trip invariant the host relies on), and the model content must survive the trip.
/// </summary>
public sealed class FxpRoundTripTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.FxpTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    private static Presentation SamplePresentation()
    {
        var presentation = new Presentation();
        presentation.Properties.Title = "Quarterly Review";
        presentation.Properties.Author = "Ada Lovelace";
        presentation.Properties.Subject = "Board update";
        presentation.Properties.Keywords = "q3, revenue";
        presentation.Properties.Comments = "Prepared for leadership";
        presentation.Properties.LastModifiedBy = "Grace Hopper";
        presentation.Properties.Created = new DateTimeOffset(2026, 6, 29, 8, 0, 0, TimeSpan.Zero);
        presentation.Properties.Modified = new DateTimeOffset(2026, 6, 29, 9, 15, 0, TimeSpan.Zero);
        presentation.Properties.Category = "Business";
        presentation.Properties.ContentStatus = "Draft";
        presentation.Properties.Language = "en-US";
        presentation.Properties.Version = "2026.06";

        var slide1 = new Slide { Id = "slide-1", Title = "Agenda" };
        // Non-placeholder content shapes (what FXP serializes in the Shapes array).
        var textShape = new SlideShape { LegacyFxpKind = "text" };
        textShape.Text = "Welcome";
        slide1.Shapes.Add(textShape);

        var rectShape = new SlideShape { LegacyFxpKind = "rectangle" };
        rectShape.Text = "";
        slide1.Shapes.Add(rectShape);

        presentation.Slides.Add(slide1);
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Results" });
        return presentation;
    }

    [Fact]
    public void Write_Read_Write_IsByteIdentical()
    {
        var path1 = Path.Combine(_tempDir, "a.fxp");
        var path2 = Path.Combine(_tempDir, "b.fxp");

        FxpFormat.Write(SamplePresentation(), path1);
        var reloaded = FxpFormat.Read(path1);
        FxpFormat.Write(reloaded, path2);

        File.ReadAllBytes(path2).Should().Equal(File.ReadAllBytes(path1));
    }

    [Fact]
    public void RoundTrip_PreservesModelContent()
    {
        var path = Path.Combine(_tempDir, "deck.fxp");
        FxpFormat.Write(SamplePresentation(), path);

        var reloaded = FxpFormat.Read(path);

        reloaded.Properties.Title.Should().Be("Quarterly Review");
        reloaded.Properties.Author.Should().Be("Ada Lovelace");
        reloaded.Properties.Subject.Should().Be("Board update");
        reloaded.Properties.Keywords.Should().Be("q3, revenue");
        reloaded.Properties.Comments.Should().Be("Prepared for leadership");
        reloaded.Properties.LastModifiedBy.Should().Be("Grace Hopper");
        reloaded.Properties.Created.Should().Be(new DateTimeOffset(2026, 6, 29, 8, 0, 0, TimeSpan.Zero));
        reloaded.Properties.Modified.Should().Be(new DateTimeOffset(2026, 6, 29, 9, 15, 0, TimeSpan.Zero));
        reloaded.Properties.Category.Should().Be("Business");
        reloaded.Properties.ContentStatus.Should().Be("Draft");
        reloaded.Properties.Language.Should().Be("en-US");
        reloaded.Properties.Version.Should().Be("2026.06");
        reloaded.Slides.Should().HaveCount(2);
        reloaded.Slides[0].Id.Should().Be("slide-1");
        reloaded.Slides[0].Title.Should().Be("Agenda");

        // FXP serializes non-placeholder shapes; placeholders (title) are NOT in the shapes list.
        // After reload the title placeholder is at index 0, then non-placeholder shapes follow.
        var nonTitleShapes = reloaded.Slides[0].Shapes
            .Where(s => s.Placeholder is null)
            .ToList();
        nonTitleShapes.Should().HaveCount(2);
        nonTitleShapes[0].Text.Should().Be("Welcome");
        // The legacy kind string is preserved for byte-stable round-trips.
        nonTitleShapes[1].LegacyFxpKind.Should().Be("rectangle");

        reloaded.Slides[1].Title.Should().Be("Results");
    }

    [Fact]
    public void CreateEmpty_RoundTrips()
    {
        var path = Path.Combine(_tempDir, "empty.fxp");
        var empty = Presentation.CreateEmpty();

        FxpFormat.Write(empty, path);
        var reloaded = FxpFormat.Read(path);

        reloaded.Slides.Should().HaveCount(1);
        reloaded.Slides[0].Title.Should().Be("Slide 1");
    }
}
