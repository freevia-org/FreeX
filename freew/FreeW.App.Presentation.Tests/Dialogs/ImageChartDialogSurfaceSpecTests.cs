using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class ImageChartDialogSurfaceSpecTests
{
    [Fact]
    public void ImageSurfacesPreserveSharedTitlesLabelsAndSupportingText()
    {
        ImageCropDialogPlanner.Surface.Title.Should().Be("Crop Picture");
        ImageCropDialogPlanner.Surface.Fields.Select(field => field.Label)
            .Should().Equal("Left (%):", "Right (%):", "Top (%):", "Bottom (%):");

        ImageBorderDialogPlanner.Surface.Should().Match<DialogSurfaceSpec<ImageBorderDialogField>>(surface =>
            surface.Title == "Picture Border"
            && surface.SupportingText == "Color: 6-digit RGB hex, e.g. 000000 for black. Leave blank to remove the border.");
        ImageBorderDialogPlanner.Surface.Fields.Select(field => field.Label)
            .Should().Equal("Color (hex, empty = no border):", "Width (pt):", "Style:");

        ImageSizeDialogPlanner.Surface.Title.Should().Be(ImageSizeDialogPlanner.DefaultTitle);
        ImageSizeDialogPlanner.Surface.Fields.Select(field => field.Label)
            .Should().Equal("Width (pt):", "Height (pt):", "Lock aspect ratio");

        ImagePositionDialogPlanner.Surface.Title.Should().Be(ImagePositionDialogPlanner.DefaultTitle);
        ImagePositionDialogPlanner.Surface.Fields.Select(field => field.Label)
            .Should().Equal("Horizontal offset (pt):", "Relative to:", "Vertical offset (pt):", "Relative to:");
    }

    [Fact]
    public void ImageAdjustSurfacesPreserveExistingDetailedAndCompactWording()
    {
        ImageAdjustDialogPlanner.DetailedSurface.Title.Should().Be("Picture Corrections and Color");
        ImageAdjustDialogPlanner.DetailedSurface.Fields.Select(field => field.Label)
            .Should().Equal(
                "Brightness (-100 to +100):",
                "Contrast (-100 to +100):",
                "Saturation (0\u2013400, 100=normal):",
                "Transparency (0\u2013100):");

        ImageAdjustDialogPlanner.CompactSurface.Title.Should().Be("Picture Corrections");
        ImageAdjustDialogPlanner.CompactSurface.Fields.Select(field => field.Label)
            .Should().Equal(
                "Brightness (-100 to 100):",
                "Contrast (-100 to 100):",
                "Saturation (0 to 400):",
                "Transparency (0 to 100):");
    }

    [Fact]
    public void ChartSurfacesPreserveTitlesLabelsAndInsertMetadata()
    {
        ChartTitleDialogPlanner.Surface.Title.Should().Be("Chart Title");
        ChartTitleDialogPlanner.Surface.Field(ChartTitleDialogField.Title).Label.Should().Be("Title:");

        ChartAxisTitlesDialogPlanner.Surface.Title.Should().Be("Axis Titles");
        ChartAxisTitlesDialogPlanner.Surface.Fields.Select(field => field.Label)
            .Should().Equal("Category axis:", "Value axis:");

        ChartSizeDialogPlanner.Surface.Title.Should().Be("Chart Size");
        ChartSizeDialogPlanner.Surface.Fields.Select(field => field.Label)
            .Should().Equal("Width (pt):", "Height (pt):");

        InsertChartDialogPlanner.Surface.Title.Should().Be("Insert Chart");
        InsertChartDialogPlanner.Surface.Fields.Select(field => field.Label)
            .Should().Equal(
                "Chart type:",
                "Title (optional):",
                "Chart data  (first column = category labels, remaining columns = series values):");
        InsertChartDialogPlanner.CategoryColumnHeader.Should().Be("Category");
    }

    [Fact]
    public void ChartDialogInitialFocusIsOwnedBySharedPresentationPolicy()
    {
        ChartTitleDialogPlanner.InitialFocusField.Should().Be(ChartTitleDialogField.Title);
        ChartAxisTitlesDialogPlanner.InitialFocusField.Should().Be(ChartAxisTitlesDialogField.Category);
        ChartSizeDialogPlanner.InitialFocusField.Should().Be(ChartSizeDialogField.Width);

        ChartTitleDialogPlanner.Surface.Field(ChartTitleDialogPlanner.InitialFocusField).AutomationId
            .Should().Be("ChartTitleTextBox");
        ChartAxisTitlesDialogPlanner.Surface.Field(ChartAxisTitlesDialogPlanner.InitialFocusField).AutomationId
            .Should().Be("ChartCategoryAxisTitleTextBox");
        ChartSizeDialogPlanner.Surface.Field(ChartSizeDialogPlanner.InitialFocusField).AutomationId
            .Should().Be("ChartSizeWidthTextBox");
    }

    [Fact]
    public void BothChartRenderersProjectTheSharedAutomationAndFocusContracts()
    {
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "MediaDialogParity.cs");
        var wpf = new[]
        {
            ReadSource("freew", "FreeW.App.Host", "ChartTitleDialog.cs"),
            ReadSource("freew", "FreeW.App.Host", "ChartAxisTitlesDialog.cs"),
            ReadSource("freew", "FreeW.App.Host", "ChartSizeDialog.cs"),
        };

        avalonia.Should().Contain("ChartTitleDialogPlanner.InitialFocusField");
        avalonia.Should().Contain("ChartAxisTitlesDialogPlanner.InitialFocusField");
        avalonia.Should().Contain("ChartSizeDialogPlanner.InitialFocusField");
        avalonia.Should().Contain("ImageChartDialogSurfaceSemantics.Apply(this, surface);");
        avalonia.Should().Contain("ImageChartDialogSurfaceSemantics.ApplyValidation(_status, surface);");
        avalonia.Should().Contain("ResolveFocusTarget(");
        wpf.Should().OnlyContain(source => source.Contains("ResolveFocusTarget(", StringComparison.Ordinal));
        wpf[0].Should().Contain("ChartTitleDialogPlanner.InitialFocusField");
        wpf[1].Should().Contain("ChartAxisTitlesDialogPlanner.InitialFocusField");
        wpf[2].Should().Contain("ChartSizeDialogPlanner.InitialFocusField");
    }

    [Fact]
    public void SurfaceAutomationContractsAreCompleteAndStable()
    {
        AssertSurface(ImageCropDialogPlanner.Surface, "ImageCropDialog");
        AssertSurface(ImageBorderDialogPlanner.Surface, "ImageBorderDialog");
        AssertSurface(ImageSizeDialogPlanner.Surface, "ImageSizeDialog");
        AssertSurface(ImagePositionDialogPlanner.Surface, "ImagePositionDialog");
        AssertSurface(ImageAdjustDialogPlanner.DetailedSurface, "ImageAdjustDialog");
        AssertSurface(ImageAdjustDialogPlanner.CompactSurface, "ImageAdjustDialog");
        AssertSurface(ChartTitleDialogPlanner.Surface, "ChartTitleDialog");
        AssertSurface(ChartAxisTitlesDialogPlanner.Surface, "ChartAxisTitlesDialog");
        AssertSurface(ChartSizeDialogPlanner.Surface, "ChartSizeDialog");
        AssertSurface(InsertChartDialogPlanner.Surface, "InsertChartDialog");
    }

    private static void AssertSurface<TField>(DialogSurfaceSpec<TField> surface, string automationId)
        where TField : struct, Enum
    {
        surface.AutomationId.Should().Be(automationId);
        surface.AutomationName.Should().NotBeNullOrWhiteSpace();
        surface.Fields.Should().OnlyHaveUniqueItems(field => field.Field);
        surface.Fields.Should().OnlyHaveUniqueItems(field => field.AutomationId);
        surface.Fields.Should().OnlyContain(field =>
            !string.IsNullOrWhiteSpace(field.Label)
            && !string.IsNullOrWhiteSpace(field.AutomationName));
    }

    private static string ReadSource(params string[] segments)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(segments).ToArray()));
    }
}
