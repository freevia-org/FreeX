extern alias Harness;

using System.IO;
using Catalog = Harness::FreeW.DialogVisualHarness.FreeWDialogEvidenceCatalog;
using FixtureKind = Harness::FreeW.DialogVisualHarness.FreeWDialogFixtureKind;
using Host = Harness::FreeW.DialogVisualHarness.FreeWDialogHost;
using OpenAction = Harness::FreeW.DialogVisualHarness.FreeWDialogOpenAction;
using RouteCoverage = Harness::FreeW.DialogVisualHarness.FreeWDialogRouteCoverage;
using SurfaceKind = Harness::FreeW.DialogVisualHarness.FreeWDialogSurfaceKind;

namespace FreeW.DialogVisualHarness.Tests;

public sealed class FreeWDialogEvidenceCatalogContractTests
{
    [Fact]
    public void Catalog_has_unique_valid_routes_and_every_Wpf_route_is_paired()
    {
        Catalog.Validate().Should().BeEmpty();
        Catalog.Routes.Should().HaveCount(98);
        Catalog.Routes.Select(route => route.RouteId.ToUpperInvariant())
            .Should().OnlyHaveUniqueItems();
        Catalog.Routes.Where(route => route.Coverage == RouteCoverage.Paired)
            .Should().OnlyContain(route => route.Wpf != null && route.Avalonia != null);
        Catalog.Routes.Where(route => route.Coverage == RouteCoverage.AvaloniaExtension)
            .Should().OnlyContain(route => route.Wpf == null && route.Avalonia != null);
        Catalog.Routes.Where(route => route.Wpf != null)
            .Should().OnlyContain(route => route.Coverage == RouteCoverage.Paired && route.Avalonia != null);
    }

    [Fact]
    public void Catalog_owns_open_actions_fixtures_and_backstage_builders()
    {
        var font = Catalog.GetRequired("font");
        font.Wpf!.OpenAction.Should().Be(OpenAction.StaticPrompt);
        font.Wpf.EntryPointName.Should().Be("Prompt");
        font.Avalonia.OpenAction.Should().Be(OpenAction.ReflectedDialog);
        font.Fixture.Should().Be(FixtureKind.DefaultRunFormatting);

        var manual = Catalog.GetRequired("manual-hyphenation");
        manual.Wpf!.OpenAction.Should().Be(OpenAction.ManualHyphenation);
        manual.Avalonia.OpenAction.Should().Be(OpenAction.ManualHyphenation);
        manual.Fixture.Should().Be(FixtureKind.ManualHyphenationCandidate);

        var backstage = Catalog.Routes.Where(route => route.SurfaceKind == SurfaceKind.Backstage).ToArray();
        backstage.Should().HaveCount(10);
        backstage.Select(route => route.BackstageMethodName).Should().OnlyHaveUniqueItems();
        backstage.Should().OnlyContain(route => route.Wpf!.OpenAction == OpenAction.BackstagePane
            && route.Avalonia.OpenAction == OpenAction.BackstagePane);
    }

