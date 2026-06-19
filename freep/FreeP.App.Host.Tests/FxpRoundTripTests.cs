using System.IO;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Coverage for the stub <c>.fxp</c> reader/writer: a presentation written, read back, and re-written must be
/// byte-identical (the round-trip invariant the host relies on), and the model content must survive the trip.
/// </summary>
public sealed class FxpRoundTripTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeP.FxpTests", Guid.NewGuid().ToString("N"));

    public FxpRoundTripTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private static Presentation SamplePresentation()
    {
        var presentation = new Presentation();
        presentation.Properties.Title = "Quarterly Review";
        presentation.Properties.Author = "Ada Lovelace";
        presentation.Properties.Keywords = "q3, revenue";

        var slide1 = new Slide { Id = "slide-1", Title = "Agenda" };
        slide1.Shapes.Add(new SlideShape { Kind = "text", Text = "Welcome" });
        slide1.Shapes.Add(new SlideShape { Kind = "rectangle", Text = "" });
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
        reloaded.Slides.Should().HaveCount(2);
        reloaded.Slides[0].Id.Should().Be("slide-1");
        reloaded.Slides[0].Title.Should().Be("Agenda");
        reloaded.Slides[0].Shapes.Should().HaveCount(2);
        reloaded.Slides[0].Shapes[0].Text.Should().Be("Welcome");
        reloaded.Slides[0].Shapes[1].Kind.Should().Be("rectangle");
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
