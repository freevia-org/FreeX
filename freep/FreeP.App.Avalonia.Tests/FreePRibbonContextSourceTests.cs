using FreeP.App.Compositor;
using FreeP.Core.Model;
using Free.Shared.Drawing;

namespace FreeP.App.Avalonia.Tests;

public sealed class FreePRibbonContextSourceTests
{
    [Fact]
    public void Refresh_maps_selected_shapes_to_the_same_contextual_tabs_as_the_Wpf_host()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            TextBody = new TextBody(),
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 2,
            Kind = SlideShapeKind.Table,
            Table = new TableShape(),
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 3,
            Kind = SlideShapeKind.SmartArt,
            SmartArt = new SmartArtShape(),
        });
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var source = new FreePRibbonContextSource();
        var changes = 0;
        source.ContextChanged += (_, _) => changes++;

        editor.Select(1);
        source.Refresh(editor);
        source.Current.IsActive("text").Should().BeTrue();
        source.Current.IsActive("table").Should().BeFalse();
        source.Current.IsActive("smartart").Should().BeFalse();

        editor.Select(2);
        source.Refresh(editor);
        source.Current.IsActive("text").Should().BeFalse();
        source.Current.IsActive("table").Should().BeTrue();

        editor.Select(3);
        source.Refresh(editor);
        source.Current.IsActive("table").Should().BeFalse();
        source.Current.IsActive("smartart").Should().BeTrue();

        source.Refresh(editor);
        changes.Should().Be(3, "an unchanged selection context must not rebuild the ribbon");
    }

    [Fact]
    public void Avalonia_host_supplies_the_selection_context_source_to_the_shared_ribbon()
    {
        var source = TestWorkspaceFileLocator.ReadAllText("freep", "FreeP.App.Avalonia", "MainWindow.cs");

        source.Should().Contain("contextSource: _ribbonContextSource")
            .And.Contain("_ribbonContextSource.Refresh(Editor);");
    }
}