    [Fact]
    public void Page_number_format_is_a_real_shared_chrome_dialog_in_both_hosts()
    {
        var route = Catalog.GetRequired("page-number-format");
        route.Coverage.Should().Be(RouteCoverage.Paired);
        route.Wpf.Should().NotBeNull();
        route.Wpf!.DialogTypeName.Should().Be("PageNumberFormatDialog");
        route.Wpf.OpenAction.Should().Be(OpenAction.ReflectedDialog);
        route.Avalonia.DialogTypeName.Should().Be("PageNumberFormatDialog");

        var wpf = Read("freew", "FreeW.App.Host", "PageNumberFormatDialog.cs");
        var commandRegistry = Read("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var catalog = Read("freew", "tools", "FreeW.DialogVisualHarness", "FreeWDialogEvidenceCatalog.cs");

        wpf.Should().Contain(": Free.Shared.Ribbon.Wpf.DialogWindow");
        wpf.Should().Contain("DialogButtonRowFactory.Create(");
        wpf.Should().Contain("PageNumberFormatDialogPlanner.TryBuildResult(");
        commandRegistry.Should().Contain("PageNumberFormatDialog.Prompt(Window.GetWindow(editor), editor.Model.Page)");
        commandRegistry.Should().NotContain("private static class PageNumberFormatDialog");
        catalog.Should().Contain("Pair(\"page-number-format\", \"PageNumberFormatDialog\")");
        catalog.Should().NotContain("AvaloniaOnly(\"page-number-format\"");
    }

    [Fact]
    public void Capture_plan_preserves_evidence_names_manifest_metadata_and_route_sizing()
    {
        var wpf = Catalog.CreateCapturePlan(
            "wpf",
            "wpf.compare-documents.tab-more",
            "compare-documents",
            "relevant-tab",
            "More");
        wpf.FullPngPath.Should().Be("full/wpf/wpf.compare-documents.tab-more.png");
        wpf.TargetPngPath.Should().Be("crops/wpf/wpf.compare-documents.tab-more.png");
        wpf.ManifestFileName.Should().Be("wpf_dialog_capture_manifest.json");
        wpf.ManifestSchema.Should().Be("freew.dialog-capture-manifest.v1");
        wpf.ManifestSchemaVersion.Should().Be(1);
        wpf.TargetHeight.Should().Be(720);

        var avalonia = Catalog.CreateCapturePlan(
            "avalonia",
            "avalonia.multilevel-list.initial",
            "multilevel-list",
            "initial",
            null);
        avalonia.FullPngPath.Should().Be("full/avalonia/avalonia.multilevel-list.initial.png");
        avalonia.TargetPngPath.Should().Be("crops/avalonia/avalonia.multilevel-list.initial.png");
        avalonia.ManifestFileName.Should().Be("avalonia_dialog_capture_manifest.json");
        avalonia.ManifestSchemaVersion.Should().Be(2);
        avalonia.UseWpfAuthoritySize.Should().BeTrue();
        avalonia.ClientWidthAdjustment.Should().Be(1);

        Catalog.CreateCapturePlan(
                "avalonia",
                "avalonia.screen-clip-overlay.open",
                "screen-clip-overlay",
                "open",
                null)
            .HasNativeFrame.Should().BeFalse();
    }

    [Fact]
    public void Native_factories_and_capture_programs_defer_UI_free_policy_to_the_catalog()
    {
        var wpfFactory = Read("freew", "tools", "FreeW.DialogVisualHarness.Wpf", "WpfDialogRouteFactory.cs");
        var avaloniaFactory = Read("freew", "tools", "FreeW.DialogVisualHarness.Avalonia", "AvaloniaDialogRouteFactory.cs");
        var wpfProgram = Read("freew", "tools", "FreeW.DialogVisualHarness.Wpf", "Program.cs");
        var avaloniaProgram = Read("freew", "tools", "FreeW.DialogVisualHarness.Avalonia", "Program.cs");
        var inventoryProgram = Read("freew", "tools", "FreeW.DialogVisualHarness", "Program.cs");

        wpfFactory.Should().Contain("FreeWDialogEvidenceCatalog.TryGet(routeId");
        avaloniaFactory.Should().Contain("FreeWDialogEvidenceCatalog.TryGet(routeId");
        wpfFactory.Should().NotContain("DialogTypes = new Dictionary");
        avaloniaFactory.Should().NotContain("DialogTypes = new Dictionary");

        wpfProgram.Should().Contain("FreeWDialogEvidenceCatalog.CreateCapturePlan(");
        avaloniaProgram.Should().Contain("FreeWDialogEvidenceCatalog.CreateCapturePlan(");
        avaloniaProgram.Should().NotContain(
            "scenario.RouteId is \"accessibility-report\" or \"font\" or \"paragraph\"");
        wpfProgram.Should().NotContain("static int TargetHeight(Scenario scenario)");
        avaloniaProgram.Should().NotContain("static int TargetHeight(Scenario scenario)");

        inventoryProgram.Should().Contain("FreeWDialogEvidenceCatalog.CanonicalRoute(host, sourceRouteId)");
        inventoryProgram.Should().Contain("FreeWDialogEvidenceCatalog.ValidStates(routeId, surfaceKind, tabs)");
        inventoryProgram.Should().NotContain("static string CanonicalRoute(string host, string routeId)");
    }

    private static string Read(params string[] relativeParts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. relativeParts]));
    }
}
