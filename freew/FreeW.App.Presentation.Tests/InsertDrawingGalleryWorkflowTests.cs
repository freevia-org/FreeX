using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class InsertDrawingGalleryWorkflowTests
{
    [Fact]
    public void SharedWorkflowRegistersBothGalleriesAndLegacyAliases()
    {
        var inserted = new List<Shape>();
        var bindings = new RibbonCommandRegistry();

        InsertDrawingGalleryWorkflow.Register(
            bindings,
            new InsertDrawingGalleryPorts(inserted.Add));

        foreach (var choice in InsertDrawingGalleryWorkflow.ShapeChoices
            .Concat(InsertDrawingGalleryWorkflow.TextBoxChoices))
        {
            bindings.TryGet(choice.CommandId, out var command).Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);
        }

        inserted.Select(shape => shape.Kind).Should().Equal(
            ShapeKind.Rectangle,
            ShapeKind.RoundedRectangle,
            ShapeKind.Ellipse,
            ShapeKind.TextBox,
            ShapeKind.TextBox,
            ShapeKind.TextBox,
            ShapeKind.TextBox);

        bindings.TryGet("freew.shape", out var shapeAlias).Should().BeTrue();
        bindings.TryGet("freew.shape-rectangle", out var rectangle).Should().BeTrue();
        shapeAlias.Should().BeSameAs(rectangle);
        bindings.TryGet("freew.text-box", out var textBoxAlias).Should().BeTrue();
        bindings.TryGet("freew.shape-textbox", out var simpleTextBox).Should().BeTrue();
        textBoxAlias.Should().BeSameAs(simpleTextBox);
        bindings.TryGet("freew.shapes", out var opener).Should().BeTrue();
        opener.Should().BeSameAs(EmptyRibbonCommand.Instance);
    }

    [Fact]
    public void SharedPresetFactoryPreservesWpfAuthorityGeometryAndTextStyling()
    {
        var rectangle = InsertDrawingGalleryWorkflow.CreateShape(InsertDrawingPreset.Rectangle);
        rectangle.Kind.Should().Be(ShapeKind.Rectangle);
        rectangle.WidthPt.Should().Be(120);
        rectangle.HeightPt.Should().Be(80);
        rectangle.FillColorHex.Should().Be("#DCE6F1");

        var ellipse = InsertDrawingGalleryWorkflow.CreateShape(InsertDrawingPreset.Ellipse);
        ellipse.WidthPt.Should().Be(100);
        ellipse.HeightPt.Should().Be(100);

        var sidebar = InsertDrawingGalleryWorkflow.CreateShape(InsertDrawingPreset.SidebarTextBox);
        sidebar.FillColorHex.Should().Be("#243F60");
        sidebar.TextParagraphs.Single().Runs.Single().Should().Match<Run>(run =>
            run.Text == "Sidebar" && run.Formatting.Bold && run.Formatting.ColorHex == "#FFFFFF");

        var quote = InsertDrawingGalleryWorkflow.CreateShape(InsertDrawingPreset.QuoteTextBox);
        quote.FillColorHex.Should().Be("#F2F2F2");
        quote.TextParagraphs.Single().Runs.Single().Should().Match<Run>(run =>
            run.Text == "\u201cQuote text here\u201d" && run.Formatting.Italic);
    }

    [Fact]
    public void BothRenderersDelegateGalleryConstructionToSharedPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));

        wpf.Should().Contain("InsertDrawingGalleryWorkflow.Register(");
        avalonia.Should().Contain("InsertDrawingGalleryWorkflow.Register(");
        wpf.Should().NotContain("registry.Register(\"freew.shape-rectangle\"");
        wpf.Should().NotContain("registry.Register(\"freew.textbox-sidebar\"");
        avalonia.Should().NotContain("r.Register(\"freew.shape\"");
    }
}
