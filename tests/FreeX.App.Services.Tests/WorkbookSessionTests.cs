using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionTests
{
    [Fact]
    public void Create_TemplateSourceClearsDirectSaveTarget()
    {
        var workbook = CreateWorkbook();
        var sourcePath = Path.Combine(Path.GetTempPath(), "Budget.xltx");
        var source = new StartupWorkbookLoadResult(
            workbook,
            "Budget.xltx",
            "Opened .xltx.",
            IsFallback: false,
            SourcePath: sourcePath,
            OpenedAsTemplate: true);

        var session = CreateSession(source);

        session.CurrentFilePath.Should().BeNull();
        session.CanSaveCurrentSource(out _).Should().BeFalse();
        session.DisplayName.Should().Be("Budget.xltx");
        session.StartupStatus.Should().Contain("Opened as template.");
    }

    [Fact]
    public void CanSaveCurrentSource_BlocksUnsupportedXlsxOverwrite()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), "Book.xlsx");
        var source = new StartupWorkbookLoadResult(
            CreateWorkbook(),
            "Book.xlsx",
            "Opened .xlsx.",
            IsFallback: false,
            SourcePath: sourcePath,
            FeatureReport: new XlsxFeatureReport(
            [
                new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Macros, "xl/vbaProject.bin")
            ]));

        var session = CreateSession(source);

        session.CanSaveCurrentSource(out _).Should().BeFalse();
        session.TryResolveSaveTarget(sourcePath, out _, out var message).Should().BeFalse();
        message.Should().Contain("FreeX Workbook");
        session.StartupStatus.Should().Contain("Unsupported XLSX features detected.");
    }

    [Fact]
    public void TryResolveOpenTarget_UsesOpenCapableFormats()
    {
        var adapter = new TestFileAdapter(formats: [
            new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: true, CanSave: false),
            new FileFormatDescriptor(".fxl", "FreeX Workbook", CanOpen: true, CanSave: true)
        ]);
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(CreateWorkbook(), "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320,
            adapters: [adapter]);

        var resolved = session.TryResolveOpenTarget("  Book.XLSM  ", out var target, out var message);

        resolved.Should().BeTrue();
        message.Should().BeEmpty();
        target.Should().NotBeNull();
        target!.Path.Should().Be("Book.XLSM");
        target.Adapter.Should().BeSameAs(adapter);
        target.Extension.Should().Be(".XLSM");
        target.Format.FormatName.Should().Be("XLSM Macro-Enabled Workbook");
        session.OpenFormats.Should().Contain(format => format.Extension == ".xlsm");
        session.SaveFormats.Should().NotContain(format => format.Extension == ".xlsm");
    }

    [Fact]
    public void TryResolveOpenTarget_RejectsUnsupportedAndMalformedPaths()
    {
        var session = CreateSession(new StartupWorkbookLoadResult(
            CreateWorkbook(),
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        session.TryResolveOpenTarget("Book.unsupported", out var unsupportedTarget, out var unsupportedMessage)
            .Should().BeFalse();
        unsupportedTarget.Should().BeNull();
        unsupportedMessage.Should().Contain(".unsupported");

        session.TryResolveOpenTarget("bad\0Book.xlsx", out var malformedTarget, out var malformedMessage)
            .Should().BeFalse();
        malformedTarget.Should().BeNull();
        malformedMessage.Should().Be("Unsupported file type.");
    }

    [Fact]
    public void CreateOpened_CreatesTemplateSessionWithOpenMetadata()
    {
        var path = Path.Combine(Path.GetTempPath(), "Budget.xltx");
        var format = new FileFormatDescriptor(
            ".xltx",
            "XLTX Template",
            CanOpen: true,
            CanSave: false,
            OpensAsTemplate: true);
        var adapter = new TestFileAdapter(formats: [format]);
        var workbook = CreateWorkbook("Template");
        var featureReport = new XlsxFeatureReport(
        [
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Charts, "xl/charts/chart1.xml")
        ]);
        var target = new WorkbookOpenTarget(path, adapter, ".xltx", format);
        var result = new WorkbookOpenResult(
            workbook,
            featureReport,
            "Budget",
            OpenedAsTemplate: true,
            LoadWarnings: ["Unsupported chart metadata retained."]);

        var session = new WorkbookSessionFactory().CreateOpened(
            target,
            result,
            viewportHeight: 240,
            viewportWidth: 320,
            adapters: [adapter]);

        session.CurrentFilePath.Should().BeNull();
        session.CurrentXlsxFeatureReport.Should().BeSameAs(featureReport);
        session.DisplayName.Should().Be("Budget.xltx");
        session.Workbook.Name.Should().Be("Budget.xltx");
        session.StartupStatus.Should().Contain("Opened as template.");
        session.StartupStatus.Should().Contain("Unsupported XLSX features detected.");
        session.StartupStatus.Should().Contain("1 load warning.");
    }

    [Fact]
    public void CommitCellText_MarksDirtyAndRecalculatesDependents()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "A1+1");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false,
            SourcePath: Path.Combine(Path.GetTempPath(), "Book.fxl")));
        session.SelectCell(a1);

        var result = session.CommitCellText("4");

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        sheet.GetCell(a1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(4);
        sheet.GetCell(b1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(5);
    }

    [Fact]
    public void SelectSheet_UpdatesActiveSheetCellTabsAndViewport()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        details.SetCell(new CellAddress(details.Id, 3, 2), new TextValue("detail"));

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var selected = session.SelectSheet(details.Id);

        selected.Should().BeTrue();
        session.ActiveSheet.Should().BeSameAs(details);
        workbook.ActiveSheetIndex.Should().Be(1);
        session.ActiveCell.Should().Be(new CellAddress(details.Id, 1, 1));
        session.SheetTabs.Should().Equal(
            new WorkbookSheetTab(summary.Id, "Sheet1", IsActive: false),
            new WorkbookSheetTab(details.Id, "Details", IsActive: true));
        session.Viewport.Cells.Should().Contain(cell => cell.Row == 3 && cell.Col == 2);
    }

    [Fact]
    public void MarkSaved_UpdatesDisplayNameAndClearsDirtyFeatureReport()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), "Book.xlsx");
        var savedPath = Path.Combine(Path.GetTempPath(), "Saved.fxl");
        var session = CreateSession(new StartupWorkbookLoadResult(
            CreateWorkbook(),
            "Book.xlsx",
            "Opened .xlsx.",
            IsFallback: false,
            SourcePath: sourcePath,
            FeatureReport: new XlsxFeatureReport(
            [
                new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Charts, "xl/charts/chart1.xml")
            ])));
        session.SelectCell(session.ActiveCell);
        session.CommitCellText("changed");

        session.MarkSaved(savedPath);

        session.IsDirty.Should().BeFalse();
        session.CurrentFilePath.Should().Be(savedPath);
        session.CurrentXlsxFeatureReport.Should().BeNull();
        session.DisplayName.Should().Be("Saved.fxl");
        session.Workbook.Name.Should().Be("Saved.fxl");
    }

    [Fact]
    public void BuildSuggestedSaveAsFileName_UsesWorkbookNameAndDefaultExtension()
    {
        var session = CreateSession(new StartupWorkbookLoadResult(
            CreateWorkbook("Quarterly Budget.xlsx"),
            "Quarterly Budget.xlsx",
            "Opened .xlsx.",
            IsFallback: false));

        session.BuildSuggestedSaveAsFileName(".fxl").Should().Be("Quarterly Budget.fxl");
        var pathWithoutExtension = Path.Combine(Path.GetTempPath(), "Budget");
        WorkbookSession.EnsureSaveExtension(pathWithoutExtension, ".fxl")
            .Should().Be(pathWithoutExtension + ".fxl");
    }

    private static WorkbookSession CreateSession(StartupWorkbookLoadResult source) =>
        new WorkbookSessionFactory().Create(source, viewportHeight: 240, viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
