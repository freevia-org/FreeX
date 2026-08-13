using Free.Shared.AppServices;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Identity + model-bus smoke tests (headless). Confirms the test assembly installs FreeP's product
/// footprint (so shared path planners resolve "FreeP") and that the presentation command bus round-trips an
/// edit through the shared undo/redo engine.
/// </summary>
public sealed class AppIdentityAndModelTests
{
    [Fact]
    public void AppProduct_IsFreeP()
    {
        AppProduct.Current.ProductDirectoryName.Should().Be("FreeP");
        AppProduct.Current.DiagnosticsEnvironmentVariable.Should().Be("FREEP_DIAGNOSTICS");
        AppProduct.Current.ProductName.Should().Be("FreeP");
    }

    [Fact]
    public void CommandBus_AddSlide_UndoRedo_RoundTrips()
    {
        var presentation = Presentation.CreateEmpty(); // starts with 1 slide
        var bus = new PresentationCommandBus(presentation);

        var changes = 0;
        bus.Changed += () => changes++;

        bus.Execute(new InsertSlideCommand(
            presentation.Slides.Count,
            new Slide { Title = "Slide 2" }));
        presentation.Slides.Should().HaveCount(2);
        bus.CanUndo.Should().BeTrue();

        bus.Undo();
        presentation.Slides.Should().HaveCount(1);
        bus.CanRedo.Should().BeTrue();

        bus.Redo();
        presentation.Slides.Should().HaveCount(2);
        presentation.Slides[1].Title.Should().Be("Slide 2");

        changes.Should().Be(3); // execute + undo + redo
    }
}
