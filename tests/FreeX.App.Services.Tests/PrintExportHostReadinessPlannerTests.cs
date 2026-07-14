using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class PrintExportHostReadinessPlannerTests
{
    [Fact]
    public void Create_WindowsAndAvaloniaUseSameScopeAndPrintJobPlanningSurface()
    {
        var workbook = BuildPrintableWorkbook();
        var selectedRange = GridRange.Parse("A1:C3", workbook.GetSheetAt(0).Id);
        var request = new PrintJobRequest(
            WorkbookExportPrintScope.SelectedRange,
            Copies: 0,
            SelectedRange: selectedRange);

        var wpfPlan = PrintExportHostReadinessPlanner.Create(
            workbook,
            hasSelection: true,
            request,
            PrintExportHostCapabilities.WindowsWpf());
        var avaloniaPlan = PrintExportHostReadinessPlanner.Create(
            workbook,
            hasSelection: true,
            request,
            PrintExportHostCapabilities.AvaloniaPortable());

        wpfPlan.ScopePlan.Scopes.Should().Equal(
            avaloniaPlan.ScopePlan.Scopes,
            (left, right) =>
                left.Scope == right.Scope &&
                left.IsAvailable == right.IsAvailable &&
                left.IsDefault == right.IsDefault);
        wpfPlan.NativePrintPlan.Status.Should().Be(PrintExportNativePrintReadinessStatus.PrintJobNotReady);
        avaloniaPlan.NativePrintPlan.Status.Should().Be(PrintExportNativePrintReadinessStatus.PrintJobNotReady);
        wpfPlan.NativePrintPlan.JobPlan.ValidationStatus.Should().Be(PrintJobValidationStatus.InvalidCopyCount);
        avaloniaPlan.NativePrintPlan.JobPlan.ValidationStatus.Should().Be(PrintJobValidationStatus.InvalidCopyCount);
        wpfPlan.NativePrintPlan.StatusText.Should().Be(avaloniaPlan.NativePrintPlan.StatusText);
    }

    [Fact]
    public void Create_WindowsSurfacePlansXpsAndAvaloniaSurfaceRejectsXpsHonestly()
    {
        var workbook = BuildPrintableWorkbook();
        var request = new PrintJobRequest(WorkbookExportPrintScope.ActiveSheet);

        var wpfPlan = PrintExportHostReadinessPlanner.Create(
            workbook,
            hasSelection: false,
            request,
            PrintExportHostCapabilities.WindowsWpf());
        var avaloniaPlan = PrintExportHostReadinessPlanner.Create(
            workbook,
            hasSelection: false,
            request,
            PrintExportHostCapabilities.AvaloniaPortable());

        wpfPlan.XpsExportPlan.IsReady.Should().BeTrue();
        wpfPlan.XpsExportPlan.Surface.Should().Be(WorkbookExportPrintSurface.WindowsDesktop);
        wpfPlan.XpsExportPlan.SupportedOutputKinds.Should().Contain(WorkbookExportPrintOutputKind.Xps);

        avaloniaPlan.XpsExportPlan.IsReady.Should().BeFalse();
        avaloniaPlan.XpsExportPlan.ValidationStatus.Should().Be(WorkbookExportPrintValidationStatus.OutputKindUnavailable);
        avaloniaPlan.XpsExportPlan.SupportedOutputKinds.Should().Equal(WorkbookExportPrintOutputKind.Pdf);
        avaloniaPlan.XpsExportPlan.StatusText.Should().Be(
            "macOS supports PDF export print planning; XPS is not available on this platform.");
    }

    [Fact]
    public void Create_NativePrintReadinessSeparatesNativeDialogPlatformPrinterAndPdfFallback()
    {
        var workbook = BuildPrintableWorkbook();
        var request = new PrintJobRequest(WorkbookExportPrintScope.ActiveSheet, Copies: 2, Collate: false);

        var wpfPlan = PrintExportHostReadinessPlanner.Create(
            workbook,
            hasSelection: false,
            request,
            PrintExportHostCapabilities.WindowsWpf());
        var avaloniaPrinterPlan = PrintExportHostReadinessPlanner.Create(
            workbook,
            hasSelection: false,
            request,
            PrintExportHostCapabilities.AvaloniaPortable(
                canSubmitToPlatformPrinter: true,
                hasPrinterDestination: true));
        var avaloniaFallbackPlan = PrintExportHostReadinessPlanner.Create(
            workbook,
            hasSelection: false,
            request,
            PrintExportHostCapabilities.AvaloniaPortable());

        wpfPlan.NativePrintPlan.IsNativePrintReady.Should().BeTrue();
        wpfPlan.NativePrintPlan.RouteKind.Should().Be(PrintExportNativePrintRouteKind.NativePrintDialog);
        wpfPlan.NativePrintPlan.StatusText.Should().Contain("native print dialog");

        avaloniaPrinterPlan.NativePrintPlan.IsNativePrintReady.Should().BeTrue();
        avaloniaPrinterPlan.NativePrintPlan.RouteKind.Should().Be(PrintExportNativePrintRouteKind.PlatformPrinter);
        avaloniaPrinterPlan.NativePrintPlan.StatusText.Should().Contain("platform printer adapter");

        avaloniaFallbackPlan.NativePrintPlan.IsNativePrintReady.Should().BeFalse();
        avaloniaFallbackPlan.NativePrintPlan.CanProducePrintReadyPdfFallback.Should().BeTrue();
        avaloniaFallbackPlan.NativePrintPlan.RouteKind.Should().Be(PrintExportNativePrintRouteKind.PrintReadyPdfFallback);
        avaloniaFallbackPlan.NativePrintPlan.StatusText.Should().Contain("print-ready PDF fallback");

        wpfPlan.NativePrintPlan.JobPlan.StatusText.Should().Be(avaloniaFallbackPlan.NativePrintPlan.JobPlan.StatusText);
    }

    private static Workbook BuildPrintableWorkbook()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintArea = GridRange.Parse("A1:E6", sheet.Id);
        return workbook;
    }
}
