using System.IO;
using System.Buffers.Binary;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FluentAssertions;
using FreeX.App.Presentation.Interactions;
using FreeX.ParityCompare.Core;

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
    public void LinuxParityCaptureHarness_UsesBoundedForegroundContainerContract()
    {
        var source = ReadLinuxParityCaptureHarnessSource();

        source.Should().Contain("--entrypoint /bin/bash");
        source.Should().Contain("$Image /work/container-run.sh");
        source.Should().Contain("timeout --signal=TERM --kill-after=5s");
        source.Should().Contain("capture_validated=true");
        source.Should().Contain("docker stop --time 5 $name");
        source.Should().Contain("docker rm -f $name");
        source.Should().NotContain("$Image bash /work/container-run.sh");
    }

    [Fact]
    public void LinuxParityCaptureHarness_RejectsUnsafeSurfaceIdsBeforeBashExpansion()
    {
        var source = ReadLinuxParityCaptureHarnessSource();

        source.Should().Contain("$SurfaceId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$'");
        source.Should().Contain("$validatedSurfaceId = $SurfaceId");
        source.Should().Contain("$targetPngFileName = \"$validatedSurfaceId.png\"");
        source.Should().Contain("$runScript = $runScript.Replace('__SURFACE__', $validatedSurfaceId)");
        source.Should().Contain("$runScript = $runScript.Replace('__TARGET_PNG__', $targetPngFileName)");
        source.Should().Contain("rm -f /work/manifest.json /work/__TARGET_PNG__ /work/run-result.txt");
        source.Should().NotContain("rm -f /work/manifest.json /work/dialog.GoalSeekStatus.png");
    }

    [Fact]
    public void LinuxParityCaptureHarness_RequiresExactValidationResultMarkers()
    {
        var source = ReadLinuxParityCaptureHarnessSource();

        source.Should().Contain("$resultLines -contains \"app_exit=0\"");
        source.Should().Contain("$resultLines -contains \"capture_validated=true\"");
        source.Should().Contain("Capture container did not report app_exit=0 and capture_validated=true.");
        source.Should().Contain("if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf))");
    }

    [Fact]
    public async Task TargetedGoalSeekStatusCapture_WritesNonBlank380x190Png()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-goalseek-status-capture-" + Guid.NewGuid().ToString("N"));

        try
        {
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    window.Measure(new global::Avalonia.Size(1120, 720));
                    window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));
                    window.UpdateLayout();

                    var results = await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        targetSurfaceId: "dialog.GoalSeekStatus");

                    results.Should().ContainSingle(result => result.Id == "dialog.GoalSeekStatus");
                    var result = results.Single();
                    result.Captured.Should().BeTrue(result.Note);

                    var pngPath = Path.Combine(outputDirectory, result.PngFileName);
                    AssertCapturedPng(outputDirectory, result);
                    ReadPngDimensions(pngPath).Should().Be((380, 190));
                    new FileInfo(pngPath).Length.Should().BeGreaterThan(1000,
                        "the targeted dialog PNG must contain rendered content, not only a minimal blank frame");
                }
                finally
                {
                    if (window.IsVisible)
                        window.Close();
                }
            }, CancellationToken.None);
        }
        finally
        {
            TryDeleteDirectory(outputDirectory);
        }
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

        routes.Should().NotContain(route => route.IsMissing,
            "the portable host now implements every authoritative dialog surface");
    }

    [Fact]
    public void InteractionDialogRoutes_KeepExplicitStableMappingsForDifferentlyNamedPortableSurfaces()
    {
        var routes = MainWindow.ParityInteractionDialogRoutes.ToDictionary(route => route.CatalogId);
        var expectedMappings = new (string CatalogId, string SurfaceId, string ProductionSurface)[]
        {
            ("dialog.ActivateSheetDialog", "dialog.ActivateSheet", "ShowSwitchWindowsDialogAsync"),
            ("dialog.ChartAreaLegendDialog", "dialog.FormatChartArea", "ShowFormatChartAreaDialog"),
            ("dialog.CommentListWindow", "dialog.CommentList", "ShowCommentsListAsync"),
            ("dialog.ConfirmPasswordDialog", "dialog.ProtectSheet", "ShowProtectSheetDialogAsync (integrated confirmation field)"),
            ("dialog.HeaderFooterDialog", "dialog.HeaderFooterDialog", "ShowHeaderFooterDialogAsync (dedicated editor)"),
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
    public async Task DialogInteractionRoute_StableFailureCohortPassesSharedContract()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-dialog-contract-cohort-" + Guid.NewGuid().ToString("N"));

        try
        {
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    var selectedIds = new HashSet<string>(StringComparer.Ordinal)
                    {
                        "dialog.ActivateSheetDialog",
                        "dialog.AdvancedFilterDialog",
                        "dialog.AllowEditRangeDialog",
                        "dialog.AutoFilterDialog",
                        "dialog.ChangeChartTypeDialog",
                        "dialog.ChartAreaLegendDialog",
                        "dialog.ChartAxisFormatDialog",
                        "dialog.ChartBarFormatDialog",
                        "dialog.ChartBubbleFormatDialog",
                        "dialog.ChartDataLabelsDialog",
                        "dialog.ChartErrorBarsDialog",
                        "dialog.ChartPieFormatDialog",
                        "dialog.ChartSeriesFormatDialog",
                        "dialog.ChartStockFormatDialog",
                        "dialog.ChartTitlesDialog",
                        "dialog.ChartTrendlineOptionsDialog",
                        "dialog.CommentListWindow",
                        "dialog.DataValidationDialog",
                        "dialog.ErrorCheckingDialog",
                        "dialog.FindReplaceDialog",
                        "dialog.FormatCellsDialog",
                        "dialog.FormatPictureDialog",
                        "dialog.HeaderFooterDialog",
                        "dialog.LegalNoticesDialog",
                        "dialog.ManageConditionalFormatsDialog",
                        "dialog.MoveChartDialog",
                        "dialog.MovePivotTableDialog",
                        "dialog.ObjectSizeDialog",
                        "dialog.PageSetupDialog",
                        "dialog.PictureCropDialog",
                        "dialog.PivotCalculatedFieldDialog",
                        "dialog.PivotCalculatedItemDialog",
                        "dialog.PivotChartOptionsDialog",
                        "dialog.PivotChartTypeDialog",
                        "dialog.PivotFieldFilterDialog",
                        "dialog.PivotFieldGroupingDialog",
                        "dialog.PivotLabelFilterDialog",
                        "dialog.PivotSortOptionsDialog",
                        "dialog.PivotStyleGalleryDialog",
                        "dialog.PivotTableDataSourceDialog",
                        "dialog.PivotTableDialog",
                        "dialog.PivotTableNameDialog",
                        "dialog.PivotTableOptionsDialog",
                        "dialog.PivotValueFieldSettingsDialog",
                        "dialog.PivotValueFilterDialog",
                        "dialog.RecommendedPivotTablesDialog",
                        "dialog.RotationDialog",
                        "dialog.ScenarioManagerDialog",
                        "dialog.SelectionPaneDialog",
                        "dialog.ShapeEffectsDialog",
                        "dialog.ShapeGradientDialog",
                        "dialog.SpellCheckDialog",
                        "dialog.SymbolPickerDialog",
                        "dialog.TextToColumnsDialog",
                        "dialog.WatchWindowDialog",
                        "dialog.WorkbookThemeDialog",
                        "dialog.OpenWorkbookNativeDialog",
                        "dialog.SaveAsWorkbookNativeDialog",
                        "dialog.ProtectWorkbookDialog",
                    };

                    await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        interactionOnly: true,
                        interactionDialogCatalogIds: selectedIds);

                    var contracts = window.BuildDialogInteractionContractResults(selectedIds);
                    contracts.Should().HaveCount(selectedIds.Count);
                    contracts.Should().OnlyContain(result => result.Status == "passed",
                        string.Join(Environment.NewLine, contracts.Select(result =>
                            $"{result.Id}: {result.Evidence}")));
                }
                finally
                {
                    if (window.IsVisible)
                        window.Close();
                }
            }, CancellationToken.None);
        }
        finally
        {
            TryDeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public async Task HeaderFooterInteractionRoute_OpensTheDedicatedProductionEditor()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-header-footer-interaction-" + Guid.NewGuid().ToString("N"));

        try
        {
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    var selectedIds = new HashSet<string>(StringComparer.Ordinal)
                    {
                        "dialog.HeaderFooterDialog",
                    };

                    var surfaces = await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        targetSurfaceId: "dialog.HeaderFooterDialog",
                        interactionOnly: true,
                        interactionDialogCatalogIds: selectedIds);

                    surfaces.Should().ContainSingle(surface =>
                        surface.Id == "dialog.HeaderFooterDialog" && surface.Captured);
                    window.DialogInteractionContracts["dialog.HeaderFooterDialog"].InitialFocus
                        .Should().Be("passed:TextBox#HeaderFooterHeaderCenterBox");
                    window.BuildDialogInteractionContractResults(selectedIds)
                        .Should().ContainSingle(result =>
                            result.Id == "dialog.HeaderFooterDialog" && result.Status == "passed");
                }
                finally
                {
                    if (window.IsVisible)
                        window.Close();
                }
            }, CancellationToken.None);
        }
        finally
        {
            TryDeleteDirectory(outputDirectory);
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
    public async Task CaptureParitySurfaces_RejectsManagedNameBoxDropdownAsAuthoritativeEvidence()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-parity-capture-namebox-dropdown-" + Guid.NewGuid().ToString("N"));

        try
        {
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    window.Measure(new global::Avalonia.Size(1120, 720));
                    window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));
                    window.UpdateLayout();

                    var results = await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        targetSurfaceId: "popup.nameBoxDropdown");

                    results.Should().ContainSingle();
                    var popup = results.Single();
                    popup.Id.Should().Be("popup.nameBoxDropdown");
                    popup.Kind.Should().Be(ParitySurfaceKind.Overlay);
                    popup.Captured.Should().BeFalse();
                    popup.Width.Should().BeNull();
                    popup.Height.Should().BeNull();
                    popup.EvidenceProvenance.Should().Be("managed-popup-diagnostic");
                    popup.Note.Should().Contain("live native X11 popup crop");
                    File.Exists(Path.Combine(outputDirectory, popup.PngFileName)).Should().BeFalse(
                        "managed/offscreen popup diagnostics must never emit authoritative parity PNGs");
                }
                finally
                {
                    if (window.IsVisible)
                        window.Close();
                }
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
    public async Task CaptureParitySurfaces_CapturesPageSetupTabsWithoutRunningInteractionContract()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-parity-capture-page-setup-" + Guid.NewGuid().ToString("N"));

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
                    targetSurfaceId: "dialog.PageSetup");

                results.Should().HaveCount(5);
                results.Should().OnlyContain(
                    result => result.Captured,
                    string.Join(Environment.NewLine, results.Select(result => $"{result.Id}: {result.Note}")));
                results.Select(result => result.Id).Should().Equal(
                    "dialog.PageSetup",
                    "dialog.PageSetup.Page",
                    "dialog.PageSetup.Margins",
                    "dialog.PageSetup.HeaderFooter",
                    "dialog.PageSetup.Sheet");
                foreach (var result in results)
                    AssertCapturedPng(outputDirectory, result);
                window.DialogInteractionContracts.Should().NotContainKey("dialog.PageSetup",
                    "visual capture must not run the separate keyboard interaction contract");

                window.Close();
            }, CancellationToken.None);
        }
        finally
        {
            TryDeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public async Task CaptureParitySurfaces_CapturesFormatCellsAlignmentTabWithoutRunningInteractionContract()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-parity-capture-format-cells-alignment-" + Guid.NewGuid().ToString("N"));

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
                    targetSurfaceId: "dialog.FormatCells.Alignment");

                results.Should().HaveCount(7);
                results.Should().OnlyContain(
                    result => result.Captured,
                    string.Join(Environment.NewLine, results.Select(result => $"{result.Id}: {result.Note}")));
                results.Select(result => result.Id).Should().Equal(
                    "dialog.FormatCells",
                    "dialog.FormatCells.Number",
                    "dialog.FormatCells.Alignment",
                    "dialog.FormatCells.Font",
                    "dialog.FormatCells.Border",
                    "dialog.FormatCells.Fill",
                    "dialog.FormatCells.Protection");
                results.Should().Contain(result => result.Id == "dialog.FormatCells.Alignment");
                foreach (var result in results)
                    AssertCapturedPng(outputDirectory, result);
                window.DialogInteractionContracts.Should().NotContainKey("dialog.FormatCells",
                    "visual capture must not run the separate keyboard interaction contract");

                window.Close();
            }, CancellationToken.None);
        }
        finally
        {
            TryDeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public async Task CaptureParitySurfaces_CapturesGoToSpecialAtFixedSizeWithoutClipping()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-parity-capture-go-to-special-" + Guid.NewGuid().ToString("N"));

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
                    targetSurfaceId: "dialog.GoToSpecial");

                results.Should().ContainSingle();
                var dialog = results.Single();
                dialog.Id.Should().Be("dialog.GoToSpecial");
                dialog.Captured.Should().BeTrue(dialog.Note);
                AssertCapturedPng(outputDirectory, dialog);

                var pngPath = Path.Combine(outputDirectory, dialog.PngFileName);
                new FileInfo(pngPath).Length.Should().BeGreaterThan(2_048,
                    "the Go To Special capture should contain the rendered controls");
                ReadPngDimensions(pngPath).Should().Be((430, 438),
                    "the fixed-size dialog client area should be captured without edge clipping");

                var image = PngCodec.DecodeFile(pngPath);
                FindExactColorBounds(image, red: 213, green: 223, blue: 229)
                    .Should().Be((13, 43, 400, 274),
                        "the Go To Special and value-type group borders should retain the WPF logical bounds");
                CountExactColorOnRow(image, 43, red: 213, green: 223, blue: 229)
                    .Should().BeGreaterThan(250,
                        "the top group border should span the full WPF-aligned content width");
                CountExactColorOnRow(image, 274, red: 213, green: 223, blue: 229)
                    .Should().BeGreaterThan(300,
                        "the bottom value-type border should remain at the WPF-aligned action-row separation");
                FindAccentRows(image, minimumY: 350, maximumY: 400, minimumPixels: 20)
                    .Should().Equal([369, 388],
                        "the default action button border should align with the WPF capture rows");
                var checkboxAnchors = FindDarkRunStartsOnRow(image, 248, minimumLength: 12);
                checkboxAnchors.Should().Equal([27, 113, 173, 255]);

                window.Close();
            }, CancellationToken.None);
        }
        finally
        {
            TryDeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public async Task CaptureParitySurfaces_CapturesSubtotalAtCanonicalFixedSize()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-parity-capture-subtotal-" + Guid.NewGuid().ToString("N"));

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
                    targetSurfaceId: "dialog.Subtotal");

                results.Should().ContainSingle();
                var dialog = results.Single();
                dialog.Id.Should().Be("dialog.Subtotal");
                dialog.Captured.Should().BeTrue(dialog.Note);
                AssertCapturedPng(outputDirectory, dialog);
                ReadPngDimensions(Path.Combine(outputDirectory, dialog.PngFileName))
                    .Should().Be((380, 390));

                window.Close();
            }, CancellationToken.None);
        }
        finally
        {
            TryDeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public async Task CaptureParitySurfaces_CapturesChartStyleCatalogDialog()
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
                results[0].Captured.Should().BeTrue(results[0].Note);
                AssertCapturedPng(outputDirectory, results[0]);

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

    private static (int Width, int Height) ReadPngDimensions(string path)
    {
        var header = File.ReadAllBytes(path).AsSpan(0, 24);
        return (
            BinaryPrimitives.ReadInt32BigEndian(header[16..20]),
            BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
    }

    private static (int MinX, int MinY, int MaxX, int MaxY) FindExactColorBounds(
        PixelImage image,
        byte red,
        byte green,
        byte blue)
    {
        var minX = image.Width;
        var minY = image.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var offset = (y * image.Width + x) * 4;
                if (image.Pixels[offset] != blue
                    || image.Pixels[offset + 1] != green
                    || image.Pixels[offset + 2] != red
                    || image.Pixels[offset + 3] != 255)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return (minX, minY, maxX, maxY);
    }

    private static int CountExactColorOnRow(
        PixelImage image,
        int y,
        byte red,
        byte green,
        byte blue)
    {
        var count = 0;
        for (var x = 0; x < image.Width; x++)
        {
            var offset = (y * image.Width + x) * 4;
            if (image.Pixels[offset] == blue
                && image.Pixels[offset + 1] == green
                && image.Pixels[offset + 2] == red
                && image.Pixels[offset + 3] == 255)
            {
                count++;
            }
        }

        return count;
    }

    private static IReadOnlyList<int> FindAccentRows(
        PixelImage image,
        int minimumY,
        int maximumY,
        int minimumPixels)
    {
        var rows = new List<int>();
        for (var y = minimumY; y <= maximumY; y++)
        {
            var count = 0;
            for (var x = 0; x < image.Width; x++)
            {
                var offset = (y * image.Width + x) * 4;
                var red = image.Pixels[offset + 2];
                var green = image.Pixels[offset + 1];
                var blue = image.Pixels[offset];
                if (red < 180 && green is > 90 and < 210 && blue > 180)
                    count++;
            }

            if (count >= minimumPixels)
                rows.Add(y);
        }

        return rows;
    }

    private static IReadOnlyList<int> FindDarkRunStartsOnRow(
        PixelImage image,
        int y,
        int minimumLength)
    {
        var starts = new List<int>();
        var runStart = -1;

        for (var x = 0; x <= image.Width; x++)
        {
            var isDark = false;
            if (x < image.Width)
            {
                var offset = (y * image.Width + x) * 4;
                isDark = image.Pixels[offset + 3] == 255
                    && image.Pixels[offset] + image.Pixels[offset + 1] + image.Pixels[offset + 2] < 660;
            }

            if (isDark && runStart < 0)
            {
                runStart = x;
            }
            else if (!isDark && runStart >= 0)
            {
                if (x - runStart >= minimumLength)
                    starts.Add(runStart);
                runStart = -1;
            }
        }

        return starts;
    }

    private static string ReadLinuxParityCaptureHarnessSource() =>
        TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot("tools", "Run-LinuxParityCapture.ps1");

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
