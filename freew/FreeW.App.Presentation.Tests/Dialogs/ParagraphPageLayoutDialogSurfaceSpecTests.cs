using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests.Dialogs;

public sealed class ParagraphPageLayoutDialogSurfaceSpecTests
{
    [Fact]
    public void ParagraphSurfacePreservesTabsSectionsFieldsAndValidationIdentity()
    {
        var surface = ParagraphBreaksDialogPlanner.Surface;

        surface.Title.Should().Be("Paragraph");
        surface.Field(ParagraphBreaksDialogField.IndentsAndSpacingTab).Label
            .Should().Be("Indents and Spacing");
        surface.Field(ParagraphBreaksDialogField.LineAndPageBreaksTab).Label
            .Should().Be("Line and Page Breaks");
        surface.Field(ParagraphBreaksDialogField.PaginationSection).Label
            .Should().Be("Pagination");
        surface.Field(ParagraphBreaksDialogField.FormattingExceptionsSection).Label
            .Should().Be("Formatting exceptions");
        surface.Fields.Select(field => field.Label).Should().ContainInOrder(
            "Left indent (pt):",
            "Right indent (pt):",
            "Special:",
            "By (pt):",
            "Space before (pt):",
            "Space after (pt):",
            "Line spacing (\u00d7):");
        surface.ValidationAutomationId.Should().Be("paragraph-validation-message");
    }

    [Fact]
    public void CompactParagraphSurfacePreservesIntentionalWording()
    {
        var surface = ParagraphIndentDialogPlanner.CompactSurface;

        surface.Title.Should().Be("Paragraph");
        surface.Fields.Select(field => field.Label)
            .Should().Equal("Left (pt):", "Right (pt):", "Special:", "By (pt):");
    }

    [Fact]
    public void AdjacentPageLayoutSurfacesPreserveExistingMetadata()
    {
        ColumnsDialogPlanner.Surface.Fields.Select(field => field.Label).Should().Equal(
            "Presets:", "Number of columns:", "Spacing (pt):", "Line between");

        CustomParagraphSpacingDialogPlanner.Surface.SupportingText.Should().Be(
            "All values in points (pt). Line spacing is a multiple (for example, 1.15 = 115%).");
        CustomParagraphSpacingDialogPlanner.Surface.Fields.Select(field => field.Label).Should().Equal(
            "Space before (pt):", "Space after (pt):", "Line spacing (x):");

        HyphenationOptionsDialogPlanner.Surface.Fields.Select(field => field.Label).Should().Equal(
            "Automatically hyphenate document",
            "Hyphenation zone (pt):",
            "Limit consecutive hyphens to (0 = no limit):",
            "Hyphenate words in CAPS");

        LineNumberOptionsDialogPlanner.Surface.Fields.Select(field => field.Label).Should().Equal(
            "Start at:", "Count by:", "Numbering:");
    }

    [Fact]
    public void SurfaceAutomationContractsAreCompleteAndStable()
    {
        AssertSurface(ParagraphBreaksDialogPlanner.Surface, "ParagraphDialog");
        AssertSurface(ParagraphIndentDialogPlanner.CompactSurface, "ParagraphIndentDialog");
        AssertSurface(ColumnsDialogPlanner.Surface, ColumnsDialogPlanner.AutomationId);
        AssertSurface(CustomParagraphSpacingDialogPlanner.Surface, CustomParagraphSpacingDialogPlanner.AutomationId);
        AssertSurface(HyphenationOptionsDialogPlanner.Surface, HyphenationOptionsDialogPlanner.AutomationId);
        AssertSurface(LineNumberOptionsDialogPlanner.Surface, LineNumberOptionsDialogPlanner.AutomationId);
    }

    [Theory]
    [InlineData("FreeW.App.Host", "ParagraphBreaksDialog.cs", "ParagraphBreaksDialogPlanner.Surface")]
    [InlineData("FreeW.App.Avalonia", "ParagraphDialog.cs", "ParagraphBreaksDialogPlanner.Surface")]
    [InlineData("FreeW.App.Host", "ParagraphIndentDialog.cs", "ParagraphIndentDialogPlanner.CompactSurface")]
    [InlineData("FreeW.App.Host", "ColumnsDialog.cs", "ColumnsDialogPlanner.Surface")]
    [InlineData("FreeW.App.Host", "CustomParagraphSpacingDialog.cs", "CustomParagraphSpacingDialogPlanner.Surface")]
    [InlineData("FreeW.App.Host", "HyphenationOptionsDialog.cs", "HyphenationOptionsDialogPlanner.Surface")]
    [InlineData("FreeW.App.Host", "LineNumberOptionsDialog.cs", "LineNumberOptionsDialogPlanner.Surface")]
    public void WpfRenderersConsumeSharedSurfaceSpecs(string project, string fileName, string surfaceReference)
    {
        ReadSource(project, fileName).Should().Contain(surfaceReference);
    }

    [Fact]
    public void AvaloniaPageLayoutRenderersConsumeAllAdjacentSurfaceSpecs()
    {
        var source = ReadSource("FreeW.App.Avalonia", "PageLayoutDialogs.cs");

        source.Should().Contain("ColumnsDialogPlanner.Surface");
        source.Should().Contain("CustomParagraphSpacingDialogPlanner.Surface");
        source.Should().Contain("HyphenationOptionsDialogPlanner.Surface");
        source.Should().Contain("LineNumberOptionsDialogPlanner.Surface");
    }

    private static void AssertSurface<TField>(DialogSurfaceSpec<TField> surface, string automationId)
        where TField : struct, Enum
    {
        surface.AutomationId.Should().Be(automationId);
        surface.AutomationName.Should().NotBeNullOrWhiteSpace();
        surface.ValidationAutomationId.Should().NotBeNullOrWhiteSpace();
        surface.Fields.Should().OnlyHaveUniqueItems(field => field.Field);
        surface.Fields.Should().OnlyHaveUniqueItems(field => field.AutomationId);
        surface.Fields.Should().OnlyContain(field =>
            !string.IsNullOrWhiteSpace(field.Label)
            && !string.IsNullOrWhiteSpace(field.AutomationName));
    }

    private static string ReadSource(string project, string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(root, "freew", project, fileName));
    }
}
