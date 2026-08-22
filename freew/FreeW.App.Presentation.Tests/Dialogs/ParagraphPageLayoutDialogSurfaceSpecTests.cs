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
    public void ParagraphVisualMetricsOwnWpfAuthorityAndAvaloniaTemplateCompensation()
    {
        var metrics = ParagraphBreaksDialogPlanner.VisualMetrics;

        metrics.WindowWidth.Should().Be(380);
        metrics.NumericFieldMinWidth.Should().Be(120);
        metrics.ActionButtonWidth.Should().Be(72);
        metrics.WpfRootMargin.Should().Be(new ParagraphDialogThickness(12, 12, 12, 12));
        metrics.WpfTabContentMargin.Should().Be(new ParagraphDialogThickness(10, 10, 10, 10));
        metrics.FieldLabelMargin.Should().Be(new ParagraphDialogThickness(0, 4, 8, 4));
        metrics.FieldControlMargin.Should().Be(new ParagraphDialogThickness(0, 4, 0, 4));
        metrics.WpfContextualSpacingMargin.Should().Be(new ParagraphDialogThickness(0, 4, 0, 0));
        metrics.CheckBoxMargin.Should().Be(new ParagraphDialogThickness(0, 0, 0, 6));
        metrics.SectionHeadingMargin.Should().Be(new ParagraphDialogThickness(0, 0, 0, 8));
        metrics.SectionSeparatorMargin.Should().Be(new ParagraphDialogThickness(0, 4, 0, 8));
        metrics.WpfActionRowMargin.Should().Be(new ParagraphDialogThickness(0, 10, 0, 0));
        metrics.AvaloniaTabsMargin.Should().Be(new ParagraphDialogThickness(12, 12, 13, 0));
        metrics.AvaloniaIndentsTabContentMargin.Should().Be(new ParagraphDialogThickness(9, 12, 12, 10));
        metrics.AvaloniaContextualSpacingMargin.Should().Be(new ParagraphDialogThickness(3, 4, 0, 0));
        metrics.AvaloniaTabPaneMargin.Should().Be(new ParagraphDialogThickness(0, -1, 0, 0));
        metrics.AvaloniaValidationMargin.Should().Be(new ParagraphDialogThickness(12, 8, 11, 0));
        metrics.AvaloniaActionRowMargin.Should().Be(new ParagraphDialogThickness(12, 10, 11, 11));
        metrics.AvaloniaLabelColumnWidth.Should().Be(104);
        metrics.AvaloniaIndentsTabHeight.Should().Be(303);
        metrics.AvaloniaBreaksTabHeight.Should().Be(235);
        metrics.AvaloniaIndentsTabHeaderWidth.Should().Be(123);
        metrics.AvaloniaBreaksTabHeaderWidth.Should().Be(122);
    }

    [Theory]
    [InlineData("FreeW.App.Host", "ParagraphBreaksDialog.cs")]
    [InlineData("FreeW.App.Avalonia", "ParagraphDialog.cs")]
    public void ParagraphRenderersConsumeSharedVisualMetrics(string project, string fileName)
    {
        var source = ReadSource(project, fileName);

        source.Should().Contain("ParagraphBreaksDialogPlanner.VisualMetrics");
        source.Should().Contain("Layout.NumericFieldMinWidth");
        source.Should().Contain("Layout.FieldLabelMargin");
        source.Should().Contain("Layout.FieldControlMargin");
        source.Should().Contain("Layout.ActionButtonWidth");
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

        DropCapOptionsDialogPlanner.Surface.Fields.Select(field => field.Label).Should().Equal(
            "Position:",
            "None",
            "Dropped",
            "In Margin",
            "Font:",
            "Lines to drop (1-10):",
            "Distance from text (pt):");

        ManualHyphenationPlanner.HostSurface.Fields.Select(field => field.Label).Should().Equal(
            "Hyphenate at:", "_Yes", "_No", "Cancel");
        ManualHyphenationPlanner.AvaloniaSurface.Fields.Select(field => field.Label).Should().Equal(
            "Hyphenate at:", "Yes", "No", "Cancel");

        var focus = ManualHyphenationPlanner.FocusPlan;
        focus.InitialFocusTarget.Should().Be(ManualHyphenationDialogField.Choices);
        focus.ValidationFocusTarget.Should().Be(ManualHyphenationDialogField.Choices);
        focus.SelectAllOnFocus.Should().BeFalse();
        focus.ActionButtons.Select(action => action.Label).Should().Equal("Yes", "No", "Cancel");
        focus.ActionButtons.Select(action => action.IsDefault).Should().Equal(true, false, false);
        focus.ActionButtons.Select(action => action.IsCancel).Should().Equal(false, false, true);
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
        AssertSurface(DropCapOptionsDialogPlanner.Surface, DropCapOptionsDialogPlanner.AutomationId, requireValidation: false);
        AssertSurface(ManualHyphenationPlanner.HostSurface, ManualHyphenationPlanner.AutomationId, requireValidation: false);
        AssertSurface(ManualHyphenationPlanner.AvaloniaSurface, ManualHyphenationPlanner.AutomationId, requireValidation: false);
    }

    [Theory]
    [InlineData("FreeW.App.Host", "ParagraphBreaksDialog.cs", "ParagraphBreaksDialogPlanner.Surface")]
    [InlineData("FreeW.App.Avalonia", "ParagraphDialog.cs", "ParagraphBreaksDialogPlanner.Surface")]
    [InlineData("FreeW.App.Host", "ParagraphIndentDialog.cs", "ParagraphIndentDialogPlanner.CompactSurface")]
    [InlineData("FreeW.App.Host", "ColumnsDialog.cs", "ColumnsDialogPlanner.Surface")]
    [InlineData("FreeW.App.Host", "CustomParagraphSpacingDialog.cs", "CustomParagraphSpacingDialogPlanner.Surface")]
    [InlineData("FreeW.App.Host", "HyphenationOptionsDialog.cs", "HyphenationOptionsDialogPlanner.Surface")]
    [InlineData("FreeW.App.Host", "LineNumberOptionsDialog.cs", "LineNumberOptionsDialogPlanner.Surface")]
    [InlineData("FreeW.App.Host", "DropCapOptionsDialog.cs", "DropCapOptionsDialogPlanner.Surface")]
    [InlineData("FreeW.App.Host", "ManualHyphenationDialog.cs", "ManualHyphenationPlanner.HostSurface")]
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
        source.Should().Contain("DropCapOptionsDialogPlanner.Surface");
        source.Should().Contain("ManualHyphenationPlanner.AvaloniaSurface");
    }

    [Fact]
    public void ManualHyphenationRenderersConsumeSharedFocusAndActionPolicy()
    {
        var wpf = ReadSource("FreeW.App.Host", "ManualHyphenationDialog.cs");
        var avalonia = ReadSource("FreeW.App.Avalonia", "PageLayoutDialogs.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ManualHyphenationPlanner.FocusPlan.ActionButtons");
            source.Should().Contain("ManualHyphenationPlanner.FocusPlan.InitialFocusTarget");
            source.Should().Contain("ResolveFocusTarget(");
        }
    }

    private static void AssertSurface<TField>(
        DialogSurfaceSpec<TField> surface,
        string automationId,
        bool requireValidation = true)
        where TField : struct, Enum
    {
        surface.AutomationId.Should().Be(automationId);
        surface.AutomationName.Should().NotBeNullOrWhiteSpace();
        if (requireValidation)
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
