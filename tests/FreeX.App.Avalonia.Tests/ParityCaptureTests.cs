using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FluentAssertions;
using FreeX.App.Presentation.Interactions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Headless coverage for the <c>--parity-capture</c> surface capture. Runs the real <see cref="MainWindow"/>
/// under the headless drawing platform, drives <see cref="MainWindow.CaptureParitySurfacesAsync"/> into a temp
/// directory, and asserts the grid surface, at least one dialog surface, and PNG files are produced. Pixel
/// fidelity is the comparison runner's concern; this proves the capture path produces real files headlessly.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class ParityCaptureTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public void ParityCaptureOutputGuard_RejectsMissingEmptyAndNonPngOutputs()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-parity-output-guard-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(outputDirectory);

            var missing = ParityCaptureOutputGuard.ResultForPng(
                "dialog.Missing",
                ParitySurfaceKind.Dialog,
                outputDirectory,
                "missing.png");
            missing.Captured.Should().BeFalse();
            missing.Note.Should().Contain("not written");

            var emptyPath = Path.Combine(outputDirectory, "empty.png");
            File.WriteAllBytes(emptyPath, []);
            var empty = ParityCaptureOutputGuard.ResultForPng(
                "dialog.Empty",
                ParitySurfaceKind.Dialog,
                outputDirectory,
                "empty.png");
            empty.Captured.Should().BeFalse();
            empty.Note.Should().Contain("empty");

            var textPath = Path.Combine(outputDirectory, "not-png.png");
            File.WriteAllText(textPath, "not a png");
            var nonPng = ParityCaptureOutputGuard.ResultForPng(
                "dialog.NotPng",
                ParitySurfaceKind.Dialog,
                outputDirectory,
                "not-png.png");
            nonPng.Captured.Should().BeFalse();
            nonPng.Note.Should().Contain("PNG signature");
        }
        finally
        {
            TryDeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ParityCaptureOptions_TryParse_AcceptsSingleSurfaceFilter()
    {
        var args = new[] { "--parity-capture", @"C:\out", "--parity-capture-surface", "dialog.ScenarioManager", "book.xlsx" };

        var parsed = ParityCaptureOptions.TryParse(args, out var options, out var remaining, out var error);

        parsed.Should().BeTrue();
        error.Should().BeEmpty();
        options.Should().NotBeNull();
        options!.OutputDirectory.Should().Be(@"C:\out");
        options.SurfaceId.Should().Be("dialog.ScenarioManager");
        remaining.Should().Equal("book.xlsx");
    }

    [Fact]
    public void InteractionDialogRoutes_MapEveryAuthoritativeCatalogRow()
    {
        var catalog = InteractionSurfaceCatalog.Dialogs;
        var routes = MainWindow.ParityInteractionDialogRoutes;

        catalog.Should().HaveCount(120);
        routes.Should().HaveCount(120);
        routes.Select(route => route.CatalogId)
            .Should().Equal(catalog.Select(row => row.Id),
                "the parity route table should stay ordered with the authoritative interaction catalog");
        routes.Select(route => route.CatalogId).Should().OnlyHaveUniqueItems();

        routes.Should().OnlyContain(route =>
            route.IsMissing
                ? route.AvaloniaProductionSurface.Length == 0 && route.MissingReason.Length > 0
                : route.AvaloniaProductionSurface.Length > 0 && route.MissingReason.Length == 0,
            "every row must resolve to production UI or an explicit missing classification with a reason");

        routes.Where(route => route.IsMissing).Select(route => route.CatalogId)
            .Should().Equal(
                "dialog.ChartStyleDialog",
                "dialog.HeaderFooterPictureFormatDialog",
                "dialog.UnhideWindowDialog");
    }

    [Fact]
    public void InteractionDialogRoutes_KeepExplicitStableMappingsForDifferentlyNamedPortableSurfaces()
    {
        var routes = MainWindow.ParityInteractionDialogRoutes.ToDictionary(route => route.CatalogId);
        var expectedMappings = new (string CatalogId, string SurfaceId, string ProductionSurface)[]
        {
            ("dialog.ActivateSheetDialog", "dialog.ActivateSheet", "ShowSwitchWindowsDialogAsync"),
            ("dialog.ChartAreaLegendDialog", "dialog.FormatChartArea", "ShowFormatChartAreaDialog"),
            ("dialog.CommentListWindow", "dialog.CommentList", "ShowNotesListAsync"),
            ("dialog.ConfirmPasswordDialog", "dialog.ProtectSheet", "ShowProtectSheetDialogAsync (integrated confirmation field)"),
            ("dialog.HeaderFooterDialog", "dialog.PageSetup.HeaderFooter", "ShowPageSetupDialogAsync (Header/Footer tab)"),
            ("dialog.HyperlinkDialog", "dialog.InsertHyperlink", "ShowInsertHyperlinkInputDialogAsync"),
            ("dialog.NameDefinitionDialog", "dialog.NameDefinition", "ShowDefineNameDialogAsync"),
            ("dialog.NamedRangeDialog", "dialog.NamedRange", "ShowNameManagerDialogAsync"),
            ("dialog.OutlineGroupDialog", "dialog.OutlineGroup", "ShowOutlineSettingsDialogAsync"),
            ("dialog.PasswordProtectionDialog", "dialog.ProtectSheet", "ShowProtectSheetDialogAsync"),
            ("dialog.ScreenTipDialog", "dialog.ScreenTip", "ShowHyperlinkSubPromptAsync (ScreenTip)"),
            ("dialog.SheetNameDialog", "dialog.RenameSheet", "ShowRenameSheetDialogAsync"),
            ("dialog.WorkbookThemeDialog", "dialog.WorkbookTheme", "ShowThemesGalleryAsync"),
        };

        foreach (var expected in expectedMappings)
        {
            routes[expected.CatalogId].SurfaceId.Should().Be(expected.SurfaceId);
            routes[expected.CatalogId].AvaloniaProductionSurface.Should().Be(expected.ProductionSurface);
            routes[expected.CatalogId].IsMissing.Should().BeFalse();
        }
    }

    [Fact]
    public async Task CaptureParitySurfaces_ProducesGridAndDialogPngs()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-parity-capture-" + Guid.NewGuid().ToString("N"));

        try
        {
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                window.ParityLegacyDialogImageCount.Should().Be(93,
                    "the original 50 single-dialog and 43 default/tab captures must remain intact");
                window.Show();
                window.Measure(new global::Avalonia.Size(1120, 720));
                window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                var results = await window.CaptureParitySurfacesAsync(outputDirectory, maxDialogSurfaces: 8);

                // The grid surface always renders the live shell window — it must be captured.
                var grid = results.Single(r => r.Id == "grid.demo");
                grid.Captured.Should().BeTrue($"the demo grid should render headlessly (note: {grid.Note})");
                File.Exists(Path.Combine(outputDirectory, "grid.demo.png"))
                    .Should().BeTrue("grid.demo.png should be written");
                new FileInfo(Path.Combine(outputDirectory, "grid.demo.png")).Length
                    .Should().BeGreaterThan(0, "the PNG should not be empty");

                // Every ribbon tab surface should also capture (same window-render path as the grid).
                results.Where(r => r.Id.StartsWith("tab.", StringComparison.Ordinal))
                    .Should().OnlyContain(r => r.Captured, "ribbon tabs render the shell window");
                File.Exists(Path.Combine(outputDirectory, "tab.Home.png")).Should().BeTrue();

                var contextualSurfaces = results
                    .Where(r => r.Id.StartsWith("contextual.", StringComparison.Ordinal))
                    .ToList();
                contextualSurfaces.Should().OnlyContain(r => r.Captured, "contextual ribbon tabs should be selected and rendered headlessly");
                contextualSurfaces.Select(r => r.Id)
                    .Should().Contain(["contextual.PivotTableAnalyze", "contextual.PivotTableDesign"]);
                foreach (var contextual in contextualSurfaces)
                    File.Exists(Path.Combine(outputDirectory, contextual.PngFileName))
                        .Should().BeTrue($"{contextual.PngFileName} should be written for captured contextual tab {contextual.Id}");
                File.ReadAllBytes(Path.Combine(outputDirectory, "contextual.PivotTableAnalyze.png"))
                    .Should().NotEqual(File.ReadAllBytes(Path.Combine(outputDirectory, "tab.Home.png")),
                        "PivotTable Analyze must render its contextual tab, not the Home fallback");
                File.ReadAllBytes(Path.Combine(outputDirectory, "contextual.PivotTableDesign.png"))
                    .Should().NotEqual(File.ReadAllBytes(Path.Combine(outputDirectory, "tab.Home.png")),
                        "PivotTable Design must render its contextual tab, not the Home fallback");

                // At least one dialog surface should be captured to a PNG via the modal-capture path.
                var capturedDialogs = results
                    .Where(r => r.Id.StartsWith("dialog.", StringComparison.Ordinal) && r.Captured)
                    .ToList();
                capturedDialogs.Should().NotBeEmpty("at least one dialog should open and render headlessly");
                foreach (var dialog in capturedDialogs)
                    AssertCapturedPng(outputDirectory, dialog);

                var backstageSurfaces = results
                    .Where(r => r.Id.StartsWith("backstage.", StringComparison.Ordinal))
                    .ToList();
                backstageSurfaces.Should().OnlyContain(r => r.Captured, "File surfaces should render as full-window Backstage captures");
                foreach (var backstage in backstageSurfaces)
                    AssertCapturedPng(outputDirectory, backstage);

                window.Close();
            }, CancellationToken.None);
        }
        finally
        {
            TryDeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public async Task CaptureParitySurfaces_CapturesOnlyRequestedScenarioManagerDialog()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-parity-capture-scenario-manager-" + Guid.NewGuid().ToString("N"));

        try
        {
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                window.Show();
                window.Measure(new global::Avalonia.Size(1120, 720));
                window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                var results = await window.CaptureParitySurfacesAsync(
                    outputDirectory,
                    targetSurfaceId: "dialog.ScenarioManager");

                results.Should().ContainSingle();
                var scenarioManager = results.Single();
                scenarioManager.Id.Should().Be("dialog.ScenarioManager");
                scenarioManager.Captured.Should().BeTrue($"Scenario Manager should render headlessly (note: {scenarioManager.Note})");
                AssertCapturedPng(outputDirectory, scenarioManager);

                window.Close();
            }, CancellationToken.None);
        }
        finally
        {
            TryDeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public async Task CaptureParitySurfaces_ReturnsExplicitMissingResult_ForMissingCatalogDialog()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-parity-capture-missing-dialog-" + Guid.NewGuid().ToString("N"));

        try
        {
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                window.Show();

                var results = await window.CaptureParitySurfacesAsync(
                    outputDirectory,
                    targetSurfaceId: "dialog.ChartStyleDialog");

                results.Should().ContainSingle();
                results[0].Id.Should().Be("dialog.ChartStyle");
                results[0].Captured.Should().BeFalse();
                results[0].Note.Should().StartWith("Missing Avalonia production dialog:");
                results[0].Note.Should().Contain("no chart-style dialog");

                window.Close();
            }, CancellationToken.None);
        }
        finally
        {
            TryDeleteDirectory(outputDirectory);
        }
    }

    private static void AssertCapturedPng(string outputDirectory, ParitySurfaceResult result)
    {
        var pngPath = Path.Combine(outputDirectory, result.PngFileName);
        File.Exists(pngPath)
            .Should().BeTrue($"{result.PngFileName} should be written for captured surface {result.Id}");
        new FileInfo(pngPath).Length
            .Should().BeGreaterThan(0, $"{result.PngFileName} should not be empty for captured surface {result.Id}");
        ParityCaptureOutputGuard.ValidatePngOutput(pngPath)
            .Should().BeNull($"{result.PngFileName} should be valid PNG evidence for captured surface {result.Id}");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Temp cleanup is best-effort.
        }
    }
}
