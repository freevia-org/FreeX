using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Rendering;

public sealed class CellMaterializationDedupSourceGuardTests
{
    [Fact]
    public void SharedPlannersRemainPlatformFree()
    {
        var textPlanner = ReadSource("src", "FreeX.App.Presentation", "Rendering", "CellTextMaterializationPlanner.cs");
        var fillPlanner = ReadSource("src", "FreeX.App.Presentation", "Rendering", "CellFillMaterializationPlanner.cs");
        var planners = textPlanner + Environment.NewLine + fillPlanner;

        planners.Should().NotContain("System.Windows");
        planners.Should().NotContain("Avalonia.");
        planners.Should().NotContain("FormattedText");
        planners.Should().NotContain("TextLayout");
        planners.Should().NotContain("LinearGradientBrush");
        planners.Should().NotContain("RadialGradientBrush");
    }

    [Fact]
    public void NativeAdaptersDelegateTextAndGradientPolicy()
    {
        var wpfStyles = ReadSource("src", "FreeX.App.UI", "GridView.Rendering.CellStyles.cs");
        var wpfGrid = ReadSource("src", "FreeX.App.UI", "GridView.Rendering.cs");
        var avaloniaGradient = ReadSource("src", "FreeX.App.Avalonia", "CellGradientBrush.cs");
        var avaloniaScript = ReadSource("src", "FreeX.App.Avalonia", "CellSuperSubScript.cs");
        var avaloniaGrid = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.cs");

        wpfGrid.Should().Contain("CellTextMaterializationProfile.Wpf");
        wpfGrid.Should().Contain("CellFillMaterializationProfile.Wpf");
        avaloniaGrid.Should().Contain("CellTextMaterializationProfile.Avalonia");
        avaloniaGrid.Should().Contain("CellFillMaterializationProfile.Avalonia");
        wpfStyles.Should().Contain("CellTextMaterializationPlanner.MaterializeRuns(");
        avaloniaGradient.Should().Contain("CellFillMaterializationPlanner.PlanGradient(");
        avaloniaScript.Should().Contain("CellTextMaterializationPlanner.Plan(");

        wpfStyles.Should().NotContain("Math.Cos(");
        wpfStyles.Should().NotContain("Math.Sin(");
        wpfStyles.Should().NotContain("OrderBy(s => s.Position)");
        avaloniaGradient.Should().NotContain("Math.Cos(");
        avaloniaGradient.Should().NotContain("Math.Sin(");
        avaloniaGradient.Should().NotContain("OrderBy(s => s.Position)");
        avaloniaScript.Should().NotContain("style?.Superscript == true");
        avaloniaScript.Should().NotContain("style?.Subscript == true");
    }

    [Fact]
    public void PatternAdaptersConsumeResolvedPortableFillPlans()
    {
        var wpf = ReadSource("src", "FreeX.App.UI", "GridView.Rendering.CellStyles.cs");
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "CellPatternFill.cs");

        wpf.Should().Contain("CellFillMaterializationPlan fillPlan");
        avalonia.Should().Contain("Build(CellFillMaterializationPlan fillPlan)");
        wpf.Should().NotContain("style.ResolveFillPatternColor(theme)");
        avalonia.Should().NotContain("style.ResolveFillPatternColor(theme)");
    }

    private static string ReadSource(params string[] parts) =>
        TestWorkspaceFileLocator.ReadAllText(parts);
}
