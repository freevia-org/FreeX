using FluentAssertions;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Guards the Zoom-dialog dedup: FreeX's and FreeW's Zoom dialogs must both reach their custom-percent
/// decision through <c>Free.Shared.AppServices.ZoomPercentPolicy</c>, and neither may re-grow a private
/// parse/clamp/validation-taxonomy copy.
/// </summary>
public sealed class ZoomPolicyDedupSourceTests
{
    private static string Read(params string[] segments) =>
        TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(segments);

    [Fact]
    public void SharedPolicy_OwnsTheCustomPercentRouteAndTheValidationTaxonomy()
    {
        var policy = Read("shared", "Free.Shared.AppServices", "ZoomPercentPolicy.cs");
        var taxonomy = Read("shared", "Free.Shared.AppServices", "ZoomPercentInput.cs");

        policy.Should().Contain("public bool TryResolveWholePercent(");
        policy.Should().Contain("ZoomPercentRangeMode rangeMode");
        taxonomy.Should().Contain("public enum ZoomPercentInputError");
        taxonomy.Should().Contain("public enum ZoomPercentRangeMode");

        foreach (var member in new[] { "Missing", "NotNumeric", "OutOfRange", "NotWholePercent" })
            taxonomy.Should().Contain(member);
    }

    [Fact]
    public void FreeXZoomDialogPlanner_RoutesCustomPercentThroughSharedPolicy()
    {
        var planner = Read("src", "FreeX.App.Services", "ZoomDialogPlanner.cs");
        var mapper = Read("src", "FreeX.App.Services", "ZoomLevelMapper.cs");

        planner.Should().Contain("ZoomLevelMapper.TryResolveWholeZoomPercent(");
        planner.Should().Contain("ZoomPercentInputError");
        mapper.Should().Contain("Policy.TryResolveWholePercent(text, ZoomPercentRangeMode.Reject");

        // No private parse/clamp copy: every numeric decision belongs to the shared policy.
        planner.Should().NotContain("double.TryParse");
        planner.Should().NotContain("int.TryParse");
        planner.Should().NotContain("NumberStyles");
        planner.Should().NotContain("Math.Clamp");
        planner.Should().NotContain("Math.Round");
        mapper.Should().NotContain("double.TryParse");
        mapper.Should().NotContain("NumberStyles");
    }

    [Fact]
    public void FreeWZoomDialogPlanner_RoutesCustomPercentThroughSharedPolicy()
    {
        var planner = Read("freew", "FreeW.App.Presentation", "Dialogs", "ZoomDialogPlanner.cs");
        var session = Read("freew", "FreeW.App.Presentation", "Dialogs", "ZoomDialogSession.cs");

        planner.Should().Contain("private static readonly ZoomPercentPolicy PercentPolicy = new(");
        planner.Should().Contain("PercentPolicy.TryResolveWholePercent(");
        planner.Should().Contain("ZoomPercentRangeMode.Clamp");
        planner.Should().Contain("PercentPolicy.FormatPercentLabel(percent)");
        planner.Should().Contain("ValidationMessageFor(ZoomPercentInputError? error)");
        session.Should().Contain("ZoomPercentInputError Error");

        planner.Should().NotContain("double.TryParse");
        planner.Should().NotContain("int.TryParse");
        planner.Should().NotContain("NumberStyles");
        planner.Should().NotContain("$\"{percent}%\"");
    }

    [Fact]
    public void ValidationErrorTaxonomy_IsDeclaredOnceAndNoLongerDuplicatedPerApp()
    {
        var freeWPlanner = Read("freew", "FreeW.App.Presentation", "Dialogs", "ZoomDialogPlanner.cs");

        // FreeW dropped its private one-value enum in favour of the shared taxonomy; FreeX keeps only a
        // *message projection* record (resource key + fallback text), not a second taxonomy.
        freeWPlanner.Should().NotContain("enum ZoomDialogValidationError");
        freeWPlanner.Should().NotContain("WholePercentRequired");

        var freeXPlanner = Read("src", "FreeX.App.Services", "ZoomDialogPlanner.cs");
        freeXPlanner.Should().Contain("record ZoomDialogValidationError(string ResourceKey, string FallbackText)");
    }

    [Fact]
    public void BothRendererFamilies_FormatPresetLabelsThroughTheirPlanner()
    {
        var wpf = Read("src", "FreeX.App.Host", "ZoomDialog.cs");
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");

        wpf.Should().Contain("ZoomDialogPlanner.FormatPresetLabel(preset)");
        avalonia.Should().Contain("ZoomDialogPlanner.FormatPresetLabel(zoom)");
        wpf.Should().NotContain("$\"{preset}%\"");
        avalonia.Should().NotContain("$\"{zoom}%\"");
    }
}
