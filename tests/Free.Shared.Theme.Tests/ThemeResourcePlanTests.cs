using System.IO;

namespace Free.Shared.Theme.Tests;

public sealed class ThemeResourcePlanTests
{
    [Fact]
    public void Plan_owns_the_complete_semantic_resource_inventory()
    {
        var plan = ThemeResourcePlan.Create(BrandThemes.FreeX, "FreeX");

        plan.Colors.Select(color => color.Role).Should().Equal(
            "Accent",
            "AccentDark",
            "AccentSoft",
            "AccentPressed",
            "TitleBar",
            "TitleBarHover",
            "TitleBarPressed",
            "TitleBarDisabled",
            "TitleBarButtonBorder",
            "RibbonButtonHover",
            "RibbonInlineDivider",
            "Text",
            "MutedText",
            "SubtleText",
            "RibbonSurface",
            "ChromeSurface",
            "SheetSurface",
            "StatusSurface",
            "Border",
            "BorderStrong",
            "Danger",
            "White");
        plan.Metrics.Select(metric => metric.Role).Should().Equal(
            "RibbonRowHeight",
            "ControlHeight",
            "IconSize",
            "CornerRadius",
            "StatusBarHeight",
            "TitleBarCaptionHeight");
        plan.Typography.Select(typography => typography.Role).Should().Equal(
            "Body",
            "Caption",
            "RibbonLabel",
            "Heading",
            "StatusBarText");
    }

    [Fact]
    public void Plan_owns_canonical_keys_values_and_shared_brush_aliases()
    {
        var plan = ThemeResourcePlan.Create(BrandThemes.FreeP, "FreeP");
        var accent = plan.Colors.Single(color => color.Role == "Accent");
        var statusHeight = plan.Metrics.Single(metric => metric.Role == "StatusBarHeight");
        var heading = plan.Typography.Single(typography => typography.Role == "Heading");

        accent.BrushKey.Should().Be("FreePAccentBrush");
        accent.ColorKey.Should().Be("FreePAccentColor");
        accent.Value.Should().Be(BrandThemes.FreeP.Colors.Accent);
        plan.SharedBrushes.Select(brush => brush.Key).Should().Equal(
            "ThemeNeutralTextBrush",
            "ThemeNeutralMutedTextBrush",
            "ThemeNeutralWhiteBrush",
            "ThemeNeutralDangerBrush",
            "ThemeNeutralSheetSurfaceBrush",
            "ThemeNeutralBorderBrush",
            "ThemeNeutralBorderStrongBrush",
            "ThemeAccentBrush",
            "ThemeAccentDarkBrush",
            "ThemeAccentSoftBrush",
            "ThemeAccentPressedBrush",
            "ThemeRibbonButtonHoverBrush");
        plan.SharedBrushes.Single(brush => brush.Key == "ThemeAccentBrush").Value
            .Should().Be(BrandThemes.FreeP.Colors.Accent);
        statusHeight.Key.Should().Be("FreePStatusBarHeight");
        statusHeight.Value.Should().Be(BrandThemes.FreeP.Metrics.StatusBarHeight);
        heading.FontFamilyKey.Should().Be("FreePHeadingFontFamily");
        heading.FontSizeKey.Should().Be("FreePHeadingFontSize");
        heading.FontWeightKey.Should().Be("FreePHeadingFontWeight");
        heading.Value.Should().Be(BrandThemes.FreeP.Typography.Heading);
    }

    [Fact]
    public void Native_appliers_consume_the_portable_plan_without_local_role_catalogs()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var wpfSource = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Theme.Wpf",
            "WpfThemeApplier.cs"));
        var avaloniaSource = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Theme.Avalonia",
            "AvaloniaThemeApplier.cs"));

        foreach (var source in new[] { wpfSource, avaloniaSource })
        {
            source.Should().Contain("ThemeResourcePlan.Create(theme, keyPrefix)");
            source.Should().Contain("foreach (var color in plan.Colors)");
            source.Should().Contain("foreach (var metric in plan.Metrics)");
            source.Should().Contain("foreach (var typography in plan.Typography)");
            source.Should().NotContain("AddBrush(\"Accent\"");
            source.Should().NotContain("AddTypo(\"Body\"");
        }

        wpfSource.Should().Contain("foreach (var sharedBrush in plan.SharedBrushes)");
    }
}
