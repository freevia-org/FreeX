using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionTests
{
    [Fact]
    public void CreateNew_CreatesCleanUnsavedDefaultWorkbookSession()
    {
        var session = new WorkbookSessionFactory().CreateNew(viewportHeight: 240, viewportWidth: 320);

        session.DisplayName.Should().Be(WorkbookFactory.DefaultWorkbookName);
        session.Workbook.Name.Should().Be(WorkbookFactory.DefaultWorkbookName);
        session.StartupStatus.Should().Be("Created new workbook.");
        session.CurrentFilePath.Should().BeNull();
        session.CurrentFileAccessIdentity.Should().BeNull();
        session.IsDirty.Should().BeFalse();
        session.CanSaveCurrentSource(out _).Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        session.CanRedo.Should().BeFalse();
        session.SheetTabs.Should().ContainSingle();
        session.ActiveSheet.Name.Should().Be("Sheet1");
        session.Viewport.RowMetrics.Should().NotBeEmpty();
        session.Viewport.ColMetrics.Should().NotBeEmpty();
    }

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
            OpenedAsTemplate: true,
            SourceFileAccessIdentity: new WorkbookFileAccessIdentity(
                sourcePath,
                "macos-security-scoped-bookmark",
                "template-token"));

        var session = CreateSession(source);

        session.CurrentFilePath.Should().BeNull();
        session.CurrentFileAccessIdentity.Should().BeNull();
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
        target!.Path.Should().Be(Path.GetFullPath("Book.XLSM"));
        target.FileAccessIdentity.Should().NotBeNull();
        target.FileAccessIdentity!.LocalPath.Should().Be(target.Path);
        target.FileAccessIdentity.HasBookmark.Should().BeFalse();
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
        malformedMessage.Should().Be("Open requires a local file path.");
    }

    [Theory]
    [InlineData("/Users/quinn/Work/Budget.XLSX", "/Users/quinn/Work/Budget.XLSX")]
    [InlineData("file:///Users/quinn/Work/Budget%202026.XLSX", "/Users/quinn/Work/Budget 2026.XLSX")]
    public void TryResolveOpenTarget_NormalizesMacOsLocalFileIngress(string candidate, string expectedPath)
    {
        var adapter = new TestFileAdapter(formats: [
            new FileFormatDescriptor(".xlsx", "Excel Workbook", CanOpen: true, CanSave: false)
        ]);
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(CreateWorkbook(), "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320,
            adapters: [adapter]);

        var resolved = session.TryResolveOpenTarget(candidate, out var target, out var message);

        resolved.Should().BeTrue();
        message.Should().BeEmpty();
        target.Should().NotBeNull();
        target!.Path.Should().Be(expectedPath);
        target.FileAccessIdentity.Should().NotBeNull();
        target.FileAccessIdentity!.LocalPath.Should().Be(expectedPath);
        target.Extension.Should().Be(".XLSX");
        target.Adapter.Should().BeSameAs(adapter);
    }

    [Fact]
    public void TryResolveOpenTarget_RejectsNonFileUri()
    {
        var session = CreateSession(new StartupWorkbookLoadResult(
            CreateWorkbook(),
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        session.TryResolveOpenTarget("https://example.test/Book.xlsx", out var target, out var message)
            .Should().BeFalse();

        target.Should().BeNull();
        message.Should().Be("Open requires a local file path.");
    }

    [Fact]
    public void SelectCurrentRegionOrAll_SelectsCurrentRegionThenWholeSheetWithoutDirtyingWorkbook()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(b2, new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(10));
        sheet.SetCell(c3, new NumberValue(20));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.BeginFormulaEdit(b2);

        var currentRegion = session.SelectCurrentRegionOrAll();

        currentRegion.Should().Be(new GridRange(b2, c3));
        session.SelectedRange.Should().Be(currentRegion);
        session.ActiveCell.Should().Be(b2);
        session.FormulaEditAddress.Should().BeNull();
        session.IsDirty.Should().BeFalse();

        var wholeSheet = session.SelectCurrentRegionOrAll();

        wholeSheet.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, CellAddress.MaxCol)));
        session.SelectedRange.Should().Be(wholeSheet);
        session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 1, 1));
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void SelectCurrentRegionOrAll_SelectsWholeSheetForBlankActiveCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var blank = new CellAddress(sheet.Id, 4, 4);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.BeginFormulaEdit(blank);

        var range = session.SelectCurrentRegionOrAll();

        range.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, CellAddress.MaxCol)));
        session.SelectedRange.Should().Be(range);
        session.FormulaEditAddress.Should().BeNull();
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void GoToReference_SelectsRangeAcrossSheetsWithoutDirtyingWorkbook()
    {
        var workbook = CreateWorkbook();
        var dataSheet = workbook.AddSheet("Data Sheet");
        var expectedRange = new GridRange(
            new CellAddress(dataSheet.Id, 2, 2),
            new CellAddress(dataSheet.Id, 4, 3));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.GoToReference("'Data Sheet'!B2:C4");

        result.Success.Should().BeTrue();
        result.SelectedRange.Should().Be(expectedRange);
        session.ActiveSheet.Id.Should().Be(dataSheet.Id);
        session.SelectedRange.Should().Be(expectedRange);
        session.ActiveCell.Should().Be(expectedRange.Start);
        session.FormulaEditAddress.Should().BeNull();
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void GoToReference_RejectsInvalidReferenceWithoutChangingSelection()
    {
        var workbook = CreateWorkbook();
        workbook.AddSheet("Data");
        var sheet = workbook.Sheets[0];
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var originalRange = session.SelectedRange;

        var result = session.GoToReference("Missing!A1");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Reference is not valid.");
        session.ActiveSheet.Id.Should().Be(sheet.Id);
        session.SelectedRange.Should().Be(originalRange);
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void OpenSelectedHyperlink_NavigatesDocumentReferenceWithoutDirtyingWorkbook()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var dataSheet = workbook.AddSheet("Data Sheet");
        var source = new CellAddress(sheet.Id, 1, 1);
        var expectedRange = new GridRange(
            new CellAddress(dataSheet.Id, 2, 2),
            new CellAddress(dataSheet.Id, 4, 3));
        sheet.Hyperlinks[source] = " 'Data Sheet'!B2:C4 ";
        sheet.HyperlinkMetadata[source] = new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(source);
        session.CanOpenSelectedHyperlink.Should().BeTrue();

        var result = session.OpenSelectedHyperlink();

        result.Success.Should().BeTrue();
        result.SelectedRange.Should().Be(expectedRange);
        session.ActiveSheet.Id.Should().Be(dataSheet.Id);
        session.SelectedRange.Should().Be(expectedRange);
        session.ActiveCell.Should().Be(expectedRange.Start);
        session.FormulaEditAddress.Should().BeNull();
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void OpenSelectedHyperlink_LeavesExternalUrlUnsupportedWithoutNavigation()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.Hyperlinks[source] = "https://example.test/report";
        sheet.HyperlinkMetadata[source] = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(source);

        var result = session.OpenSelectedHyperlink();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("External hyperlinks are not supported on this platform.");
        session.ActiveSheet.Id.Should().Be(sheet.Id);
        session.SelectedRange.Should().Be(new GridRange(source, source));
        session.ActiveCell.Should().Be(source);
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void TryGetSelectedHyperlinkPlan_ExposesExternalTargetWithoutNavigation()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.Hyperlinks[source] = " https://example.test/report ";
        sheet.HyperlinkMetadata[source] = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(source);

        session.TryGetSelectedHyperlinkPlan(out var plan).Should().BeTrue();

        plan.Should().Be(new HyperlinkNavigationPlan(
            HyperlinkNavigationKind.External,
            "https://example.test/report",
            null));
        session.ActiveSheet.Id.Should().Be(sheet.Id);
        session.SelectedRange.Should().Be(new GridRange(source, source));
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void TryGetSelectedHyperlinkPlan_ResolvesRelativeLocalFileAgainstWorkbookPathWithoutNavigation()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var source = new CellAddress(sheet.Id, 1, 1);
        var workbookDirectory = Path.Combine(Path.GetTempPath(), "FreeXWorkbookSessionHyperlinks");
        var workbookPath = Path.Combine(workbookDirectory, "Book.fxl");
        var expectedLocalPath = Path.GetFullPath(Path.Combine(workbookDirectory, "Reports", "Budget.xlsx"));
        sheet.Hyperlinks[source] = " Reports/Budget.xlsx ";
        sheet.HyperlinkMetadata[source] = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false,
            SourcePath: workbookPath));
        session.SelectCell(source);

        session.TryGetSelectedHyperlinkPlan(out var plan).Should().BeTrue();

        plan.Should().Be(new HyperlinkNavigationPlan(
            HyperlinkNavigationKind.LocalFile,
            "Reports/Budget.xlsx",
            null,
            expectedLocalPath));
        session.ActiveSheet.Id.Should().Be(sheet.Id);
        session.SelectedRange.Should().Be(new GridRange(source, source));
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void OpenSelectedHyperlink_LeavesLocalFileUnsupportedWithoutNavigation()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var source = new CellAddress(sheet.Id, 1, 1);
        var workbookPath = Path.Combine(Path.GetTempPath(), "FreeXWorkbookSessionHyperlinks", "Book.fxl");
        sheet.Hyperlinks[source] = " Reports/Budget.xlsx ";
        sheet.HyperlinkMetadata[source] = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false,
            SourcePath: workbookPath));
        session.SelectCell(source);

        var result = session.OpenSelectedHyperlink();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Local file hyperlinks require a platform file-opening route.");
        session.ActiveSheet.Id.Should().Be(sheet.Id);
        session.SelectedRange.Should().Be(new GridRange(source, source));
        session.ActiveCell.Should().Be(source);
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void GoToSpecial_SelectsBlanksWithoutDirtyingWorkbook()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new TextValue("filled"));
        sheet.SetCell(c1, new NumberValue(10));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, c1));

        var result = session.GoToSpecial(GoToSpecialKind.Blanks);

        result.Success.Should().BeTrue();
        result.MatchCount.Should().Be(1);
        result.SelectedRange.Should().Be(new GridRange(b1, b1));
        result.SelectedRanges.Should().Equal(new GridRange(b1, b1));
        session.SelectedRange.Should().Be(new GridRange(b1, b1));
        session.SelectedRanges.Should().Equal(new GridRange(b1, b1));
        session.ActiveCell.Should().Be(b1);
        session.FormulaEditAddress.Should().BeNull();
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void GoToSpecial_ConstantsHonorsValueTypeFiltersWithoutDirtyingWorkbook()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(42));
        sheet.SetCell(b1, new TextValue("text"));
        sheet.SetCell(c1, new BoolValue(true));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, c1));

        var result = session.GoToSpecial(
            GoToSpecialKind.Constants,
            new GoToSpecialOptions(GoToSpecialValueTypes.Numbers | GoToSpecialValueTypes.Logicals));

        result.Success.Should().BeTrue();
        result.MatchCount.Should().Be(2);
        result.SelectedRange.Should().Be(new GridRange(a1, a1));
        result.SelectedRanges.Should().Equal(new GridRange(a1, a1), new GridRange(c1, c1));
        session.SelectedRange.Should().Be(new GridRange(a1, a1));
        session.SelectedRanges.Should().Equal(new GridRange(a1, a1), new GridRange(c1, c1));
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void GoToSpecial_VisibleCellsOnlySkipsHiddenRowsAndColumns()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.HiddenRows.Add(2);
        sheet.HiddenCols.Add(2);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b2));

        var result = session.GoToSpecial(GoToSpecialKind.VisibleCellsOnly);

        result.Success.Should().BeTrue();
        result.MatchCount.Should().Be(1);
        result.SelectedRanges.Should().Equal(new GridRange(a1, a1));
        session.SelectedRange.Should().Be(new GridRange(a1, a1));
        session.SelectedRanges.Should().Equal(new GridRange(a1, a1));
        session.ActiveCell.Should().Be(a1);
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void GoToSpecial_NoMatchesLeavesSelectionUnchanged()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(1));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var originalRange = session.SelectedRange;

        var result = session.GoToSpecial(GoToSpecialKind.Blanks);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("No cells found.");
        result.SelectedRange.Should().BeNull();
        result.SelectedRanges.Should().BeEmpty();
        session.SelectedRange.Should().Be(originalRange);
        session.SelectedRanges.Should().Equal(originalRange);
        session.ActiveCell.Should().Be(a1);
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void FindNext_StartsAfterActiveCellThenWrapsAndStoresLastText()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, new TextValue("needle one"));
        sheet.SetCell(c1, new TextValue("needle two"));
        sheet.SetCell(a3, new TextValue("needle three"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var first = session.FindNext("needle");
        var second = session.FindNext();
        var third = session.FindNext();

        first.Success.Should().BeTrue();
        first.SelectedRange.Should().Be(new GridRange(c1, c1));
        first.MatchIndex.Should().Be(2);
        first.MatchCount.Should().Be(3);
        second.SelectedRange.Should().Be(new GridRange(a3, a3));
        second.MatchIndex.Should().Be(3);
        third.SelectedRange.Should().Be(new GridRange(a1, a1));
        third.MatchIndex.Should().Be(1);
        session.LastFindText.Should().Be("needle");
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void FindAll_ProjectsRowsWithoutChangingSelectionOrDirtyingWorkbook()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Budget");
        workbook.ActiveSheetIndex = 0;
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(a1, Cell.FromFormula("=SUM(B2:B3)"));
        sheet.SetCell(b2, new TextValue("Budget match"));
        workbook.DefineNamedRange("InputCell", new GridRange(b2, b2));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book1.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(c3);
        var originalSelection = session.SelectedRange;

        var formulaResult = session.FindAll("SUM");
        var valueResult = session.FindAll("Budget");

        formulaResult.Success.Should().BeTrue();
        formulaResult.Matches.Should().Equal(
            new WorkbookFindAllMatch("Book1", "Budget", "", a1, "A1", "=SUM(B2:B3)", "=SUM(B2:B3)"));
        valueResult.Success.Should().BeTrue();
        valueResult.Matches.Should().Equal(
            new WorkbookFindAllMatch("Book1", "Budget", "InputCell", b2, "B2", "Budget match", ""));
        valueResult.MatchCount.Should().Be(1);
        session.SelectedRange.Should().Be(originalSelection);
        session.ActiveCell.Should().Be(c3);
        session.LastFindText.Should().Be("Budget");
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void FindAll_CanSearchWorkbookScopeWithoutChangingActiveSheet()
    {
        var workbook = CreateWorkbook();
        var first = workbook.Sheets.Single();
        var second = workbook.AddSheet("Second");
        var firstA1 = new CellAddress(first.Id, 1, 1);
        var secondA1 = new CellAddress(second.Id, 1, 1);
        first.SetCell(firstA1, new TextValue("needle first"));
        second.SetCell(secondA1, new TextValue("needle second"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.FindAll(
            "needle",
            new FindOptions(Within: FindWithin.Workbook, LookIn: FindLookIn.Values));

        result.Success.Should().BeTrue();
        result.Matches.Select(match => match.Address).Should().Equal(firstA1, secondA1);
        result.Matches.Select(match => match.Sheet).Should().Equal("Sheet1", "Second");
        session.ActiveSheet.Should().BeSameAs(first);
        session.SelectedRange.Should().Be(new GridRange(new CellAddress(first.Id, 1, 1), new CellAddress(first.Id, 1, 1)));
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void CreateFormatDiffFromActiveCell_CapturesStyleOnlyFormatting()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var fill = new CellColor(255, 242, 204);
        var styleId = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = fill,
            NumberFormat = "$#,##0.00",
            HorizontalAlignment = HorizontalAlignment.Center
        });
        sheet.SetStyleOnly(b2.Row, b2.Col, styleId);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(b2);

        var diff = session.CreateFormatDiffFromActiveCell();

        diff.Should().NotBeNull();
        diff!.Bold.Should().BeTrue();
        diff.FillColor.Should().Be(fill);
        diff.NumberFormat.Should().Be("$#,##0.00");
        diff.HAlign.Should().Be(HorizontalAlignment.Center);
        sheet.GetCell(b2).Should().BeNull();
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void FindAll_WithRequiredFormatFiltersThroughWorkbookSession()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        var yellow = new CellColor(255, 255, 0);
        var requiredStyleId = workbook.RegisterStyle(new CellStyle { Bold = true, FillColor = yellow });
        var partialStyleId = workbook.RegisterStyle(new CellStyle { FillColor = yellow });
        sheet.SetCell(a1, new TextValue("needle"));
        sheet.SetCell(a2, new TextValue("needle"));
        sheet.SetCell(a3, new TextValue("needle"));
        sheet.GetCell(a1)!.StyleId = requiredStyleId;
        sheet.GetCell(a2)!.StyleId = partialStyleId;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a3);

        var result = session.FindAll(
            "needle",
            new FindOptions(
                Within: FindWithin.Sheet,
                LookIn: FindLookIn.Values,
                RequiredFormat: new StyleDiff(Bold: true, FillColor: yellow)));

        result.Success.Should().BeTrue();
        result.Matches.Select(match => match.Address).Should().Equal(a1);
        result.MatchCount.Should().Be(1);
        session.SelectedRange.Should().Be(new GridRange(a3, a3));
        session.ActiveCell.Should().Be(a3);
        session.LastFindText.Should().Be("needle");
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void FindAll_WithCommentsLookInReportsThreadedRootAndReplyMatchesAtSameCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(a1, new TextValue("foo value"));
        sheet.ThreadedComments[a1] = new ThreadedComment("foo root", "Quinn")
        {
            Replies =
            [
                new CommentReply("foo reply", "Codex"),
                new CommentReply("other reply", "FreeX")
            ]
        };
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(c3);
        var originalSelection = session.SelectedRange;

        var result = session.FindAll(
            "foo",
            new FindOptions(Within: FindWithin.Sheet, LookIn: FindLookIn.Comments));

        result.Success.Should().BeTrue();
        result.MatchCount.Should().Be(2);
        result.Matches.Should().Equal(
            new WorkbookFindAllMatch("Book", "Sheet1", "", a1, "A1", "foo root", ""),
            new WorkbookFindAllMatch("Book", "Sheet1", "", a1, "A1", "foo reply", ""));
        session.SelectedRange.Should().Be(originalSelection);
        session.ActiveCell.Should().Be(c3);
        session.LastFindText.Should().Be("foo");
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void FindAll_NoMatchesUpdatesLastFindTextWithoutChangingSelectionOrDirtyingWorkbook()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("value"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(b2);

        var result = session.FindAll("missing");

        result.Success.Should().BeTrue();
        result.MatchCount.Should().Be(0);
        result.Matches.Should().BeEmpty();
        session.LastFindText.Should().Be("missing");
        session.SelectedRange.Should().Be(new GridRange(b2, b2));
        session.ActiveCell.Should().Be(b2);
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void FindAll_RejectsEmptyFindText()
    {
        var session = CreateSession(new StartupWorkbookLoadResult(
            CreateWorkbook(),
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.FindAll("");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Find text is required.");
        result.Matches.Should().BeEmpty();
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void FindNext_WithoutArgumentsRepeatsLastOptionsAndMatchFlags()
    {
        var workbook = CreateWorkbook();
        var firstSheet = workbook.Sheets.Single();
        var secondSheet = workbook.AddSheet("Second");
        var firstA1 = new CellAddress(firstSheet.Id, 1, 1);
        var secondA1 = new CellAddress(secondSheet.Id, 1, 1);
        firstSheet.SetCell(firstA1, new TextValue("Needle first"));
        firstSheet.SetCell(new CellAddress(firstSheet.Id, 2, 1), new TextValue("needle wrong case"));
        secondSheet.SetCell(secondA1, new TextValue("Needle second"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(firstA1);

        var first = session.FindNext(
            "Needle",
            new FindOptions(Within: FindWithin.Workbook, LookIn: FindLookIn.Values),
            matchCase: true);
        var second = session.FindNext();

        first.Success.Should().BeTrue();
        first.SelectedRange.Should().Be(new GridRange(secondA1, secondA1));
        first.MatchCount.Should().Be(2);
        second.Success.Should().BeTrue();
        second.SelectedRange.Should().Be(new GridRange(firstA1, firstA1));
        second.MatchCount.Should().Be(2);
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void ReplaceAllValues_ReplacesActiveSheetValuesPreservesSelectionAndSupportsUndoRedo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var otherSheet = workbook.AddSheet("Other");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var otherA1 = new CellAddress(otherSheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("foo one"));
        sheet.SetCell(c1, new TextValue("foo two"));
        otherSheet.SetCell(otherA1, new TextValue("foo other"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(b2);

        var result = session.ReplaceAllValues("foo", "bar");

        result.Success.Should().BeTrue();
        result.ReplacedCount.Should().Be(2);
        session.SelectedRange.Should().Be(new GridRange(b2, b2));
        session.ActiveCell.Should().Be(b2);
        session.LastFindText.Should().Be("foo");
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("bar one");
        sheet.GetCell(c1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("bar two");
        otherSheet.GetCell(otherA1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo other");

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        session.CanRedo.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo one");
        sheet.GetCell(c1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo two");

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("bar one");
        sheet.GetCell(c1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("bar two");
    }

    [Fact]
    public void ReplaceAllValues_WithReplacementFormatAppliesCellStyleAndUndoRedoRestoresValuesAndStyles()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var selection = new CellAddress(sheet.Id, 2, 2);
        var replacementFill = new CellColor(255, 255, 0);
        var unchangedFill = new CellColor(221, 235, 247);
        var originalA1Style = workbook.RegisterStyle(new CellStyle
        {
            Italic = true,
            NumberFormat = "0.0"
        });
        var unchangedStyle = workbook.RegisterStyle(new CellStyle { FillColor = unchangedFill });
        sheet.SetCell(a1, new TextValue("foo one"));
        sheet.SetCell(b1, new TextValue("foo two"));
        sheet.SetCell(c1, new TextValue("other"));
        sheet.GetCell(a1)!.StyleId = originalA1Style;
        sheet.GetCell(c1)!.StyleId = unchangedStyle;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(selection);

        var result = session.ReplaceAllValues(
            "foo",
            "bar",
            new FindOptions(Within: FindWithin.Sheet, LookIn: FindLookIn.Values),
            replacementFormat: new StyleDiff(
                Bold: true,
                FillColor: replacementFill,
                NumberFormat: "$#,##0.00"));

        result.Success.Should().BeTrue();
        result.ReplacedCount.Should().Be(2);
        result.MatchCount.Should().Be(2);
        session.SelectedRange.Should().Be(new GridRange(selection, selection));
        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("bar one");
        sheet.GetCell(b1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("bar two");
        sheet.GetCell(c1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("other");
        GetStyle(workbook, sheet, a1).Bold.Should().BeTrue();
        GetStyle(workbook, sheet, a1).Italic.Should().BeTrue();
        GetStyle(workbook, sheet, a1).FillColor.Should().Be(replacementFill);
        GetStyle(workbook, sheet, a1).NumberFormat.Should().Be("$#,##0.00");
        GetStyle(workbook, sheet, b1).Bold.Should().BeTrue();
        GetStyle(workbook, sheet, b1).FillColor.Should().Be(replacementFill);
        GetStyle(workbook, sheet, b1).NumberFormat.Should().Be("$#,##0.00");
        GetStyle(workbook, sheet, c1).FillColor.Should().Be(unchangedFill);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();

        session.UndoLastEdit().Success.Should().BeTrue();

        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo one");
        sheet.GetCell(b1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo two");
        sheet.GetCell(c1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("other");
        GetStyle(workbook, sheet, a1).Should().Be(workbook.GetStyle(originalA1Style));
        GetStyle(workbook, sheet, b1).Should().Be(CellStyle.Default);
        GetStyle(workbook, sheet, c1).Should().Be(workbook.GetStyle(unchangedStyle));
        session.CanRedo.Should().BeTrue();

        session.RedoLastEdit().Success.Should().BeTrue();

        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("bar one");
        sheet.GetCell(b1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("bar two");
        GetStyle(workbook, sheet, a1).FillColor.Should().Be(replacementFill);
        GetStyle(workbook, sheet, b1).FillColor.Should().Be(replacementFill);
        GetStyle(workbook, sheet, c1).FillColor.Should().Be(unchangedFill);
    }

    [Fact]
    public void ReplaceAllValues_CanReplaceWorkbookScopeValuesWithoutChangingActiveSheet()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var otherSheet = workbook.AddSheet("Other");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var otherA1 = new CellAddress(otherSheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("foo active"));
        otherSheet.SetCell(otherA1, new TextValue("foo other"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(b2);

        var result = session.ReplaceAllValues(
            "foo",
            "bar",
            new FindOptions(Within: FindWithin.Workbook, LookIn: FindLookIn.Values));

        result.Success.Should().BeTrue();
        result.ReplacedCount.Should().Be(2);
        result.MatchCount.Should().Be(2);
        session.ActiveSheet.Should().BeSameAs(sheet);
        session.SelectedRange.Should().Be(new GridRange(b2, b2));
        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("bar active");
        otherSheet.GetCell(otherA1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("bar other");

        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo active");
        otherSheet.GetCell(otherA1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo other");
    }

    [Fact]
    public void ReplaceAllValues_WithFormulaLookInReplacesFormulaTextAndSupportsUndoRedo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var otherSheet = workbook.AddSheet("Other");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var otherA1 = new CellAddress(otherSheet.Id, 1, 1);
        sheet.SetCell(a1, Cell.FromFormula("SUM(B1:B3)"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle { Bold = true });
        otherSheet.SetCell(otherA1, Cell.FromFormula("SUM(C1:C3)"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(b2);

        var result = session.ReplaceAllValues(
            "SUM",
            "MAX",
            new FindOptions(Within: FindWithin.Workbook, LookIn: FindLookIn.Formulas));

        result.Success.Should().BeTrue();
        result.ReplacedCount.Should().Be(2);
        result.MatchCount.Should().Be(2);
        session.SelectedRange.Should().Be(new GridRange(b2, b2));
        session.IsDirty.Should().BeTrue();
        sheet.GetCell(a1)!.FormulaText.Should().Be("MAX(B1:B3)");
        sheet.GetCell(a1)!.StyleId.Should().NotBe(StyleId.Default);
        otherSheet.GetCell(otherA1)!.FormulaText.Should().Be("MAX(C1:C3)");

        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.GetCell(a1)!.FormulaText.Should().Be("SUM(B1:B3)");
        otherSheet.GetCell(otherA1)!.FormulaText.Should().Be("SUM(C1:C3)");

        session.RedoLastEdit().Success.Should().BeTrue();
        sheet.GetCell(a1)!.FormulaText.Should().Be("MAX(B1:B3)");
        otherSheet.GetCell(otherA1)!.FormulaText.Should().Be("MAX(C1:C3)");
    }

    [Fact]
    public void ReplaceAllValues_WithNotesLookInReplacesSimpleNoteTextAndPreservesCells()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("foo value"));
        sheet.SetCell(b1, Cell.FromFormula("FOO(A1)"));
        sheet.Comments[a1] = "foo note";
        sheet.Comments[b1] = "foo formula note";
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.ReplaceAllValues(
            "foo",
            "bar",
            new FindOptions(Within: FindWithin.Sheet, LookIn: FindLookIn.Notes));

        result.Success.Should().BeTrue();
        result.ReplacedCount.Should().Be(2);
        result.MatchCount.Should().Be(2);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo value");
        sheet.GetCell(b1)!.FormulaText.Should().Be("FOO(A1)");
        sheet.Comments[a1].Should().Be("bar note");
        sheet.Comments[b1].Should().Be("bar formula note");

        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.Comments[a1].Should().Be("foo note");
        sheet.Comments[b1].Should().Be("foo formula note");
        session.CanRedo.Should().BeTrue();

        session.RedoLastEdit().Success.Should().BeTrue();
        sheet.Comments[a1].Should().Be("bar note");
        sheet.Comments[b1].Should().Be("bar formula note");
    }

    [Fact]
    public void ReplaceAllValues_WithCommentsLookInReplacesThreadedRootAndReplyTextPreservesMetadataAndCells()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("foo value"));
        sheet.SetCell(b1, Cell.FromFormula("FOO(A1)"));
        sheet.ThreadedComments[a1] = new ThreadedComment("foo root", "Quinn")
        {
            Replies = [new CommentReply("foo reply", "Codex")],
            IsResolved = true
        };
        sheet.ThreadedComments[b1] = new ThreadedComment("foo formula root", "FreeX");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.ReplaceAllValues(
            "foo",
            "bar",
            new FindOptions(Within: FindWithin.Sheet, LookIn: FindLookIn.Comments));

        result.Success.Should().BeTrue();
        result.ReplacedCount.Should().Be(3);
        result.MatchCount.Should().Be(3);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo value");
        sheet.GetCell(b1)!.FormulaText.Should().Be("FOO(A1)");
        sheet.ThreadedComments[a1].Text.Should().Be("bar root");
        sheet.ThreadedComments[a1].Author.Should().Be("Quinn");
        sheet.ThreadedComments[a1].Replies.Should().ContainSingle().Which.Text.Should().Be("bar reply");
        sheet.ThreadedComments[a1].Replies.Single().Author.Should().Be("Codex");
        sheet.ThreadedComments[a1].IsResolved.Should().BeTrue();
        sheet.ThreadedComments[b1].Text.Should().Be("bar formula root");

        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.ThreadedComments[a1].Text.Should().Be("foo root");
        sheet.ThreadedComments[a1].Replies.Should().Equal(new CommentReply("foo reply", "Codex"));
        sheet.ThreadedComments[a1].IsResolved.Should().BeTrue();
        sheet.ThreadedComments[b1].Text.Should().Be("foo formula root");
        session.CanRedo.Should().BeTrue();

        session.RedoLastEdit().Success.Should().BeTrue();
        sheet.ThreadedComments[a1].Text.Should().Be("bar root");
        sheet.ThreadedComments[a1].Replies.Should().ContainSingle().Which.Text.Should().Be("bar reply");
        sheet.ThreadedComments[b1].Text.Should().Be("bar formula root");
    }

    [Fact]
    public void ReplaceAllValues_WithCommentsLookInReplacesThreadedRootAndRepliesAtSameCellPreservesMetadata()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var rootCreatedAt = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var firstReplyCreatedAt = rootCreatedAt.AddMinutes(5);
        var secondReplyCreatedAt = rootCreatedAt.AddMinutes(10);
        var unchangedReplyCreatedAt = rootCreatedAt.AddMinutes(15);
        sheet.SetCell(a1, new TextValue("foo value"));
        sheet.ThreadedComments[a1] = new ThreadedComment("foo root", "Quinn")
        {
            Replies =
            [
                new CommentReply("foo first reply", "Codex") { CreatedAtUtc = firstReplyCreatedAt },
                new CommentReply("foo second reply", "FreeX") { CreatedAtUtc = secondReplyCreatedAt },
                new CommentReply("keep reply", "Reviewer") { CreatedAtUtc = unchangedReplyCreatedAt }
            ],
            IsResolved = true,
            CreatedAtUtc = rootCreatedAt
        };
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.ReplaceAllValues(
            "foo",
            "bar",
            new FindOptions(Within: FindWithin.Sheet, LookIn: FindLookIn.Comments));

        result.Success.Should().BeTrue();
        result.ReplacedCount.Should().Be(3);
        result.MatchCount.Should().Be(3);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo value");

        var replacedComment = sheet.ThreadedComments[a1];
        replacedComment.Text.Should().Be("bar root");
        replacedComment.Author.Should().Be("Quinn");
        replacedComment.IsResolved.Should().BeTrue();
        replacedComment.CreatedAtUtc.Should().Be(rootCreatedAt);
        replacedComment.Replies.Select(reply => reply.Text).Should().Equal(
            "bar first reply",
            "bar second reply",
            "keep reply");
        replacedComment.Replies.Select(reply => reply.Author).Should().Equal("Codex", "FreeX", "Reviewer");
        replacedComment.Replies.Select(reply => reply.CreatedAtUtc).Should().Equal(
            firstReplyCreatedAt,
            secondReplyCreatedAt,
            unchangedReplyCreatedAt);

        session.UndoLastEdit().Success.Should().BeTrue();
        var restoredComment = sheet.ThreadedComments[a1];
        restoredComment.Text.Should().Be("foo root");
        restoredComment.Author.Should().Be("Quinn");
        restoredComment.IsResolved.Should().BeTrue();
        restoredComment.Replies.Select(reply => reply.Text).Should().Equal(
            "foo first reply",
            "foo second reply",
            "keep reply");
        restoredComment.Replies.Select(reply => reply.Author).Should().Equal("Codex", "FreeX", "Reviewer");
        session.CanRedo.Should().BeTrue();

        session.RedoLastEdit().Success.Should().BeTrue();
        var redoneComment = sheet.ThreadedComments[a1];
        redoneComment.Text.Should().Be("bar root");
        redoneComment.Replies.Select(reply => reply.Text).Should().Equal(
            "bar first reply",
            "bar second reply",
            "keep reply");
        redoneComment.Author.Should().Be("Quinn");
        redoneComment.IsResolved.Should().BeTrue();
    }

    [Fact]
    public void ReplaceAllValues_SkipsFormulaCellsWithoutDirtyingWorkbook()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var formulaCell = Cell.FromFormula("A2");
        formulaCell.Value = new TextValue("foo calculated");
        sheet.SetCell(a1, formulaCell);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.ReplaceAllValues("foo", "bar");

        result.Success.Should().BeTrue();
        result.ReplacedCount.Should().Be(0);
        session.LastFindText.Should().Be("foo");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        sheet.GetCell(a1)!.FormulaText.Should().Be("A2");
        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo calculated");
    }

    [Fact]
    public void ReplaceAllValues_NoMatchesLeavesWorkbookClean()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("value"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.ReplaceAllValues("missing", "bar");

        result.Success.Should().BeTrue();
        result.ReplacedCount.Should().Be(0);
        session.LastFindText.Should().Be("missing");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void ReplaceAllValues_RejectsEmptyFindText()
    {
        var session = CreateSession(new StartupWorkbookLoadResult(
            CreateWorkbook(),
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.ReplaceAllValues("", "bar");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Find text is required.");
        result.ReplacedCount.Should().Be(0);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void ReplaceNextValue_ReplacesNextActiveSheetValueSelectsCellAndSupportsUndoRedo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, new TextValue("foo one"));
        sheet.SetCell(c1, new TextValue("foo two"));
        sheet.SetCell(a3, new TextValue("foo three"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.ReplaceNextValue("foo", "bar");

        result.Success.Should().BeTrue();
        result.ReplacedCount.Should().Be(1);
        result.ReplacedRange.Should().Be(new GridRange(c1, c1));
        result.MatchIndex.Should().Be(2);
        result.MatchCount.Should().Be(3);
        session.SelectedRange.Should().Be(new GridRange(c1, c1));
        session.ActiveCell.Should().Be(c1);
        session.LastFindText.Should().Be("foo");
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo one");
        sheet.GetCell(c1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("bar two");
        sheet.GetCell(a3)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo three");

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.GetCell(c1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo two");
        session.CanRedo.Should().BeTrue();

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        sheet.GetCell(c1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("bar two");
    }

    [Fact]
    public void ReplaceNextValue_WithReplacementFormatAppliesOnlyToReplacedCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        var replacementFill = new CellColor(198, 239, 206);
        var originalA1Style = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(221, 235, 247) });
        sheet.SetCell(a1, new TextValue("foo one"));
        sheet.SetCell(c1, new TextValue("foo two"));
        sheet.SetCell(a3, new TextValue("foo three"));
        sheet.GetCell(a1)!.StyleId = originalA1Style;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.ReplaceNextValue(
            "foo",
            "bar",
            new FindOptions(Within: FindWithin.Sheet, LookIn: FindLookIn.Values),
            replacementFormat: new StyleDiff(Italic: true, FillColor: replacementFill));

        result.Success.Should().BeTrue();
        result.ReplacedCount.Should().Be(1);
        result.ReplacedRange.Should().Be(new GridRange(c1, c1));
        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo one");
        sheet.GetCell(c1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("bar two");
        sheet.GetCell(a3)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo three");
        GetStyle(workbook, sheet, a1).Should().Be(workbook.GetStyle(originalA1Style));
        GetStyle(workbook, sheet, c1).Italic.Should().BeTrue();
        GetStyle(workbook, sheet, c1).FillColor.Should().Be(replacementFill);
        GetStyle(workbook, sheet, a3).FillColor.Should().BeNull();
        session.SelectedRange.Should().Be(new GridRange(c1, c1));
        session.ActiveCell.Should().Be(c1);
        session.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void ReplaceNextValue_ReplacesCurrentFoundValueThenNextCallAdvances()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new TextValue("foo one"));
        sheet.SetCell(b1, new TextValue("foo two"));
        sheet.SetCell(c1, new TextValue("foo three"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var found = session.FindNext("foo");
        var firstReplace = session.ReplaceNextValue("foo", "bar");
        var secondReplace = session.ReplaceNextValue("foo", "bar");

        found.SelectedRange.Should().Be(new GridRange(b1, b1));
        firstReplace.Success.Should().BeTrue();
        firstReplace.ReplacedRange.Should().Be(new GridRange(b1, b1));
        secondReplace.Success.Should().BeTrue();
        secondReplace.ReplacedRange.Should().Be(new GridRange(c1, c1));
        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo one");
        sheet.GetCell(b1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("bar two");
        sheet.GetCell(c1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("bar three");
        session.SelectedRange.Should().Be(new GridRange(c1, c1));
        session.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void ReplaceNextValue_WithWorkbookScopeCanMoveToNextSheet()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var otherSheet = workbook.AddSheet("Other");
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var otherA1 = new CellAddress(otherSheet.Id, 1, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("foo active"));
        otherSheet.SetCell(otherA1, new TextValue("foo other"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(c1);

        var result = session.ReplaceNextValue(
            "foo",
            "bar",
            new FindOptions(Within: FindWithin.Workbook, LookIn: FindLookIn.Values));

        result.Success.Should().BeTrue();
        result.ReplacedCount.Should().Be(1);
        result.ReplacedRange.Should().Be(new GridRange(otherA1, otherA1));
        session.ActiveSheet.Should().BeSameAs(otherSheet);
        otherSheet.GetCell(otherA1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("bar other");
    }

    [Fact]
    public void ReplaceNextValue_WithFormulaLookInReplacesFormulaText()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, Cell.FromFormula("SUM(A2:A3)"));
        sheet.SetCell(c1, Cell.FromFormula("SUM(C2:C3)"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.ReplaceNextValue(
            "SUM",
            "MAX",
            new FindOptions(Within: FindWithin.Sheet, LookIn: FindLookIn.Formulas));

        result.Success.Should().BeTrue();
        result.ReplacedCount.Should().Be(1);
        result.ReplacedRange.Should().Be(new GridRange(c1, c1));
        result.MatchIndex.Should().Be(2);
        result.MatchCount.Should().Be(2);
        sheet.GetCell(a1)!.FormulaText.Should().Be("SUM(A2:A3)");
        sheet.GetCell(c1)!.FormulaText.Should().Be("MAX(C2:C3)");

        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.GetCell(c1)!.FormulaText.Should().Be("SUM(C2:C3)");
    }

    [Fact]
    public void ReplaceNextValue_WithNotesLookInReplacesSimpleNoteTextAndPreservesCells()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new TextValue("foo value"));
        sheet.SetCell(c1, Cell.FromFormula("FOO(A1)"));
        sheet.Comments[a1] = "foo first note";
        sheet.Comments[c1] = "foo next note";
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.ReplaceNextValue(
            "foo",
            "bar",
            new FindOptions(Within: FindWithin.Sheet, LookIn: FindLookIn.Notes));

        result.Success.Should().BeTrue();
        result.ReplacedCount.Should().Be(1);
        result.ReplacedRange.Should().Be(new GridRange(c1, c1));
        result.MatchIndex.Should().Be(2);
        result.MatchCount.Should().Be(2);
        session.SelectedRange.Should().Be(new GridRange(c1, c1));
        session.ActiveCell.Should().Be(c1);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo value");
        sheet.GetCell(c1)!.FormulaText.Should().Be("FOO(A1)");
        sheet.Comments[a1].Should().Be("foo first note");
        sheet.Comments[c1].Should().Be("bar next note");

        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.Comments[c1].Should().Be("foo next note");
        session.CanRedo.Should().BeTrue();

        session.RedoLastEdit().Success.Should().BeTrue();
        sheet.Comments[c1].Should().Be("bar next note");
    }

    [Fact]
    public void ReplaceNextValue_WithCommentsLookInReplacesThreadedRootTextAndPreservesRepliesAndCells()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new TextValue("foo value"));
        sheet.SetCell(c1, Cell.FromFormula("FOO(A1)"));
        sheet.ThreadedComments[a1] = new ThreadedComment("foo first root", "Quinn");
        sheet.ThreadedComments[c1] = new ThreadedComment("foo next root", "Quinn")
        {
            Replies = [new CommentReply("foo reply", "Codex")],
            IsResolved = true
        };
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.ReplaceNextValue(
            "foo",
            "bar",
            new FindOptions(Within: FindWithin.Sheet, LookIn: FindLookIn.Comments));

        result.Success.Should().BeTrue();
        result.ReplacedCount.Should().Be(1);
        result.ReplacedRange.Should().Be(new GridRange(c1, c1));
        result.MatchIndex.Should().Be(2);
        result.MatchCount.Should().Be(3);
        session.SelectedRange.Should().Be(new GridRange(c1, c1));
        session.ActiveCell.Should().Be(c1);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo value");
        sheet.GetCell(c1)!.FormulaText.Should().Be("FOO(A1)");
        sheet.ThreadedComments[a1].Text.Should().Be("foo first root");
        sheet.ThreadedComments[c1].Text.Should().Be("bar next root");
        sheet.ThreadedComments[c1].Replies.Should().Equal(new CommentReply("foo reply", "Codex"));
        sheet.ThreadedComments[c1].IsResolved.Should().BeTrue();

        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.ThreadedComments[c1].Text.Should().Be("foo next root");
        sheet.ThreadedComments[c1].Replies.Should().Equal(new CommentReply("foo reply", "Codex"));
        sheet.ThreadedComments[c1].IsResolved.Should().BeTrue();
        session.CanRedo.Should().BeTrue();

        session.RedoLastEdit().Success.Should().BeTrue();
        sheet.ThreadedComments[c1].Text.Should().Be("bar next root");
    }

    [Fact]
    public void ReplaceNextValue_WithCommentsLookInTargetsThreadedRootThenReplyAtSameCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("foo value"));
        sheet.ThreadedComments[a1] = new ThreadedComment("foo root", "Quinn")
        {
            Replies = [new CommentReply("foo reply", "Codex")],
            IsResolved = true
        };
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);
        var options = new FindOptions(Within: FindWithin.Sheet, LookIn: FindLookIn.Comments);

        var rootReplace = session.ReplaceNextValue("foo", "bar", options);

        rootReplace.Success.Should().BeTrue();
        rootReplace.ReplacedCount.Should().Be(1);
        rootReplace.ReplacedRange.Should().Be(new GridRange(a1, a1));
        rootReplace.MatchIndex.Should().Be(1);
        rootReplace.MatchCount.Should().Be(2);
        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo value");
        sheet.ThreadedComments[a1].Text.Should().Be("bar root");
        sheet.ThreadedComments[a1].Author.Should().Be("Quinn");
        sheet.ThreadedComments[a1].Replies.Should().ContainSingle()
            .Which.Should().Be(new CommentReply("foo reply", "Codex"));
        sheet.ThreadedComments[a1].IsResolved.Should().BeTrue();

        var replyReplace = session.ReplaceNextValue("foo", "bar", options);

        replyReplace.Success.Should().BeTrue();
        replyReplace.ReplacedCount.Should().Be(1);
        replyReplace.ReplacedRange.Should().Be(new GridRange(a1, a1));
        replyReplace.MatchIndex.Should().Be(1);
        replyReplace.MatchCount.Should().Be(1);
        session.SelectedRange.Should().Be(new GridRange(a1, a1));
        session.ActiveCell.Should().Be(a1);
        sheet.ThreadedComments[a1].Text.Should().Be("bar root");
        sheet.ThreadedComments[a1].Author.Should().Be("Quinn");
        sheet.ThreadedComments[a1].Replies.Should().ContainSingle()
            .Which.Text.Should().Be("bar reply");
        sheet.ThreadedComments[a1].Replies.Single().Author.Should().Be("Codex");
        sheet.ThreadedComments[a1].Replies.Single().ModifiedAtUtc.Should().NotBeNull();
        sheet.ThreadedComments[a1].IsResolved.Should().BeTrue();
        session.LastFindText.Should().Be("foo");
        session.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void ReplaceNextValue_FormulaMatchSelectsWithoutDirtyingWorkbook()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var formulaCell = Cell.FromFormula("A2");
        formulaCell.Value = new TextValue("foo calculated");
        sheet.SetCell(a1, formulaCell);
        sheet.SetCell(b1, new TextValue("foo value"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(b1);

        var result = session.ReplaceNextValue("foo", "bar");

        result.Success.Should().BeTrue();
        result.ReplacedCount.Should().Be(0);
        result.ReplacedRange.Should().Be(new GridRange(a1, a1));
        result.MatchIndex.Should().Be(1);
        result.MatchCount.Should().Be(2);
        session.SelectedRange.Should().Be(new GridRange(a1, a1));
        session.ActiveCell.Should().Be(a1);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        sheet.GetCell(a1)!.FormulaText.Should().Be("A2");
        sheet.GetCell(a1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo calculated");
        sheet.GetCell(b1)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("foo value");
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
        var identity = new WorkbookFileAccessIdentity(
            path,
            "macos-security-scoped-bookmark",
            "template-token");
        var target = new WorkbookOpenTarget(path, adapter, ".xltx", format, identity);
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
        session.CurrentFileAccessIdentity.Should().BeNull();
        session.CurrentXlsxFeatureReport.Should().BeSameAs(featureReport);
        session.DisplayName.Should().Be("Budget.xltx");
        session.Workbook.Name.Should().Be("Budget.xltx");
        session.StartupStatus.Should().Contain("Opened as template.");
        session.StartupStatus.Should().Contain("Unsupported XLSX features detected.");
        session.StartupStatus.Should().Contain("1 load warning.");
    }

    [Fact]
    public void CreateOpened_CarriesTargetFileAccessIdentityIntoCurrentSession()
    {
        var path = Path.Combine(Path.GetTempPath(), "Budget.fxl");
        var format = new FileFormatDescriptor(".fxl", "FreeX Workbook", CanOpen: true, CanSave: true);
        var adapter = new TestFileAdapter(formats: [format]);
        var workbook = CreateWorkbook("Budget");
        var identity = new WorkbookFileAccessIdentity(
            path,
            "macos-security-scoped-bookmark",
            "open-token");
        var target = new WorkbookOpenTarget(path, adapter, ".fxl", format, identity);
        var result = new WorkbookOpenResult(workbook, null, "Budget", OpenedAsTemplate: false, LoadWarnings: []);

        var session = new WorkbookSessionFactory().CreateOpened(
            target,
            result,
            viewportHeight: 240,
            viewportWidth: 320,
            adapters: [adapter]);

        session.CurrentFilePath.Should().Be(path);
        session.CurrentFileAccessIdentity.Should().NotBeNull();
        session.CurrentFileAccessIdentity!.LocalPath.Should().Be(path);
        session.CurrentFileAccessIdentity.BookmarkKind.Should().Be("macos-security-scoped-bookmark");
        session.CurrentFileAccessIdentity.BookmarkPayload.Should().Be("open-token");
    }

    [Theory]
    [InlineData(true, 1, 2, 3, "one", "two", "three")]
    [InlineData(false, 3, 2, 1, "three", "two", "one")]
    public void SortSelectedRange_SortsRowsByFirstColumnPreservesSelectionAndUndo(
        bool ascending,
        int first,
        int second,
        int third,
        string firstLabel,
        string secondLabel,
        string thirdLabel)
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, new NumberValue(2));
        sheet.SetCell(b1, new TextValue("two"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("three"));
        sheet.SetCell(a3, new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("one"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var range = new GridRange(a1, new CellAddress(sheet.Id, 3, 2));
        session.SelectRange(range);

        var result = session.SortSelectedRange(ascending);

        result.Success.Should().BeTrue();
        session.CanSortSelectedRange.Should().BeTrue();
        session.SelectedRange.Should().Be(range);
        session.ActiveCell.Should().Be(a1);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        sheet.GetValue(1, 1).Should().Be(new NumberValue(first));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(second));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(third));
        sheet.GetValue(1, 2).Should().Be(new TextValue(firstLabel));
        sheet.GetValue(2, 2).Should().Be(new TextValue(secondLabel));
        sheet.GetValue(3, 2).Should().Be(new TextValue(thirdLabel));

        session.UndoLastEdit().Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(3));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(1, 2).Should().Be(new TextValue("two"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("three"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("one"));
    }

    [Fact]
    public void SortSelectedRange_SingleRowRejectsWithoutDirtyingWorkbook()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(2));
        sheet.SetCell(b1, new TextValue("two"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));

        var result = session.SortSelectedRange(ascending: true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Select at least two rows to sort.");
        session.CanSortSelectedRange.Should().BeFalse();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        sheet.GetValue(1, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(1, 2).Should().Be(new TextValue("two"));
    }

    [Fact]
    public void SortSelectedRange_CustomKeysExcludeHeaderRowAndPreserveSelection()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("Group"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Rank"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(2));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var range = new GridRange(a1, new CellAddress(sheet.Id, 4, 2));
        session.SelectRange(range);

        var result = session.SortSelectedRange(
            [
                new SortKey(0, true),
                new SortKey(1, false)
            ],
            new SortOptions(),
            hasHeaders: true);

        result.Success.Should().BeTrue();
        session.SelectedRange.Should().Be(range);
        session.ActiveCell.Should().Be(a1);
        sheet.GetValue(1, 1).Should().Be(new TextValue("Group"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("Rank"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("A"));
        sheet.GetValue(2, 2).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new TextValue("A"));
        sheet.GetValue(3, 2).Should().Be(new NumberValue(1));
        sheet.GetValue(4, 1).Should().Be(new TextValue("B"));
        sheet.GetValue(4, 2).Should().Be(new NumberValue(2));
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void SortSelectedRange_CustomColorKeyUsesTargetColorAndPreservesHeader()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var red = new CellColor(255, 0, 0);
        var redStyle = workbook.RegisterStyle(new CellStyle { FillColor = red });
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Owner"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Queued"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Ava"));
        var redCell = Cell.FromValue(new TextValue("Escalated"));
        redCell.StyleId = redStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), redCell);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Ben"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Done"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("Cia"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var range = new GridRange(a1, new CellAddress(sheet.Id, 4, 2));
        session.SelectRange(range);

        var result = session.SortSelectedRange(
            [new SortKey(0, true, SortOn.CellColor, red)],
            new SortOptions(CaseSensitive: false, LeftToRight: false),
            hasHeaders: true);

        result.Success.Should().BeTrue();
        session.SelectedRange.Should().Be(range);
        session.ActiveCell.Should().Be(a1);
        sheet.GetValue(1, 1).Should().Be(new TextValue("Status"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("Owner"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Escalated"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Ben"));
        GetStyle(workbook, sheet, new CellAddress(sheet.Id, 2, 1)).FillColor.Should().Be(red);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
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
    public void UndoLastEdit_RevertsCellEditAndRecalculatesDependents()
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
        session.CommitCellText("4");

        var result = session.UndoLastEdit();

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().ContainSingle().Which.Should().Be(a1);
        session.CanUndo.Should().BeFalse();
        session.CanRedo.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        sheet.GetCell(a1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(1);
        sheet.GetCell(b1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(2);
    }

    [Fact]
    public void RedoLastEdit_ReappliesUndoneCellEditAndRecalculatesDependents()
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
        session.CommitCellText("4");
        session.UndoLastEdit();

        var result = session.RedoLastEdit();

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().ContainSingle().Which.Should().Be(a1);
        session.CanUndo.Should().BeTrue();
        session.CanRedo.Should().BeFalse();
        session.IsDirty.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        sheet.GetCell(a1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(4);
        sheet.GetCell(b1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(5);
    }

    [Fact]
    public void UndoRedo_ReturnsFailureWhenHistoryIsUnavailable()
    {
        var session = CreateSession(new StartupWorkbookLoadResult(
            CreateWorkbook(),
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var undo = session.UndoLastEdit();
        var redo = session.RedoLastEdit();

        undo.Success.Should().BeFalse();
        undo.ErrorMessage.Should().Be("Nothing to undo");
        redo.Success.Should().BeFalse();
        redo.ErrorMessage.Should().Be("Nothing to redo");
        session.CanUndo.Should().BeFalse();
        session.CanRedo.Should().BeFalse();
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void CopyActiveCellText_SerializesActiveCellDisplayTextForClipboard()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(b2, new TextValue("North\tWest"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(b2);

        var text = session.CopyActiveCellText();

        text.Should().Be("\"North\tWest\"");
    }

    [Fact]
    public void CopySelectedRangeText_SerializesSelectedRangeAndCapturesInternalClipboard()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var c2 = new CellAddress(sheet.Id, 2, 3);
        var b3 = new CellAddress(sheet.Id, 3, 2);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(b2, new TextValue("North"));
        sheet.SetCell(c2, new TextValue("West"));
        sheet.SetCell(b3, new NumberValue(10));
        sheet.SetCell(c3, new NumberValue(20));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(c3, b2));

        var text = session.CopySelectedRangeText();

        text.Should().Be("North\tWest\r\n10\t20");
        session.SelectedRange.Should().Be(new GridRange(b2, c3));
        session.ActiveCell.Should().Be(b2);
    }

    [Fact]
    public void CopySelectedRangeText_RejectsNonCongruentMultipleSelectedRanges()
    {
        // Excel can copy a multiple selection only when its areas share the same rows or the
        // same columns. Areas that differ in both rows and columns stay rejected. (Congruent
        // same-row/same-column multi-area copy is covered by WorkbookSessionMultiRangeCopyTests.)
        var (session, sheet, a1, _) = CreateSessionWithMultipleSelectedRanges();
        var c3 = new CellAddress(sheet.Id, 3, 3);
        var areaA1 = new GridRange(a1, a1);
        var areaC3 = new GridRange(c3, c3);
        session.SelectRanges(areaA1, new[] { areaA1, areaC3 });

        var result = session.TryCopySelectedRangeText();
        Action copy = () => session.CopySelectedRangeText();

        result.Success.Should().BeFalse();
        result.Text.Should().BeNull();
        result.ErrorMessage.Should().Be("Copy does not support multiple selected ranges yet.");
        copy.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Copy does not support multiple selected ranges yet.");
        session.SelectedRanges.Should().Equal(areaA1, areaC3);
    }

    [Fact]
    public void CutSelectedRangeText_RejectsMultipleSelectedRanges()
    {
        var (session, sheet, a1, c1) = CreateSessionWithMultipleSelectedRanges();

        var result = session.TryCutSelectedRangeText();
        Action cut = () => session.CutSelectedRangeText();

        result.Success.Should().BeFalse();
        result.Text.Should().BeNull();
        result.ErrorMessage.Should().Be("Cut does not support multiple selected ranges yet.");
        cut.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Cut does not support multiple selected ranges yet.");
        sheet.GetValue(a1).Should().Be(new NumberValue(42));
        sheet.GetValue(c1).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void PasteClipboardTextAtActiveCell_UsesInternalClipboardAndRebasesFormulas()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var d3 = new CellAddress(sheet.Id, 3, 4);
        var e3 = new CellAddress(sheet.Id, 3, 5);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "A1+1");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));
        var clipboardText = session.CopySelectedRangeText();
        session.SelectCell(d3);

        var result = session.PasteClipboardTextAtActiveCell(clipboardText);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(d3, e3);
        session.SelectedRange.Should().Be(new GridRange(d3, e3));
        sheet.GetCell(d3)!.Value.Should().Be(new NumberValue(1));
        sheet.GetCell(e3)!.FormulaText.Should().Be("D3+1");
    }

    [Fact]
    public void PasteClipboardTextAtActiveCell_TilesInternalClipboardAcrossLargerSelectedRange()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var d3 = new CellAddress(sheet.Id, 3, 4);
        var f5 = new CellAddress(sheet.Id, 5, 6);
        sheet.SetCell(a1, new TextValue("A"));
        sheet.SetCell(b1, new TextValue("B"));
        sheet.SetCell(a2, new TextValue("C"));
        sheet.SetCell(b2, new TextValue("D"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b2));
        var clipboardText = session.CopySelectedRangeText();
        session.SelectRange(new GridRange(d3, f5));

        var result = session.PasteClipboardTextAtActiveCell(clipboardText);

        result.Success.Should().BeTrue();
        session.SelectedRange.Should().Be(new GridRange(d3, f5));
        sheet.GetValue(d3).Should().Be(new TextValue("A"));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 5)).Should().Be(new TextValue("B"));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 6)).Should().Be(new TextValue("A"));
        sheet.GetValue(new CellAddress(sheet.Id, 4, 4)).Should().Be(new TextValue("C"));
        sheet.GetValue(new CellAddress(sheet.Id, 4, 5)).Should().Be(new TextValue("D"));
        sheet.GetValue(f5).Should().Be(new TextValue("A"));
    }

    [Fact]
    public void PasteClipboardTextAtActiveCell_UsesInternalClipboardWhenPlatformTextCannotBeRead()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new TextValue("North"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);
        session.CopySelectedRangeText();
        session.SelectCell(c1);

        var result = session.PasteClipboardTextAtActiveCell(null);

        result.Success.Should().BeTrue();
        sheet.GetValue(c1).Should().Be(new TextValue("North"));
    }

    [Fact]
    public void PasteClipboardTextAtActiveCell_ClearsCutSourceAfterNonOverlappingInternalPaste()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var d3 = new CellAddress(sheet.Id, 3, 4);
        var e3 = new CellAddress(sheet.Id, 3, 5);
        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetFormula(b1, "A1+1");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));
        var clipboardText = session.CutSelectedRangeText();
        session.SelectCell(d3);

        var paste = session.PasteClipboardTextAtActiveCell(clipboardText);

        paste.Success.Should().BeTrue();
        paste.AffectedCells.Should().Contain([d3, e3, a1, b1]);
        session.SelectedRange.Should().Be(new GridRange(d3, e3));
        sheet.GetCell(a1)!.Value.Should().Be(BlankValue.Instance);
        sheet.GetCell(b1)!.FormulaText.Should().BeNull();
        sheet.GetCell(b1)!.Value.Should().Be(BlankValue.Instance);
        sheet.GetCell(d3)!.Value.Should().Be(new NumberValue(10));
        sheet.GetCell(e3)!.FormulaText.Should().Be("D3+1");

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(10));
        sheet.GetCell(b1)!.FormulaText.Should().Be("A1+1");
        sheet.GetCell(d3).Should().BeNull();
        sheet.GetCell(e3).Should().BeNull();
    }

    [Fact]
    public void PasteClipboardTextAtActiveCell_DoesNotClearCutSourceWhenPasteOverlaps()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new TextValue("left"));
        sheet.SetCell(b1, new TextValue("right"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));
        var clipboardText = session.CutSelectedRangeText();
        session.SelectCell(b1);

        var result = session.PasteClipboardTextAtActiveCell(clipboardText);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(b1, c1);
        sheet.GetValue(a1).Should().Be(new TextValue("left"));
        sheet.GetValue(b1).Should().Be(new TextValue("left"));
        sheet.GetValue(c1).Should().Be(new TextValue("right"));
    }

    [Fact]
    public void PasteClipboardTextAtActiveCell_DoesNotClearCutSourceWhenClipboardTextChanges()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new TextValue("source"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);
        session.CutSelectedRangeText();
        session.SelectCell(c1);

        var result = session.PasteClipboardTextAtActiveCell("external");

        result.Success.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(new TextValue("source"));
        sheet.GetValue(c1).Should().Be(new TextValue("external"));
    }

    [Fact]
    public void PasteClipboardTextAtActiveCell_PreserveTextPastesChangedExternalTextAsLiteralText()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new TextValue("source"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);
        session.CutSelectedRangeText();
        session.SelectCell(c1);

        var result = session.PasteClipboardTextAtActiveCell("00123", preserveText: true);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().ContainSingle().Which.Should().Be(c1);
        sheet.GetValue(a1).Should().Be(new TextValue("source"));
        sheet.GetValue(c1).Should().Be(new TextValue("00123"));
        session.SelectedRange.Should().Be(new GridRange(c1, c1));
    }

    [Fact]
    public void PasteSpecialClipboardAtActiveCell_ValuesModePreservesDestinationStyleAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        var sourceStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(0xFF, 0xFF, 0) });
        var destinationStyle = workbook.RegisterStyle(new CellStyle { FontColor = new CellColor(0xC0, 0, 0) });
        sheet.SetCell(a1, new Cell { Value = new NumberValue(42), StyleId = sourceStyle });
        sheet.SetStyleOnly(c3.Row, c3.Col, destinationStyle);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);
        var clipboardText = session.CopySelectedRangeText();
        session.SelectCell(c3);

        var result = session.PasteSpecialClipboardAtActiveCell(clipboardText, PasteCellsMode.Values, default);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().ContainSingle().Which.Should().Be(c3);
        session.SelectedRange.Should().Be(new GridRange(c3, c3));
        sheet.GetCell(c3)!.Value.Should().Be(new NumberValue(42));
        workbook.GetStyle(sheet.GetCell(c3)!.StyleId).FontColor.Should().Be(new CellColor(0xC0, 0, 0));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.GetCell(c3).Should().BeNull();
        sheet.GetStyleOnly(c3.Row, c3.Col).Should().Be(destinationStyle);
    }

    [Fact]
    public void PasteSpecialClipboardAtActiveCell_ValuesAndNumberFormatsCopiesNumberFormatPreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        var sourceStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            NumberFormat = "0.00%"
        });
        var destinationStyle = workbook.RegisterStyle(new CellStyle
        {
            Italic = true,
            FillColor = new CellColor(0xEE, 0xDD, 0xCC),
            NumberFormat = "General"
        });
        var sourceCell = Cell.FromFormula("B1+1");
        sourceCell.Value = new NumberValue(0.25);
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(a1, sourceCell);
        sheet.SetCell(c3, new Cell { Value = new TextValue("old"), StyleId = destinationStyle });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);
        var clipboardText = session.CopySelectedRangeText();
        session.SelectCell(c3);

        var result = session.PasteSpecialClipboardAtActiveCell(
            clipboardText,
            PasteCellsMode.All,
            new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndNumberFormats));

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().ContainSingle().Which.Should().Be(c3);
        session.SelectedRange.Should().Be(new GridRange(c3, c3));
        var pasted = sheet.GetCell(c3)!;
        pasted.FormulaText.Should().BeNull();
        pasted.Value.Should().Be(new NumberValue(0.25));
        var pastedStyle = workbook.GetStyle(pasted.StyleId);
        pastedStyle.NumberFormat.Should().Be("0.00%");
        pastedStyle.Italic.Should().BeTrue();
        pastedStyle.FillColor.Should().Be(new CellColor(0xEE, 0xDD, 0xCC));
        pastedStyle.Bold.Should().BeFalse();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.GetValue(c3).Should().Be(new TextValue("old"));
        sheet.GetCell(c3)!.StyleId.Should().Be(destinationStyle);
    }

    [Fact]
    public void PasteSpecialClipboardAtActiveCell_RejectsChangedPlatformClipboardText()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new TextValue("source"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);
        session.CopySelectedRangeText();
        session.SelectCell(c1);

        var result = session.PasteSpecialClipboardAtActiveCell("external", PasteCellsMode.Values, default);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Paste Special requires copied FreeX cells.");
        sheet.GetCell(c1).Should().BeNull();
    }

    [Fact]
    public void PasteSpecialClipboardAtActiveCell_ValuesModePastesToMatchingMultipleSelectedRangesPreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        var d3 = new CellAddress(sheet.Id, 3, 4);
        var f5 = new CellAddress(sheet.Id, 5, 6);
        var g5 = new CellAddress(sheet.Id, 5, 7);
        sheet.SetCell(a1, new NumberValue(42));
        sheet.SetCell(b1, new TextValue("West"));
        sheet.SetCell(c3, new TextValue("old c3"));
        sheet.SetCell(d3, new TextValue("old d3"));
        sheet.SetCell(f5, new TextValue("old f5"));
        sheet.SetCell(g5, new TextValue("old g5"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));
        var clipboardText = session.CopySelectedRangeText();
        var firstTarget = new GridRange(c3, d3);
        var secondTarget = new GridRange(f5, g5);
        session.SelectRanges(firstTarget, [firstTarget, secondTarget]);

        var result = session.PasteSpecialClipboardAtActiveCell(clipboardText, PasteCellsMode.Values, default);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(c3, d3, f5, g5);
        session.SelectedRange.Should().Be(firstTarget);
        session.SelectedRanges.Should().Equal(firstTarget, secondTarget);
        session.ActiveCell.Should().Be(c3);
        sheet.GetValue(c3).Should().Be(new NumberValue(42));
        sheet.GetValue(d3).Should().Be(new TextValue("West"));
        sheet.GetValue(f5).Should().Be(new NumberValue(42));
        sheet.GetValue(g5).Should().Be(new TextValue("West"));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.GetValue(c3).Should().Be(new TextValue("old c3"));
        sheet.GetValue(d3).Should().Be(new TextValue("old d3"));
        sheet.GetValue(f5).Should().Be(new TextValue("old f5"));
        sheet.GetValue(g5).Should().Be(new TextValue("old g5"));
    }

    [Fact]
    public void PasteSpecialClipboardAtActiveCell_RejectsMultipleSelectedRangesWithMismatchedPasteSize()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var e1 = new CellAddress(sheet.Id, 1, 5);
        var f1 = new CellAddress(sheet.Id, 1, 6);
        sheet.SetCell(a1, new NumberValue(42));
        sheet.SetCell(b1, new TextValue("West"));
        sheet.SetCell(c1, new TextValue("left"));
        sheet.SetCell(e1, new TextValue("middle"));
        sheet.SetCell(f1, new TextValue("right"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));
        var clipboardText = session.CopySelectedRangeText();
        var shortTarget = new GridRange(c1, c1);
        var matchingTarget = new GridRange(e1, f1);
        session.SelectRanges(shortTarget, [shortTarget, matchingTarget]);

        var result = session.PasteSpecialClipboardAtActiveCell(clipboardText, PasteCellsMode.Values, default);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Paste Special does not support multiple selected ranges yet.");
        result.AffectedCells.Should().BeEmpty();
        session.SelectedRanges.Should().Equal(shortTarget, matchingTarget);
        sheet.GetValue(c1).Should().Be(new TextValue("left"));
        sheet.GetValue(e1).Should().Be(new TextValue("middle"));
        sheet.GetValue(f1).Should().Be(new TextValue("right"));
    }

    [Fact]
    public void PasteSpecialClipboardAtActiveCell_RejectsMultipleSelectedRangesFromCutClipboard()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var f1 = new CellAddress(sheet.Id, 1, 6);
        var g1 = new CellAddress(sheet.Id, 1, 7);
        sheet.SetCell(a1, new NumberValue(42));
        sheet.SetCell(b1, new TextValue("West"));
        sheet.SetCell(c1, new TextValue("old c1"));
        sheet.SetCell(d1, new TextValue("old d1"));
        sheet.SetCell(f1, new TextValue("old f1"));
        sheet.SetCell(g1, new TextValue("old g1"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));
        var clipboardText = session.CutSelectedRangeText();
        var firstTarget = new GridRange(c1, d1);
        var secondTarget = new GridRange(f1, g1);
        session.SelectRanges(firstTarget, [firstTarget, secondTarget]);

        var result = session.PasteSpecialClipboardAtActiveCell(clipboardText, PasteCellsMode.Values, default);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Paste Special does not support multiple selected ranges yet.");
        result.AffectedCells.Should().BeEmpty();
        session.SelectedRanges.Should().Equal(firstTarget, secondTarget);
        sheet.GetValue(a1).Should().Be(new NumberValue(42));
        sheet.GetValue(b1).Should().Be(new TextValue("West"));
        sheet.GetValue(c1).Should().Be(new TextValue("old c1"));
        sheet.GetValue(d1).Should().Be(new TextValue("old d1"));
        sheet.GetValue(f1).Should().Be(new TextValue("old f1"));
        sheet.GetValue(g1).Should().Be(new TextValue("old g1"));
    }

    [Fact]
    public void PasteSpecialClipboardAtActiveCell_FormatsModeDoesNotClearCutSource()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var sourceStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(0x33, 0x99, 0x66) });
        sheet.SetCell(a1, new Cell { Value = new TextValue("source"), StyleId = sourceStyle });
        sheet.SetCell(c1, new TextValue("destination"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);
        var clipboardText = session.CutSelectedRangeText();
        session.SelectCell(c1);

        var result = session.PasteSpecialClipboardAtActiveCell(clipboardText, PasteCellsMode.Formats, default);

        result.Success.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(new TextValue("source"));
        sheet.GetValue(c1).Should().Be(new TextValue("destination"));
        sheet.GetCell(c1)!.StyleId.Should().Be(sourceStyle);
    }

    [Fact]
    public void PasteColumnWidthsFromClipboardAtActiveCell_CopiesWidthsPreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var d3 = new CellAddress(sheet.Id, 3, 4);
        sheet.SetCell(a1, new TextValue("wide"));
        sheet.SetCell(b1, new TextValue("default"));
        sheet.ColumnWidths[1] = 22.5;
        sheet.ColumnWidths[4] = 9;
        sheet.ColumnWidths[5] = 18;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));
        var clipboardText = session.CopySelectedRangeText();
        session.SelectCell(d3);

        var result = session.PasteColumnWidthsFromClipboardAtActiveCell(clipboardText);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().BeEmpty();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(d3);
        session.SelectedRange.Should().Be(new GridRange(d3, d3));
        sheet.ColumnWidths[4].Should().Be(22.5);
        sheet.ColumnWidths.Should().NotContainKey(5);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.ColumnWidths[4].Should().Be(9);
        sheet.ColumnWidths[5].Should().Be(18);
        session.CanRedo.Should().BeTrue();

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        sheet.ColumnWidths[4].Should().Be(22.5);
        sheet.ColumnWidths.Should().NotContainKey(5);
    }

    [Fact]
    public void PasteColumnWidthsFromClipboardAtActiveCell_RejectsChangedPlatformClipboardText()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        sheet.SetCell(a1, new TextValue("source"));
        sheet.ColumnWidths[1] = 22;
        sheet.ColumnWidths[4] = 9;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);
        session.CopySelectedRangeText();
        session.SelectCell(d1);

        var result = session.PasteColumnWidthsFromClipboardAtActiveCell("external");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Paste Column Widths requires copied FreeX cells.");
        sheet.ColumnWidths[4].Should().Be(9);
    }

    [Fact]
    public void PasteSpecialClipboardAtActiveCell_KeepSourceColumnWidthsPastesValuesAndWidthsWithoutClearingCutSource()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var e1 = new CellAddress(sheet.Id, 1, 5);
        sheet.SetCell(a1, new TextValue("source"));
        sheet.SetCell(b1, new NumberValue(7));
        sheet.SetCell(d1, new TextValue("old"));
        sheet.SetCell(e1, new TextValue("old2"));
        sheet.ColumnWidths[1] = 24;
        sheet.ColumnWidths[4] = 9;
        sheet.ColumnWidths[5] = 18;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));
        var clipboardText = session.CutSelectedRangeText();
        session.SelectCell(d1);

        var result = session.PasteSpecialClipboardAtActiveCell(
            clipboardText,
            PasteCellsMode.All,
            default,
            keepSourceColumnWidths: true);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Contain([d1, e1]);
        session.SelectedRange.Should().Be(new GridRange(d1, e1));
        sheet.GetValue(a1).Should().Be(new TextValue("source"));
        sheet.GetValue(b1).Should().Be(new NumberValue(7));
        sheet.GetValue(d1).Should().Be(new TextValue("source"));
        sheet.GetValue(e1).Should().Be(new NumberValue(7));
        sheet.ColumnWidths[4].Should().Be(24);
        sheet.ColumnWidths.Should().NotContainKey(5);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(new TextValue("source"));
        sheet.GetValue(b1).Should().Be(new NumberValue(7));
        sheet.GetValue(d1).Should().Be(new TextValue("old"));
        sheet.GetValue(e1).Should().Be(new TextValue("old2"));
        sheet.ColumnWidths[4].Should().Be(9);
        sheet.ColumnWidths[5].Should().Be(18);
    }

    [Fact]
    public void PasteClipboardTextAtActiveCell_PropagatesInternalClipboardAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryB1 = new CellAddress(summary.Id, 1, 2);
        var summaryD3 = new CellAddress(summary.Id, 3, 4);
        var summaryE3 = new CellAddress(summary.Id, 3, 5);
        var detailsD3 = new CellAddress(details.Id, 3, 4);
        var detailsE3 = new CellAddress(details.Id, 3, 5);
        var hiddenD3 = new CellAddress(hidden.Id, 3, 4);
        summary.SetCell(summaryA1, new NumberValue(1));
        summary.SetFormula(summaryB1, "A1+1");
        details.SetCell(detailsD3, new TextValue("old"));
        hidden.SetCell(hiddenD3, new TextValue("hidden"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(summaryA1, summaryB1));
        var clipboardText = session.CopySelectedRangeText();
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryD3);

        var result = session.PasteClipboardTextAtActiveCell(clipboardText);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(summaryD3, summaryE3, detailsD3, detailsE3);
        result.AffectedCells.Should().NotContain(hiddenD3);
        session.SelectedRange.Should().Be(new GridRange(summaryD3, summaryE3));
        session.IsWorkbookGrouped.Should().BeTrue();
        summary.GetValue(summaryD3).Should().Be(new NumberValue(1));
        summary.GetCell(summaryE3)!.FormulaText.Should().Be("D3+1");
        details.GetValue(detailsD3).Should().Be(new NumberValue(1));
        details.GetCell(detailsE3)!.FormulaText.Should().Be("D3+1");
        hidden.GetValue(hiddenD3).Should().Be(new TextValue("hidden"));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.GetCell(summaryD3).Should().BeNull();
        summary.GetCell(summaryE3).Should().BeNull();
        details.GetValue(detailsD3).Should().Be(new TextValue("old"));
        details.GetCell(detailsE3).Should().BeNull();
        hidden.GetValue(hiddenD3).Should().Be(new TextValue("hidden"));
        session.IsWorkbookGrouped.Should().BeTrue();
    }

    [Fact]
    public void PasteClipboardTextAtActiveCell_RejectsProtectedGroupedTargetAndRollsBackActiveSheet()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryC1 = new CellAddress(summary.Id, 1, 3);
        var detailsC1 = new CellAddress(details.Id, 1, 3);
        summary.SetCell(summaryA1, new TextValue("source"));
        summary.SetCell(summaryC1, new TextValue("active old"));
        details.SetCell(detailsC1, new TextValue("locked"));
        details.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(summaryA1);
        var clipboardText = session.CopySelectedRangeText();
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryC1);

        var result = session.PasteClipboardTextAtActiveCell(clipboardText);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        summary.GetValue(summaryC1).Should().Be(new TextValue("active old"));
        details.GetValue(detailsC1).Should().Be(new TextValue("locked"));
        session.ActiveSheet.Should().BeSameAs(summary);
        session.ActiveCell.Should().Be(summaryC1);
        session.IsWorkbookGrouped.Should().BeTrue();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void PasteExternalTextAtActiveCell_PropagatesAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var summaryC2 = new CellAddress(summary.Id, 2, 3);
        var summaryD2 = new CellAddress(summary.Id, 2, 4);
        var detailsC2 = new CellAddress(details.Id, 2, 3);
        var detailsD2 = new CellAddress(details.Id, 2, 4);
        var hiddenC2 = new CellAddress(hidden.Id, 2, 3);
        details.SetCell(detailsC2, new TextValue("old"));
        hidden.SetCell(hiddenC2, new TextValue("hidden"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryC2);

        var result = session.PasteExternalTextAtActiveCell("7\tNorth");

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(summaryC2, summaryD2, detailsC2, detailsD2);
        result.AffectedCells.Should().NotContain(hiddenC2);
        session.SelectedRange.Should().Be(new GridRange(summaryC2, summaryD2));
        session.IsWorkbookGrouped.Should().BeTrue();
        summary.GetValue(summaryC2).Should().Be(new NumberValue(7));
        summary.GetValue(summaryD2).Should().Be(new TextValue("North"));
        details.GetValue(detailsC2).Should().Be(new NumberValue(7));
        details.GetValue(detailsD2).Should().Be(new TextValue("North"));
        hidden.GetValue(hiddenC2).Should().Be(new TextValue("hidden"));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.GetCell(summaryC2).Should().BeNull();
        summary.GetCell(summaryD2).Should().BeNull();
        details.GetValue(detailsC2).Should().Be(new TextValue("old"));
        details.GetCell(detailsD2).Should().BeNull();
        hidden.GetValue(hiddenC2).Should().Be(new TextValue("hidden"));
    }

    [Fact]
    public void PasteSpecialClipboardAtActiveCell_ValuesModePropagatesAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryC3 = new CellAddress(summary.Id, 3, 3);
        var detailsC3 = new CellAddress(details.Id, 3, 3);
        var sourceStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(0xFF, 0xFF, 0) });
        var summaryStyle = workbook.RegisterStyle(new CellStyle { FontColor = new CellColor(0xC0, 0, 0) });
        var detailsStyle = workbook.RegisterStyle(new CellStyle { FontColor = new CellColor(0, 0, 0xC0) });
        summary.SetCell(summaryA1, new Cell { Value = new NumberValue(42), StyleId = sourceStyle });
        summary.SetStyleOnly(summaryC3.Row, summaryC3.Col, summaryStyle);
        details.SetCell(detailsC3, new Cell { Value = new TextValue("old"), StyleId = detailsStyle });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(summaryA1);
        var clipboardText = session.CopySelectedRangeText();
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryC3);

        var result = session.PasteSpecialClipboardAtActiveCell(clipboardText, PasteCellsMode.Values, default);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(summaryC3, detailsC3);
        session.SelectedRange.Should().Be(new GridRange(summaryC3, summaryC3));
        session.IsWorkbookGrouped.Should().BeTrue();
        summary.GetValue(summaryC3).Should().Be(new NumberValue(42));
        details.GetValue(detailsC3).Should().Be(new NumberValue(42));
        GetStyle(workbook, summary, summaryC3).FontColor.Should().Be(new CellColor(0xC0, 0, 0));
        GetStyle(workbook, details, detailsC3).FontColor.Should().Be(new CellColor(0, 0, 0xC0));
        GetStyle(workbook, summary, summaryC3).FillColor.Should().BeNull();
        GetStyle(workbook, details, detailsC3).FillColor.Should().BeNull();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.GetCell(summaryC3).Should().BeNull();
        summary.GetStyleOnly(summaryC3.Row, summaryC3.Col).Should().Be(summaryStyle);
        details.GetValue(detailsC3).Should().Be(new TextValue("old"));
        details.GetCell(detailsC3)!.StyleId.Should().Be(detailsStyle);
    }

    [Fact]
    public void PasteCommentsFromClipboardAtActiveCell_CopiesNotesAndThreadedCommentsPreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var d3 = new CellAddress(sheet.Id, 3, 4);
        var e3 = new CellAddress(sheet.Id, 3, 5);
        sheet.SetCell(a1, new TextValue("note source"));
        sheet.SetCell(b1, new TextValue("thread source"));
        sheet.Comments[a1] = "plain note";
        sheet.ThreadedComments[b1] = new ThreadedComment("thread note", "Quinn")
        {
            Replies = [new CommentReply("reply", "Codex")],
            IsResolved = true
        };
        sheet.Comments[d3] = "old note";
        sheet.ThreadedComments[e3] = new ThreadedComment("old thread", "FreeX");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));
        var clipboardText = session.CutSelectedRangeText();
        session.SelectCell(d3);

        var result = session.PasteCommentsFromClipboardAtActiveCell(clipboardText);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(d3, e3);
        session.SelectedRange.Should().Be(new GridRange(d3, e3));
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(new TextValue("note source"));
        sheet.GetValue(b1).Should().Be(new TextValue("thread source"));
        sheet.Comments[d3].Should().Be("plain note");
        sheet.ThreadedComments[e3].Text.Should().Be("thread note");
        sheet.ThreadedComments[e3].Replies.Should().Equal(new CommentReply("reply", "Codex"));
        sheet.ThreadedComments[e3].IsResolved.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.Comments[d3].Should().Be("old note");
        sheet.ThreadedComments[e3].Text.Should().Be("old thread");
    }

    [Fact]
    public void PasteDataValidationFromClipboardAtActiveCell_TransposesRebasesRulesPreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var d3 = new CellAddress(sheet.Id, 3, 4);
        sheet.SetCell(a1, new TextValue("first"));
        sheet.SetCell(b1, new TextValue("second"));
        var sourceRange = new GridRange(a1, b1);
        var oldDestinationRule = new DataValidation
        {
            AppliesTo = new GridRange(d3, d3),
            Type = DvType.WholeNumber,
            Formula1 = "1",
            Formula2 = "9"
        };
        sheet.DataValidations.Add(oldDestinationRule);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = sourceRange,
            Type = DvType.Custom,
            Formula1 = "=C1>0",
            ErrorTitle = "Source rule"
        });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(sourceRange);
        var clipboardText = session.CopySelectedRangeText();
        session.SelectCell(d3);

        var result = session.PasteDataValidationFromClipboardAtActiveCell(clipboardText, transpose: true);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().BeEmpty();
        session.SelectedRange.Should().Be(new GridRange(d3, new CellAddress(sheet.Id, 4, 4)));
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        sheet.DataValidations.Should().NotContain(rule => ReferenceEquals(rule, oldDestinationRule));
        sheet.DataValidations.Should().Contain(rule =>
            rule.AppliesTo == sourceRange &&
            rule.Formula1 == "=C1>0");
        sheet.DataValidations.Should().ContainSingle(rule =>
            rule.AppliesTo == new GridRange(d3, new CellAddress(sheet.Id, 4, 4)) &&
            rule.Formula1 == "=F3>0" &&
            rule.ErrorTitle == "Source rule");

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.DataValidations.Should().ContainSingle(rule =>
            rule.AppliesTo == oldDestinationRule.AppliesTo &&
            rule.Type == DvType.WholeNumber &&
            rule.Formula1 == "1" &&
            rule.Formula2 == "9");
        sheet.DataValidations.Should().ContainSingle(rule => rule.AppliesTo == sourceRange && rule.Formula1 == "=C1>0");
    }

    [Fact]
    public void PasteCommentsAndValidationFromClipboardAtActiveCell_RejectChangedPlatformClipboardText()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        sheet.SetCell(a1, new TextValue("source"));
        sheet.Comments[a1] = "note";
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(a1, a1),
            Type = DvType.List,
            Formula1 = "Yes,No"
        });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);
        session.CopySelectedRangeText();
        session.SelectCell(d1);

        var commentsResult = session.PasteCommentsFromClipboardAtActiveCell("external");

        commentsResult.Success.Should().BeFalse();
        commentsResult.ErrorMessage.Should().Be("Paste Comments requires copied FreeX cells.");
        sheet.Comments.Should().NotContainKey(d1);

        session.SelectCell(a1);
        session.CopySelectedRangeText();
        session.SelectCell(d1);

        var validationResult = session.PasteDataValidationFromClipboardAtActiveCell("external");

        validationResult.Success.Should().BeFalse();
        validationResult.ErrorMessage.Should().Be("Paste Validation requires copied FreeX cells.");
        sheet.DataValidations.Should().NotContain(rule => rule.AppliesTo.Contains(d1));
    }

    [Fact]
    public void PasteLinkFromClipboardAtActiveCell_CreatesFormulasPreservesDestinationStyleAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var d3 = new CellAddress(sheet.Id, 3, 4);
        var e3 = new CellAddress(sheet.Id, 3, 5);
        var destinationStyle = workbook.RegisterStyle(new CellStyle { FontColor = new CellColor(0x21, 0x43, 0x65) });
        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetCell(b1, new NumberValue(12));
        sheet.SetCell(d3, new Cell { Value = new TextValue("old"), StyleId = destinationStyle });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));
        var clipboardText = session.CopySelectedRangeText();
        session.SelectCell(d3);

        var result = session.PasteLinkFromClipboardAtActiveCell(clipboardText);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(d3, e3);
        session.SelectedRange.Should().Be(new GridRange(d3, e3));
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        sheet.GetCell(d3)!.FormulaText.Should().Be("Sheet1!A1");
        sheet.GetCell(e3)!.FormulaText.Should().Be("Sheet1!B1");
        workbook.GetStyle(sheet.GetCell(d3)!.StyleId).FontColor.Should().Be(new CellColor(0x21, 0x43, 0x65));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.GetValue(d3).Should().Be(new TextValue("old"));
        workbook.GetStyle(sheet.GetCell(d3)!.StyleId).FontColor.Should().Be(new CellColor(0x21, 0x43, 0x65));
        sheet.GetCell(e3).Should().BeNull();
    }

    [Fact]
    public void PasteLinkFromClipboardAtActiveCell_RejectsChangedPlatformClipboardText()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        sheet.SetCell(a1, new TextValue("source"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);
        session.CopySelectedRangeText();
        session.SelectCell(d1);

        var result = session.PasteLinkFromClipboardAtActiveCell("external");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Paste Link requires copied FreeX cells.");
        sheet.GetCell(d1).Should().BeNull();
    }

    [Fact]
    public void PasteLinkFromClipboardAtActiveCell_TransposesAndCanKeepSourceColumnWidthsWithoutClearingCutSource()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var d3 = new CellAddress(sheet.Id, 3, 4);
        var d4 = new CellAddress(sheet.Id, 4, 4);
        sheet.SetCell(a1, new TextValue("first"));
        sheet.SetCell(b1, new TextValue("second"));
        sheet.ColumnWidths[1] = 22;
        sheet.ColumnWidths[4] = 9;
        sheet.ColumnWidths[5] = 18;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));
        var clipboardText = session.CutSelectedRangeText();
        session.SelectCell(d3);

        var result = session.PasteLinkFromClipboardAtActiveCell(
            clipboardText,
            transpose: true,
            keepSourceColumnWidths: true);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(d3, d4);
        session.SelectedRange.Should().Be(new GridRange(d3, d4));
        sheet.GetValue(a1).Should().Be(new TextValue("first"));
        sheet.GetValue(b1).Should().Be(new TextValue("second"));
        sheet.GetCell(d3)!.FormulaText.Should().Be("Sheet1!A1");
        sheet.GetCell(d4)!.FormulaText.Should().Be("Sheet1!B1");
        sheet.ColumnWidths[4].Should().Be(22);
        sheet.ColumnWidths.Should().NotContainKey(5);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(new TextValue("first"));
        sheet.GetValue(b1).Should().Be(new TextValue("second"));
        sheet.GetCell(d3).Should().BeNull();
        sheet.GetCell(d4).Should().BeNull();
        sheet.ColumnWidths[4].Should().Be(9);
        sheet.ColumnWidths[5].Should().Be(18);
    }

    [Fact]
    public void PastePictureFromClipboardAtActiveCell_AddsCellRangeSnapshotPreservesSourceAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var d4 = new CellAddress(sheet.Id, 4, 4);
        sheet.SetCell(a1, new TextValue("Q1"));
        sheet.SetCell(b1, new NumberValue(10));
        sheet.SetCell(a2, new BoolValue(true));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b2));
        var clipboardText = session.CopySelectedRangeText();
        session.SelectCell(d4);

        var result = session.PastePictureFromClipboardAtActiveCell(clipboardText);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().ContainSingle().Which.Should().Be(d4);
        session.SelectedRange.Should().Be(new GridRange(d4, d4));
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(new TextValue("Q1"));
        sheet.GetValue(b1).Should().Be(new NumberValue(10));
        sheet.GetValue(a2).Should().Be(new BoolValue(true));
        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        picture.Anchor.Should().Be(d4);
        picture.Kind.Should().Be(PictureKind.CellRangeSnapshot);
        picture.IsLinkedToSourceRange.Should().BeFalse();
        picture.SourceRowCount.Should().Be(2);
        picture.SourceColumnCount.Should().Be(2);
        picture.Cells.Should().Contain(cell => cell.RowOffset == 0 && cell.ColumnOffset == 0 && cell.Text == "Q1");
        picture.Cells.Should().Contain(cell => cell.RowOffset == 0 && cell.ColumnOffset == 1 && cell.Text == "10");
        picture.Cells.Should().Contain(cell => cell.RowOffset == 1 && cell.ColumnOffset == 0 && cell.Text == "TRUE");
        picture.Cells.Should().Contain(cell => cell.RowOffset == 1 && cell.ColumnOffset == 1 && cell.Text == "");

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.Pictures.Should().BeEmpty();
    }

    [Fact]
    public void PastePictureFromClipboardAtActiveCell_LinkedPictureRecordsSourceRangeAndSheetName()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var e5 = new CellAddress(sheet.Id, 5, 5);
        sheet.SetCell(a1, new TextValue("source"));
        sheet.SetCell(b2, new NumberValue(42));
        var sourceRange = new GridRange(a1, b2);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(sourceRange);
        var clipboardText = session.CopySelectedRangeText();
        session.SelectCell(e5);

        var result = session.PastePictureFromClipboardAtActiveCell(clipboardText, linkedPicture: true);

        result.Success.Should().BeTrue();
        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        picture.Anchor.Should().Be(e5);
        picture.IsLinkedToSourceRange.Should().BeTrue();
        picture.LinkedSourceRange.Should().Be(sourceRange);
        picture.LinkedSourceSheetName.Should().Be(sheet.Name);
        picture.Cells.Should().Contain(cell => cell.RowOffset == 0 && cell.ColumnOffset == 0 && cell.Text == "source");
        picture.Cells.Should().Contain(cell => cell.RowOffset == 1 && cell.ColumnOffset == 1 && cell.Text == "42");
    }

    [Fact]
    public void PastePictureFromClipboardAtActiveCell_RejectsChangedPlatformClipboardText()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        sheet.SetCell(a1, new TextValue("source"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);
        session.CopySelectedRangeText();
        session.SelectCell(d1);

        var pictureResult = session.PastePictureFromClipboardAtActiveCell("external");

        pictureResult.Success.Should().BeFalse();
        pictureResult.ErrorMessage.Should().Be("Paste Picture requires copied FreeX cells.");
        sheet.Pictures.Should().BeEmpty();

        session.SelectCell(a1);
        session.CopySelectedRangeText();
        session.SelectCell(d1);

        var linkedPictureResult = session.PastePictureFromClipboardAtActiveCell("external", linkedPicture: true);

        linkedPictureResult.Success.Should().BeFalse();
        linkedPictureResult.ErrorMessage.Should().Be("Paste Linked Picture requires copied FreeX cells.");
        sheet.Pictures.Should().BeEmpty();
    }

    [Fact]
    public void ShouldPreferExternalClipboardImage_UsesImageOnlyWhenNoTextOrInternalCopyWins()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("source"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        session.ShouldPreferExternalClipboardImage(null).Should().BeTrue();
        session.ShouldPreferExternalClipboardImage("").Should().BeTrue();
        session.ShouldPreferExternalClipboardImage("text").Should().BeFalse();

        session.SelectCell(a1);
        var clipboardText = session.CopySelectedRangeText();

        session.ShouldPreferExternalClipboardImage(null).Should().BeFalse();
        session.ShouldPreferExternalClipboardImage(clipboardText).Should().BeFalse();
        session.ShouldPreferExternalClipboardImage("").Should().BeTrue();
    }

    [Fact]
    public void PasteClipboardImageAtActiveCell_AddsBinaryImagePicturePreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var d4 = new CellAddress(sheet.Id, 4, 4);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(d4);
        var pngBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };

        var result = session.PasteClipboardImageAtActiveCell(pngBytes, pixelWidth: 96, pixelHeight: 72);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().ContainSingle().Which.Should().Be(d4);
        session.ActiveCell.Should().Be(d4);
        session.SelectedRange.Should().Be(new GridRange(d4, d4));
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        picture.Anchor.Should().Be(d4);
        picture.Kind.Should().Be(PictureKind.Image);
        picture.ContentType.Should().Be("image/png");
        picture.ImageBytes.Should().Equal(pngBytes);
        picture.Width.Should().Be(96);
        picture.Height.Should().Be(72);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.Pictures.Should().BeEmpty();
    }

    [Fact]
    public void PasteColumnWidthsFromClipboardAtActiveCell_PropagatesAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryB1 = new CellAddress(summary.Id, 1, 2);
        var summaryD1 = new CellAddress(summary.Id, 1, 4);
        summary.SetCell(summaryA1, new TextValue("wide"));
        summary.SetCell(summaryB1, new TextValue("default"));
        summary.ColumnWidths[1] = 22.5;
        summary.ColumnWidths[4] = 8;
        summary.ColumnWidths[5] = 16;
        details.ColumnWidths[4] = 9;
        details.ColumnWidths[5] = 18;
        hidden.ColumnWidths[4] = 7;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(summaryA1, summaryB1));
        var clipboardText = session.CopySelectedRangeText();
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryD1);

        var result = session.PasteColumnWidthsFromClipboardAtActiveCell(clipboardText);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().BeEmpty();
        session.ActiveCell.Should().Be(summaryD1);
        session.SelectedRange.Should().Be(new GridRange(summaryD1, summaryD1));
        session.IsWorkbookGrouped.Should().BeTrue();
        summary.ColumnWidths[4].Should().Be(22.5);
        summary.ColumnWidths.Should().NotContainKey(5);
        details.ColumnWidths[4].Should().Be(22.5);
        details.ColumnWidths.Should().NotContainKey(5);
        hidden.ColumnWidths[4].Should().Be(7);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.ColumnWidths[4].Should().Be(8);
        summary.ColumnWidths[5].Should().Be(16);
        details.ColumnWidths[4].Should().Be(9);
        details.ColumnWidths[5].Should().Be(18);
        hidden.ColumnWidths[4].Should().Be(7);
    }

    [Fact]
    public void PasteCommentsFromClipboardAtActiveCell_PropagatesAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryB1 = new CellAddress(summary.Id, 1, 2);
        var summaryD3 = new CellAddress(summary.Id, 3, 4);
        var summaryE3 = new CellAddress(summary.Id, 3, 5);
        var detailsD3 = new CellAddress(details.Id, 3, 4);
        var detailsE3 = new CellAddress(details.Id, 3, 5);
        var hiddenD3 = new CellAddress(hidden.Id, 3, 4);
        summary.SetCell(summaryA1, new TextValue("note source"));
        summary.SetCell(summaryB1, new TextValue("thread source"));
        summary.Comments[summaryA1] = "plain note";
        summary.ThreadedComments[summaryB1] = new ThreadedComment("thread note", "Quinn")
        {
            Replies = [new CommentReply("reply", "Codex")],
            IsResolved = true
        };
        details.Comments[detailsD3] = "old note";
        details.ThreadedComments[detailsE3] = new ThreadedComment("old thread", "FreeX");
        hidden.Comments[hiddenD3] = "hidden note";
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(summaryA1, summaryB1));
        var clipboardText = session.CopySelectedRangeText();
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryD3);

        var result = session.PasteCommentsFromClipboardAtActiveCell(clipboardText);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(summaryD3, summaryE3, detailsD3, detailsE3);
        session.SelectedRange.Should().Be(new GridRange(summaryD3, summaryE3));
        session.IsWorkbookGrouped.Should().BeTrue();
        summary.Comments[summaryD3].Should().Be("plain note");
        summary.ThreadedComments[summaryE3].Text.Should().Be("thread note");
        summary.ThreadedComments[summaryE3].Replies.Should().Equal(new CommentReply("reply", "Codex"));
        details.Comments[detailsD3].Should().Be("plain note");
        details.ThreadedComments[detailsE3].Text.Should().Be("thread note");
        hidden.Comments[hiddenD3].Should().Be("hidden note");

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.Comments.Should().NotContainKey(summaryD3);
        summary.ThreadedComments.Should().NotContainKey(summaryE3);
        details.Comments[detailsD3].Should().Be("old note");
        details.ThreadedComments[detailsE3].Text.Should().Be("old thread");
        hidden.Comments[hiddenD3].Should().Be("hidden note");
    }

    [Fact]
    public void PasteCommentsFromClipboardAtActiveCell_RejectsProtectedGroupedTargetAndRollsBackActiveSheet()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryD3 = new CellAddress(summary.Id, 3, 4);
        var detailsD3 = new CellAddress(details.Id, 3, 4);
        summary.SetCell(summaryA1, new TextValue("note source"));
        summary.Comments[summaryA1] = "plain note";
        summary.Comments[summaryD3] = "active old";
        details.Comments[detailsD3] = "locked";
        details.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(summaryA1);
        var clipboardText = session.CopySelectedRangeText();
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryD3);

        var result = session.PasteCommentsFromClipboardAtActiveCell(clipboardText);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        summary.Comments[summaryD3].Should().Be("active old");
        details.Comments[detailsD3].Should().Be("locked");
        session.ActiveSheet.Should().BeSameAs(summary);
        session.ActiveCell.Should().Be(summaryD3);
        session.IsWorkbookGrouped.Should().BeTrue();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void PasteDataValidationFromClipboardAtActiveCell_PropagatesAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryB1 = new CellAddress(summary.Id, 1, 2);
        var summaryD3 = new CellAddress(summary.Id, 3, 4);
        var detailsD3 = new CellAddress(details.Id, 3, 4);
        var hiddenD3 = new CellAddress(hidden.Id, 3, 4);
        var sourceRange = new GridRange(summaryA1, summaryB1);
        var oldDetailsRule = new DataValidation
        {
            AppliesTo = new GridRange(detailsD3, detailsD3),
            Type = DvType.WholeNumber,
            Formula1 = "1",
            Formula2 = "9"
        };
        var hiddenRule = new DataValidation
        {
            AppliesTo = new GridRange(hiddenD3, hiddenD3),
            Type = DvType.TextLength,
            Formula1 = "2"
        };
        summary.DataValidations.Add(new DataValidation
        {
            AppliesTo = sourceRange,
            Type = DvType.Custom,
            Formula1 = "=C1>0",
            ErrorTitle = "Source rule"
        });
        details.DataValidations.Add(oldDetailsRule);
        hidden.DataValidations.Add(hiddenRule);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(sourceRange);
        var clipboardText = session.CopySelectedRangeText();
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryD3);

        var result = session.PasteDataValidationFromClipboardAtActiveCell(clipboardText, transpose: true);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().BeEmpty();
        session.SelectedRange.Should().Be(new GridRange(summaryD3, new CellAddress(summary.Id, 4, 4)));
        session.IsWorkbookGrouped.Should().BeTrue();
        summary.DataValidations.Should().ContainSingle(rule =>
            rule.AppliesTo == new GridRange(summaryD3, new CellAddress(summary.Id, 4, 4)) &&
            rule.Formula1 == "=F3>0" &&
            rule.ErrorTitle == "Source rule");
        details.DataValidations.Should().ContainSingle(rule =>
            rule.AppliesTo == new GridRange(detailsD3, new CellAddress(details.Id, 4, 4)) &&
            rule.Formula1 == "=F3>0" &&
            rule.ErrorTitle == "Source rule");
        hidden.DataValidations.Should().ContainSingle(rule =>
            rule.AppliesTo == hiddenRule.AppliesTo &&
            rule.Type == DvType.TextLength &&
            rule.Formula1 == "2");

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.DataValidations.Should().ContainSingle(rule => rule.AppliesTo == sourceRange && rule.Formula1 == "=C1>0");
        details.DataValidations.Should().ContainSingle(rule =>
            rule.AppliesTo == oldDetailsRule.AppliesTo &&
            rule.Type == DvType.WholeNumber &&
            rule.Formula1 == "1" &&
            rule.Formula2 == "9");
        hidden.DataValidations.Should().ContainSingle(rule =>
            rule.AppliesTo == hiddenRule.AppliesTo &&
            rule.Type == DvType.TextLength &&
            rule.Formula1 == "2");
    }

    [Fact]
    public void PasteLinkFromClipboardAtActiveCell_PropagatesAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryB1 = new CellAddress(summary.Id, 1, 2);
        var summaryD3 = new CellAddress(summary.Id, 3, 4);
        var summaryE3 = new CellAddress(summary.Id, 3, 5);
        var detailsD3 = new CellAddress(details.Id, 3, 4);
        var detailsE3 = new CellAddress(details.Id, 3, 5);
        var hiddenD3 = new CellAddress(hidden.Id, 3, 4);
        var detailsStyle = workbook.RegisterStyle(new CellStyle { FontColor = new CellColor(0, 0, 0xC0) });
        summary.SetCell(summaryA1, new NumberValue(10));
        summary.SetCell(summaryB1, new NumberValue(12));
        details.SetCell(detailsD3, new Cell { Value = new TextValue("old"), StyleId = detailsStyle });
        hidden.SetCell(hiddenD3, new TextValue("hidden"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(summaryA1, summaryB1));
        var clipboardText = session.CopySelectedRangeText();
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryD3);

        var result = session.PasteLinkFromClipboardAtActiveCell(clipboardText);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(summaryD3, summaryE3, detailsD3, detailsE3);
        session.SelectedRange.Should().Be(new GridRange(summaryD3, summaryE3));
        session.IsWorkbookGrouped.Should().BeTrue();
        summary.GetCell(summaryD3)!.FormulaText.Should().Be("Sheet1!A1");
        summary.GetCell(summaryE3)!.FormulaText.Should().Be("Sheet1!B1");
        details.GetCell(detailsD3)!.FormulaText.Should().Be("Sheet1!A1");
        details.GetCell(detailsE3)!.FormulaText.Should().Be("Sheet1!B1");
        details.GetCell(detailsD3)!.StyleId.Should().Be(detailsStyle);
        hidden.GetValue(hiddenD3).Should().Be(new TextValue("hidden"));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.GetCell(summaryD3).Should().BeNull();
        summary.GetCell(summaryE3).Should().BeNull();
        details.GetValue(detailsD3).Should().Be(new TextValue("old"));
        details.GetCell(detailsD3)!.StyleId.Should().Be(detailsStyle);
        details.GetCell(detailsE3).Should().BeNull();
        hidden.GetValue(hiddenD3).Should().Be(new TextValue("hidden"));
    }

    [Fact]
    public void PastePictureFromClipboardAtActiveCell_PropagatesLinkedPicturesAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryB2 = new CellAddress(summary.Id, 2, 2);
        var summaryE5 = new CellAddress(summary.Id, 5, 5);
        var detailsE5 = new CellAddress(details.Id, 5, 5);
        var sourceRange = new GridRange(summaryA1, summaryB2);
        summary.SetCell(summaryA1, new TextValue("source"));
        summary.SetCell(summaryB2, new NumberValue(42));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(sourceRange);
        var clipboardText = session.CopySelectedRangeText();
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryE5);

        var result = session.PastePictureFromClipboardAtActiveCell(clipboardText, linkedPicture: true);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(summaryE5, detailsE5);
        session.ActiveCell.Should().Be(summaryE5);
        session.SelectedRange.Should().Be(new GridRange(summaryE5, summaryE5));
        session.IsWorkbookGrouped.Should().BeTrue();
        var summaryPicture = summary.Pictures.Should().ContainSingle().Subject;
        var detailsPicture = details.Pictures.Should().ContainSingle().Subject;
        summaryPicture.Anchor.Should().Be(summaryE5);
        detailsPicture.Anchor.Should().Be(detailsE5);
        summaryPicture.IsLinkedToSourceRange.Should().BeTrue();
        detailsPicture.IsLinkedToSourceRange.Should().BeTrue();
        summaryPicture.LinkedSourceRange.Should().Be(sourceRange);
        detailsPicture.LinkedSourceRange.Should().Be(sourceRange);
        detailsPicture.LinkedSourceSheetName.Should().Be(summary.Name);
        hidden.Pictures.Should().BeEmpty();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.Pictures.Should().BeEmpty();
        details.Pictures.Should().BeEmpty();
        hidden.Pictures.Should().BeEmpty();
    }

    [Fact]
    public void PasteClipboardImageAtActiveCell_PropagatesAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var summaryD4 = new CellAddress(summary.Id, 4, 4);
        var detailsD4 = new CellAddress(details.Id, 4, 4);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryD4);
        var pngBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };

        var result = session.PasteClipboardImageAtActiveCell(pngBytes, pixelWidth: 96, pixelHeight: 72);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(summaryD4, detailsD4);
        session.ActiveCell.Should().Be(summaryD4);
        session.SelectedRange.Should().Be(new GridRange(summaryD4, summaryD4));
        session.IsWorkbookGrouped.Should().BeTrue();
        summary.Pictures.Should().ContainSingle(picture =>
            picture.Anchor == summaryD4 &&
            picture.Kind == PictureKind.Image &&
            picture.ImageBytes.SequenceEqual(pngBytes));
        details.Pictures.Should().ContainSingle(picture =>
            picture.Anchor == detailsD4 &&
            picture.Kind == PictureKind.Image &&
            picture.ImageBytes.SequenceEqual(pngBytes));
        hidden.Pictures.Should().BeEmpty();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.Pictures.Should().BeEmpty();
        details.Pictures.Should().BeEmpty();
        hidden.Pictures.Should().BeEmpty();
    }

    [Fact]
    public void ClearSelectedRangeContents_ClearsValuesAndFormulasPreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetFormula(b1, "A1+1");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));

        var result = session.ClearSelectedRangeContents();

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(a1, b1);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b1));
        sheet.GetCell(a1)!.Value.Should().Be(BlankValue.Instance);
        sheet.GetCell(b1)!.FormulaText.Should().BeNull();
        sheet.GetCell(b1)!.Value.Should().Be(BlankValue.Instance);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        session.CanRedo.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(10));
        sheet.GetCell(b1)!.FormulaText.Should().Be("A1+1");
    }

    [Fact]
    public void ClearSelectedRangeFormats_ClearsStylePreservesContentCommentsHyperlinksSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var styleId = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = new CellColor(0xEE, 0xDD, 0xCC),
            NumberFormat = "$#,##0.00"
        });
        sheet.SetCell(a1, new Cell { Value = new NumberValue(42), StyleId = styleId });
        sheet.Comments[a1] = "Keep note";
        sheet.Hyperlinks[a1] = "https://example.com";
        sheet.HyperlinkMetadata[a1] = new HyperlinkMetadata(ScreenTip: "Open example");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.ClearSelectedRangeFormats();

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, a1));
        sheet.GetValue(a1).Should().Be(new NumberValue(42));
        sheet.Comments[a1].Should().Be("Keep note");
        sheet.Hyperlinks[a1].Should().Be("https://example.com");
        var clearedStyle = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
        clearedStyle.Bold.Should().BeFalse();
        clearedStyle.FillColor.Should().BeNull();
        clearedStyle.NumberFormat.Should().Be("General");

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.GetCell(a1)!.StyleId.Should().Be(styleId);
        sheet.GetValue(a1).Should().Be(new NumberValue(42));
        sheet.HyperlinkMetadata[a1].Should().Be(new HyperlinkMetadata(ScreenTip: "Open example"));
    }

    [Fact]
    public void ClearSelectedRangeComments_ClearsNotesAndThreadedCommentsPreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new TextValue("note"));
        sheet.Comments[a1] = "Legacy note";
        sheet.ThreadedComments[b1] = new ThreadedComment("Thread note", "Quinn")
        {
            Replies = [new CommentReply("Reply", "Codex")]
        };
        sheet.Comments[c1] = "Outside";
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));

        var result = session.ClearSelectedRangeComments();

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Contain([a1, b1]);
        session.SelectedRange.Should().Be(new GridRange(a1, b1));
        sheet.GetValue(a1).Should().Be(new TextValue("note"));
        sheet.Comments.Should().NotContainKey(a1);
        sheet.ThreadedComments.Should().NotContainKey(b1);
        sheet.Comments[c1].Should().Be("Outside");

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.Comments[a1].Should().Be("Legacy note");
        sheet.ThreadedComments[b1].Text.Should().Be("Thread note");
        sheet.ThreadedComments[b1].Replies.Should().Equal(new CommentReply("Reply", "Codex"));
        sheet.Comments[c1].Should().Be("Outside");
    }

    [Fact]
    public void EditActiveCellThreadedComment_ReplacesRootTextReadsBackAndUndoes()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.ThreadedComments[a1] = new ThreadedComment("Original", "Quinn");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        session.GetActiveCellThreadedCommentText().Should().Be("Original");

        var result = session.EditActiveCellThreadedComment("Edited");

        result.Success.Should().BeTrue();
        sheet.ThreadedComments[a1].Text.Should().Be("Edited");
        sheet.ThreadedComments[a1].Author.Should().Be("Quinn");
        session.GetActiveCellThreadedCommentText().Should().Be("Edited");

        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.ThreadedComments[a1].Text.Should().Be("Original");
    }

    [Fact]
    public void SetActiveCellThreadedCommentResolved_TogglesResolvedFlagReadsBackAndUndoes()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.ThreadedComments[a1] = new ThreadedComment("Thread", "Quinn");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        session.IsActiveCellThreadedCommentResolved().Should().BeFalse();

        var resolved = session.SetActiveCellThreadedCommentResolved(true);

        resolved.Success.Should().BeTrue();
        sheet.ThreadedComments[a1].IsResolved.Should().BeTrue();
        session.IsActiveCellThreadedCommentResolved().Should().BeTrue();

        session.SetActiveCellThreadedCommentResolved(false).Success.Should().BeTrue();
        sheet.ThreadedComments[a1].IsResolved.Should().BeFalse();

        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.ThreadedComments[a1].IsResolved.Should().BeTrue();
    }

    [Fact]
    public void GetActiveCellNote_ReturnsExistingNoteTextOrNull()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.Comments[a1] = "A note";
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        session.SelectCell(a1);
        session.GetActiveCellNote().Should().Be("A note");

        session.SelectCell(b1);
        session.GetActiveCellNote().Should().BeNull();
    }

    [Fact]
    public void ClearSelectedRangeHyperlinks_ClearsTargetsPreservesDisplayTextSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("Example"));
        sheet.Hyperlinks[a1] = "https://example.com";
        sheet.HyperlinkMetadata[a1] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            ScreenTip: "Open example");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.ClearSelectedRangeHyperlinks();

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().ContainSingle().Which.Should().Be(a1);
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, a1));
        sheet.GetValue(a1).Should().Be(new TextValue("Example"));
        sheet.Hyperlinks.Should().NotContainKey(a1);
        sheet.HyperlinkMetadata.Should().NotContainKey(a1);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(new TextValue("Example"));
        sheet.Hyperlinks[a1].Should().Be("https://example.com");
        sheet.HyperlinkMetadata[a1].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            ScreenTip: "Open example"));
    }

    [Fact]
    public void SetSelectedRangeHyperlink_AppliesPlanToSelectedRangeAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new TextValue("Old A"));
        sheet.SetCell(b1, new TextValue("Old B"));
        sheet.SetCell(c1, new TextValue("Outside"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));
        var plan = HyperlinkDialogPlanner.Plan(
            "review@example.test",
            "Team mail",
            HyperlinkTargetKind.EmailAddress,
            "Email team",
            "team@example.test");

        var result = session.SetSelectedRangeHyperlink(plan);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(a1, b1);
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b1));
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(new TextValue("Team mail"));
        sheet.GetValue(b1).Should().Be(new TextValue("Team mail"));
        sheet.GetValue(c1).Should().Be(new TextValue("Outside"));
        sheet.Hyperlinks[a1].Should().Be("mailto:review@example.test");
        sheet.Hyperlinks[b1].Should().Be("mailto:review@example.test");
        sheet.HyperlinkMetadata[a1].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.EmailAddress,
            "Email team",
            "team@example.test"));
        sheet.HyperlinkMetadata[b1].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.EmailAddress,
            "Email team",
            "team@example.test"));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(new TextValue("Old A"));
        sheet.GetValue(b1).Should().Be(new TextValue("Old B"));
        sheet.Hyperlinks.Should().NotContainKey(a1);
        sheet.Hyperlinks.Should().NotContainKey(b1);
        sheet.HyperlinkMetadata.Should().NotContainKey(a1);
        sheet.HyperlinkMetadata.Should().NotContainKey(b1);
    }

    [Fact]
    public void SetSelectedRangeHyperlink_PropagatesAcrossGroupedSheets()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var detailsA1 = new CellAddress(details.Id, 1, 1);
        summary.SetCell(summaryA1, new TextValue("Summary"));
        details.SetCell(detailsA1, new TextValue("Details"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryA1);
        var plan = HyperlinkDialogPlanner.Plan(
            "Sheet1!A1",
            "Jump",
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump within workbook",
            "SummaryTop");

        var result = session.SetSelectedRangeHyperlink(plan);

        result.Success.Should().BeTrue();
        summary.Hyperlinks[summaryA1].Should().Be("Sheet1!A1");
        details.Hyperlinks[detailsA1].Should().Be("Sheet1!A1");
        summary.HyperlinkMetadata[summaryA1].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump within workbook",
            "SummaryTop"));
        details.HyperlinkMetadata[detailsA1].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump within workbook",
            "SummaryTop"));
        summary.GetValue(summaryA1).Should().Be(new TextValue("Jump"));
        details.GetValue(detailsA1).Should().Be(new TextValue("Jump"));
    }

    [Fact]
    public void ClearSelectedRangeAll_ClearsContentsFormatsRulesCommentsHyperlinksSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var range = new GridRange(a1, b1);
        var styleId = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = new CellColor(0x22, 0x99, 0x66),
            NumberFormat = "0.00%"
        });
        sheet.SetCell(a1, new Cell { Value = new NumberValue(10), StyleId = styleId });
        sheet.SetFormula(b1, "A1+1");
        sheet.Comments[a1] = "Legacy note";
        sheet.ThreadedComments[b1] = new ThreadedComment("Thread note", "Quinn");
        sheet.Hyperlinks[a1] = "https://example.com";
        sheet.HyperlinkMetadata[a1] = new HyperlinkMetadata(ScreenTip: "Open example");
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "5"
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = range,
            Type = DvType.List,
            Formula1 = "Yes,No"
        });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(range);

        var result = session.ClearSelectedRangeAll();

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(range);
        sheet.GetCell(a1)!.Value.Should().Be(BlankValue.Instance);
        sheet.GetCell(b1)!.FormulaText.Should().BeNull();
        GetStyle(workbook, sheet, a1).Bold.Should().BeFalse();
        GetStyle(workbook, sheet, a1).FillColor.Should().BeNull();
        GetStyle(workbook, sheet, a1).NumberFormat.Should().Be("General");
        sheet.ConditionalFormats.Should().BeEmpty();
        sheet.DataValidations.Should().BeEmpty();
        sheet.Comments.Should().BeEmpty();
        sheet.ThreadedComments.Should().BeEmpty();
        sheet.Hyperlinks.Should().BeEmpty();
        sheet.HyperlinkMetadata.Should().BeEmpty();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(new NumberValue(10));
        sheet.GetCell(b1)!.FormulaText.Should().Be("A1+1");
        sheet.GetCell(a1)!.StyleId.Should().Be(styleId);
        sheet.ConditionalFormats.Should().ContainSingle();
        sheet.DataValidations.Should().ContainSingle();
        sheet.Comments[a1].Should().Be("Legacy note");
        sheet.ThreadedComments[b1].Text.Should().Be("Thread note");
        sheet.Hyperlinks[a1].Should().Be("https://example.com");
        sheet.HyperlinkMetadata[a1].Should().Be(new HyperlinkMetadata(ScreenTip: "Open example"));
    }

    [Fact]
    public void ClearSelectedRangeAll_PropagatesAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var detailsA1 = new CellAddress(details.Id, 1, 1);
        var hiddenA1 = new CellAddress(hidden.Id, 1, 1);
        var summaryStyle = workbook.RegisterStyle(new CellStyle { Bold = true, FillColor = new CellColor(0xAA, 0xBB, 0xCC) });
        var detailsStyle = workbook.RegisterStyle(new CellStyle { Italic = true, FillColor = new CellColor(0xCC, 0xBB, 0xAA) });
        var hiddenStyle = workbook.RegisterStyle(new CellStyle { Underline = true });
        summary.SetCell(summaryA1, new Cell { Value = new TextValue("summary"), StyleId = summaryStyle });
        details.SetCell(detailsA1, new Cell { Value = new TextValue("details"), StyleId = detailsStyle });
        hidden.SetCell(hiddenA1, new Cell { Value = new TextValue("hidden"), StyleId = hiddenStyle });
        summary.Comments[summaryA1] = "summary note";
        details.Comments[detailsA1] = "details note";
        hidden.Comments[hiddenA1] = "hidden note";
        summary.Hyperlinks[summaryA1] = "https://summary.example.com";
        details.Hyperlinks[detailsA1] = "https://details.example.com";
        hidden.Hyperlinks[hiddenA1] = "https://hidden.example.com";
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryA1);

        var result = session.ClearSelectedRangeAll();

        result.Success.Should().BeTrue();
        summary.GetCell(summaryA1)!.Value.Should().Be(BlankValue.Instance);
        details.GetCell(detailsA1)!.Value.Should().Be(BlankValue.Instance);
        hidden.GetValue(hiddenA1).Should().Be(new TextValue("hidden"));
        summary.Comments.Should().NotContainKey(summaryA1);
        details.Comments.Should().NotContainKey(detailsA1);
        hidden.Comments[hiddenA1].Should().Be("hidden note");
        summary.Hyperlinks.Should().NotContainKey(summaryA1);
        details.Hyperlinks.Should().NotContainKey(detailsA1);
        hidden.Hyperlinks[hiddenA1].Should().Be("https://hidden.example.com");

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.GetValue(summaryA1).Should().Be(new TextValue("summary"));
        details.GetValue(detailsA1).Should().Be(new TextValue("details"));
        hidden.GetValue(hiddenA1).Should().Be(new TextValue("hidden"));
        summary.GetCell(summaryA1)!.StyleId.Should().Be(summaryStyle);
        details.GetCell(detailsA1)!.StyleId.Should().Be(detailsStyle);
        hidden.GetCell(hiddenA1)!.StyleId.Should().Be(hiddenStyle);
        summary.Comments[summaryA1].Should().Be("summary note");
        details.Comments[detailsA1].Should().Be("details note");
        hidden.Comments[hiddenA1].Should().Be("hidden note");
        summary.Hyperlinks[summaryA1].Should().Be("https://summary.example.com");
        details.Hyperlinks[detailsA1].Should().Be("https://details.example.com");
        hidden.Hyperlinks[hiddenA1].Should().Be("https://hidden.example.com");
    }

    [Fact]
    public void InsertAutoSumFormula_AggregatesUseVerticalSelectionAndKeepFormulaSelected()
    {
        var functionNames = new[] { "SUM", "AVERAGE", "COUNT", "COUNTA", "MAX", "MIN" };

        foreach (var functionName in functionNames)
        {
            var workbook = CreateWorkbook();
            var sheet = workbook.Sheets.Single();
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var a2 = new CellAddress(sheet.Id, 2, 1);
            var a3 = new CellAddress(sheet.Id, 3, 1);
            var a4 = new CellAddress(sheet.Id, 4, 1);
            sheet.SetCell(a1, new NumberValue(10));
            sheet.SetCell(a2, new NumberValue(20));
            sheet.SetCell(a3, new NumberValue(30));
            var session = CreateSession(new StartupWorkbookLoadResult(
                workbook,
                "Book.fxl",
                "Opened .fxl.",
                IsFallback: false));
            session.SelectRange(new GridRange(a1, a3));

            var result = session.InsertAutoSumFormula(functionName);

            result.Success.Should().BeTrue(functionName);
            result.AffectedCells.Should().ContainSingle().Which.Should().Be(a4);
            sheet.GetCell(a4)!.FormulaText.Should().Be($"{functionName}(A1:A3)");
            session.ActiveCell.Should().Be(a4);
            session.SelectedRange.Should().Be(new GridRange(session.ActiveCell, session.ActiveCell));
        }
    }

    [Fact]
    public void InsertAutoSumFormula_AggregatesUseHorizontalSelectionAndKeepFormulaSelected()
    {
        var functionNames = new[] { "SUM", "AVERAGE", "COUNT", "COUNTA", "MAX", "MIN" };

        foreach (var functionName in functionNames)
        {
            var workbook = CreateWorkbook();
            var sheet = workbook.Sheets.Single();
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b1 = new CellAddress(sheet.Id, 1, 2);
            var c1 = new CellAddress(sheet.Id, 1, 3);
            var d1 = new CellAddress(sheet.Id, 1, 4);
            sheet.SetCell(a1, new NumberValue(10));
            sheet.SetCell(b1, new NumberValue(20));
            sheet.SetCell(c1, new NumberValue(30));
            var session = CreateSession(new StartupWorkbookLoadResult(
                workbook,
                "Book.fxl",
                "Opened .fxl.",
                IsFallback: false));
            session.SelectRange(new GridRange(a1, c1));

            var result = session.InsertAutoSumFormula(functionName);

            result.Success.Should().BeTrue(functionName);
            result.AffectedCells.Should().ContainSingle().Which.Should().Be(d1);
            sheet.GetCell(d1)!.FormulaText.Should().Be($"{functionName}(A1:C1)");
            session.ActiveCell.Should().Be(d1);
            session.SelectedRange.Should().Be(new GridRange(d1, d1));
        }
    }

    [Fact]
    public void InsertAutoSumFormula_ReturnsNoOpWhenSelectionTargetWouldExceedWorksheet()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var start = new CellAddress(sheet.Id, CellAddress.MaxRow - 1, 1);
        var end = new CellAddress(sheet.Id, CellAddress.MaxRow, 1);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(start, end));

        var result = session.InsertAutoSumFormula("SUM");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("outside the worksheet bounds");
        result.AffectedCells.Should().BeEmpty();
        session.ActiveCell.Should().Be(start);
        session.SelectedRange.Should().Be(new GridRange(start, end));
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void InsertAutoSumFormula_SumUsesNumbersAboveKeepsFormulaSelectedAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var c2 = new CellAddress(sheet.Id, 2, 3);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        var c4 = new CellAddress(sheet.Id, 4, 3);
        sheet.SetCell(c2, new NumberValue(10));
        sheet.SetCell(c3, new NumberValue(20));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(c4);

        var result = session.InsertAutoSumFormula("SUM");

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().ContainSingle().Which.Should().Be(c4);
        sheet.GetCell(c4)!.FormulaText.Should().Be("SUM(C2:C3)");
        session.ActiveCell.Should().Be(c4);
        session.SelectedRange.Should().Be(new GridRange(session.ActiveCell, session.ActiveCell));
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.GetCell(c4).Should().BeNull();
        session.CanRedo.Should().BeTrue();
    }

    [Fact]
    public void InsertAutoSumFormula_AggregatesFallBackToNumbersOnTheLeftAndKeepFormulaSelected()
    {
        var functionNames = new[] { "SUM", "AVERAGE", "COUNT", "COUNTA", "MAX", "MIN" };

        foreach (var functionName in functionNames)
        {
            var workbook = CreateWorkbook();
            var sheet = workbook.Sheets.Single();
            var a5 = new CellAddress(sheet.Id, 5, 1);
            var b5 = new CellAddress(sheet.Id, 5, 2);
            var c5 = new CellAddress(sheet.Id, 5, 3);
            sheet.SetCell(a5, new NumberValue(10));
            sheet.SetCell(b5, new NumberValue(20));
            var session = CreateSession(new StartupWorkbookLoadResult(
                workbook,
                "Book.fxl",
                "Opened .fxl.",
                IsFallback: false));
            session.SelectCell(c5);

            var result = session.InsertAutoSumFormula(functionName);

            result.Success.Should().BeTrue(functionName);
            sheet.GetCell(c5)!.FormulaText.Should().Be($"{functionName}(A5:B5)");
            session.ActiveCell.Should().Be(c5);
            session.SelectedRange.Should().Be(new GridRange(c5, c5));
        }
    }

    [Fact]
    public void InsertAutoSumFormula_AggregatesUseSameInferredRangeAndKeepFormulaSelected()
    {
        var functionNames = new[] { "SUM", "AVERAGE", "COUNT", "COUNTA", "MAX", "MIN" };

        foreach (var functionName in functionNames)
        {
            var workbook = CreateWorkbook();
            var sheet = workbook.Sheets.Single();
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var a2 = new CellAddress(sheet.Id, 2, 1);
            var a3 = new CellAddress(sheet.Id, 3, 1);
            sheet.SetCell(a1, new NumberValue(10));
            sheet.SetCell(a2, new NumberValue(20));
            var session = CreateSession(new StartupWorkbookLoadResult(
                workbook,
                "Book.fxl",
                "Opened .fxl.",
                IsFallback: false));
            session.SelectCell(a3);

            var result = session.InsertAutoSumFormula(functionName);

            result.Success.Should().BeTrue(functionName);
            sheet.GetCell(a3)!.FormulaText.Should().Be($"{functionName}(A1:A2)");
            session.ActiveCell.Should().Be(a3);
            session.SelectedRange.Should().Be(new GridRange(a3, a3));
        }
    }

    [Fact]
    public void InsertAutoSumFormula_PropagatesAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryA2 = new CellAddress(summary.Id, 2, 1);
        var detailsA1 = new CellAddress(details.Id, 1, 1);
        var detailsA2 = new CellAddress(details.Id, 2, 1);
        var hiddenA1 = new CellAddress(hidden.Id, 1, 1);
        var hiddenA2 = new CellAddress(hidden.Id, 2, 1);
        summary.SetCell(summaryA1, new NumberValue(10));
        details.SetCell(detailsA1, new NumberValue(20));
        hidden.SetCell(hiddenA1, new NumberValue(30));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryA2);

        var result = session.InsertAutoSumFormula("MAX");

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Contain([summaryA2, detailsA2]);
        result.AffectedCells.Should().NotContain(hiddenA2);
        summary.GetCell(summaryA2)!.FormulaText.Should().Be("MAX(A1:A1)");
        details.GetCell(detailsA2)!.FormulaText.Should().Be("MAX(A1:A1)");
        hidden.GetCell(hiddenA2).Should().BeNull();
        session.ActiveCell.Should().Be(summaryA2);
        session.SelectedRange.Should().Be(new GridRange(summaryA2, summaryA2));
        session.IsWorkbookGrouped.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.GetCell(summaryA2).Should().BeNull();
        details.GetCell(detailsA2).Should().BeNull();
        hidden.GetCell(hiddenA2).Should().BeNull();
        session.IsWorkbookGrouped.Should().BeTrue();
    }

    [Fact]
    public void InsertAutoSumFormula_RejectsProtectedGroupedTargetAndRollsBackActiveSheet()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryA2 = new CellAddress(summary.Id, 2, 1);
        var detailsA1 = new CellAddress(details.Id, 1, 1);
        var detailsA2 = new CellAddress(details.Id, 2, 1);
        summary.SetCell(summaryA1, new NumberValue(10));
        details.SetCell(detailsA1, new NumberValue(20));
        details.SetCell(detailsA2, new TextValue("locked"));
        details.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryA2);

        var result = session.InsertAutoSumFormula("MIN");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        summary.GetCell(summaryA2).Should().BeNull();
        details.GetValue(detailsA2).Should().Be(new TextValue("locked"));
        session.ActiveSheet.Should().BeSameAs(summary);
        session.ActiveCell.Should().Be(summaryA2);
        session.IsWorkbookGrouped.Should().BeTrue();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void CanFillSelectedRange_RequiresTargetCellsByDirection()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        session.SelectCell(a1);

        session.CanFillSelectedRange(FillCellsDirection.Down).Should().BeFalse();
        session.CanFillSelectedRange(FillCellsDirection.Right).Should().BeFalse();
        session.CanFillSelectedRange(FillCellsDirection.Up).Should().BeFalse();
        session.CanFillSelectedRange(FillCellsDirection.Left).Should().BeFalse();

        session.SelectRange(new GridRange(a1, a2));

        session.CanFillSelectedRange(FillCellsDirection.Down).Should().BeTrue();
        session.CanFillSelectedRange(FillCellsDirection.Up).Should().BeTrue();
        session.CanFillSelectedRange(FillCellsDirection.Right).Should().BeFalse();
        session.CanFillSelectedRange(FillCellsDirection.Left).Should().BeFalse();

        session.SelectRange(new GridRange(a1, b1));

        session.CanFillSelectedRange(FillCellsDirection.Down).Should().BeFalse();
        session.CanFillSelectedRange(FillCellsDirection.Up).Should().BeFalse();
        session.CanFillSelectedRange(FillCellsDirection.Right).Should().BeTrue();
        session.CanFillSelectedRange(FillCellsDirection.Left).Should().BeTrue();

        session.SelectRange(new GridRange(a1, b2));

        session.CanFillSelectedRange(FillCellsDirection.Down).Should().BeTrue();
        session.CanFillSelectedRange(FillCellsDirection.Right).Should().BeTrue();
        session.CanFillSelectedRange(FillCellsDirection.Up).Should().BeTrue();
        session.CanFillSelectedRange(FillCellsDirection.Left).Should().BeTrue();
    }

    [Fact]
    public void FillSelectedRange_DownCopiesFormulaAndHyperlinkPreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetFormula(a1, "B1+$C$1");
        sheet.Hyperlinks[a1] = "https://example.com";
        sheet.HyperlinkMetadata[a1] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open example",
            "section-one");
        sheet.SetCell(a2, new TextValue("old"));
        sheet.Hyperlinks[a2] = "mailto:old@example.com";
        sheet.HyperlinkMetadata[a2] = new HyperlinkMetadata(
            HyperlinkTargetKind.EmailAddress,
            "Email old",
            "old@example.com");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, a2));

        var result = session.FillSelectedRange(FillCellsDirection.Down);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().ContainSingle().Which.Should().Be(a2);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, a2));
        sheet.GetCell(a2)!.FormulaText.Should().Be("B2+$C$1");
        sheet.Hyperlinks[a2].Should().Be("https://example.com");
        sheet.HyperlinkMetadata[a2].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open example",
            "section-one"));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        session.CanRedo.Should().BeTrue();
        sheet.GetCell(a2)!.FormulaText.Should().BeNull();
        sheet.GetValue(a2).Should().Be(new TextValue("old"));
        sheet.Hyperlinks[a2].Should().Be("mailto:old@example.com");
        sheet.HyperlinkMetadata[a2].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.EmailAddress,
            "Email old",
            "old@example.com"));
    }

    [Fact]
    public void FillSelectedRange_RightPropagatesAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryB1 = new CellAddress(summary.Id, 1, 2);
        var detailsA1 = new CellAddress(details.Id, 1, 1);
        var detailsB1 = new CellAddress(details.Id, 1, 2);
        var hiddenA1 = new CellAddress(hidden.Id, 1, 1);
        var hiddenB1 = new CellAddress(hidden.Id, 1, 2);
        summary.SetCell(summaryA1, new TextValue("summary source"));
        summary.SetCell(summaryB1, new TextValue("summary old"));
        details.SetCell(detailsA1, new TextValue("details source"));
        details.SetCell(detailsB1, new TextValue("details old"));
        hidden.SetCell(hiddenA1, new TextValue("hidden source"));
        hidden.SetCell(hiddenB1, new TextValue("hidden old"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectAllVisibleSheets();
        session.SelectRange(new GridRange(summaryA1, summaryB1));

        var result = session.FillSelectedRange(FillCellsDirection.Right);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Contain([summaryB1, detailsB1]);
        result.AffectedCells.Should().NotContain(hiddenB1);
        session.SelectedRange.Should().Be(new GridRange(summaryA1, summaryB1));
        session.IsWorkbookGrouped.Should().BeTrue();
        summary.GetValue(summaryB1).Should().Be(new TextValue("summary source"));
        details.GetValue(detailsB1).Should().Be(new TextValue("details source"));
        hidden.GetValue(hiddenB1).Should().Be(new TextValue("hidden old"));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.GetValue(summaryB1).Should().Be(new TextValue("summary old"));
        details.GetValue(detailsB1).Should().Be(new TextValue("details old"));
        hidden.GetValue(hiddenB1).Should().Be(new TextValue("hidden old"));
        session.IsWorkbookGrouped.Should().BeTrue();
    }

    [Fact]
    public void FillSelectedRange_RejectsProtectedGroupedTargetAndRollsBackActiveSheet()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryB1 = new CellAddress(summary.Id, 1, 2);
        var detailsA1 = new CellAddress(details.Id, 1, 1);
        var detailsB1 = new CellAddress(details.Id, 1, 2);
        summary.SetCell(summaryA1, new TextValue("summary source"));
        summary.SetCell(summaryB1, new TextValue("summary old"));
        details.SetCell(detailsA1, new TextValue("details source"));
        details.SetCell(detailsB1, new TextValue("locked"));
        details.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectAllVisibleSheets();
        session.SelectRange(new GridRange(summaryA1, summaryB1));

        var result = session.FillSelectedRange(FillCellsDirection.Right);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        summary.GetValue(summaryB1).Should().Be(new TextValue("summary old"));
        details.GetValue(detailsB1).Should().Be(new TextValue("locked"));
        session.ActiveSheet.Should().BeSameAs(summary);
        session.ActiveCell.Should().Be(summaryA1);
        session.IsWorkbookGrouped.Should().BeTrue();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SetSelectedRangeBold_AppliesStylePreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("value"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));

        var result = session.SetSelectedRangeBold(true);

        result.Success.Should().BeTrue();
        session.IsSelectedRangeStartBold.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b1));
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).Bold.Should().BeTrue();
        var b1StyleOnly = sheet.GetStyleOnly(b1.Row, b1.Col);
        b1StyleOnly.Should().NotBeNull();
        workbook.GetStyle(b1StyleOnly!.Value).Bold.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).Bold.Should().BeFalse();
        sheet.GetStyleOnly(b1.Row, b1.Col).Should().BeNull();
    }

    [Fact]
    public void SetSelectedRangeBold_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("locked"));
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeBold(true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).Bold.Should().BeFalse();
    }

    [Fact]
    public void SetSelectedRangeItalic_AppliesStylePreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("value"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));

        var result = session.SetSelectedRangeItalic(true);

        result.Success.Should().BeTrue();
        session.IsSelectedRangeStartItalic.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b1));
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).Italic.Should().BeTrue();
        var b1StyleOnly = sheet.GetStyleOnly(b1.Row, b1.Col);
        b1StyleOnly.Should().NotBeNull();
        workbook.GetStyle(b1StyleOnly!.Value).Italic.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).Italic.Should().BeFalse();
        sheet.GetStyleOnly(b1.Row, b1.Col).Should().BeNull();
    }

    [Fact]
    public void IsSelectedRangeStartItalic_UsesStyleOnlyFormattingForEmptyCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        session.SetSelectedRangeItalic(true);

        sheet.GetCell(a1).Should().BeNull();
        sheet.GetStyleOnly(a1.Row, a1.Col).Should().NotBeNull();
        session.IsSelectedRangeStartItalic.Should().BeTrue();
    }

    [Fact]
    public void SetSelectedRangeItalic_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("locked"));
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeItalic(true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).Italic.Should().BeFalse();
    }

    [Fact]
    public void SetSelectedRangeUnderline_AppliesStyleClearsStrikethroughPreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("value"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle { Strikethrough = true });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));

        var result = session.SetSelectedRangeUnderline(true);

        result.Success.Should().BeTrue();
        session.IsSelectedRangeStartUnderline.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b1));
        var a1Style = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
        a1Style.Underline.Should().BeTrue();
        a1Style.Strikethrough.Should().BeFalse();
        var b1StyleOnly = sheet.GetStyleOnly(b1.Row, b1.Col);
        b1StyleOnly.Should().NotBeNull();
        var b1Style = workbook.GetStyle(b1StyleOnly!.Value);
        b1Style.Underline.Should().BeTrue();
        b1Style.Strikethrough.Should().BeFalse();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        var restoredA1Style = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
        restoredA1Style.Underline.Should().BeFalse();
        restoredA1Style.Strikethrough.Should().BeTrue();
        sheet.GetStyleOnly(b1.Row, b1.Col).Should().BeNull();
    }

    [Fact]
    public void IsSelectedRangeStartUnderline_UsesSingleUnderlineStateForStyleOnlyFormatting()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        session.SetSelectedRangeUnderline(true);

        sheet.GetCell(a1).Should().BeNull();
        sheet.GetStyleOnly(a1.Row, a1.Col).Should().NotBeNull();
        session.IsSelectedRangeStartUnderline.Should().BeTrue();
    }

    [Fact]
    public void IsSelectedRangeStartUnderline_IsFalseForImportedUnderlineAndStrikethroughCombination()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("styled"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle
        {
            Underline = true,
            Strikethrough = true
        });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        session.IsSelectedRangeStartUnderline.Should().BeFalse();
    }

    [Fact]
    public void SetSelectedRangeUnderline_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("locked"));
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeUnderline(true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).Underline.Should().BeFalse();
    }

    [Fact]
    public void SetSelectedRangeStrikethrough_AppliesStyleClearsUnderlineModesPreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("value"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle
        {
            Underline = true,
            DoubleUnderline = true
        });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));

        var result = session.SetSelectedRangeStrikethrough(true);

        result.Success.Should().BeTrue();
        session.IsSelectedRangeStartStrikethrough.Should().BeTrue();
        session.IsSelectedRangeStartUnderline.Should().BeFalse();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b1));
        var a1Style = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
        a1Style.Strikethrough.Should().BeTrue();
        a1Style.Underline.Should().BeFalse();
        a1Style.DoubleUnderline.Should().BeFalse();
        var b1StyleOnly = sheet.GetStyleOnly(b1.Row, b1.Col);
        b1StyleOnly.Should().NotBeNull();
        var b1Style = workbook.GetStyle(b1StyleOnly!.Value);
        b1Style.Strikethrough.Should().BeTrue();
        b1Style.Underline.Should().BeFalse();
        b1Style.DoubleUnderline.Should().BeFalse();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        var restoredA1Style = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
        restoredA1Style.Strikethrough.Should().BeFalse();
        restoredA1Style.Underline.Should().BeTrue();
        restoredA1Style.DoubleUnderline.Should().BeTrue();
        sheet.GetStyleOnly(b1.Row, b1.Col).Should().BeNull();
    }

    [Fact]
    public void IsSelectedRangeStartStrikethrough_UsesStyleOnlyFormattingForEmptyCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        session.SetSelectedRangeStrikethrough(true);

        sheet.GetCell(a1).Should().BeNull();
        sheet.GetStyleOnly(a1.Row, a1.Col).Should().NotBeNull();
        session.IsSelectedRangeStartStrikethrough.Should().BeTrue();
    }

    [Fact]
    public void IsSelectedRangeStartStrikethrough_IsTrueForImportedUnderlineAndStrikethroughCombination()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("styled"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle
        {
            Underline = true,
            Strikethrough = true
        });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        session.IsSelectedRangeStartStrikethrough.Should().BeTrue();
        session.IsSelectedRangeStartUnderline.Should().BeFalse();
    }

    [Fact]
    public void SetSelectedRangeStrikethrough_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("locked"));
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeStrikethrough(true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).Strikethrough.Should().BeFalse();
    }

    [Fact]
    public void SetSelectedRangeDoubleUnderline_AppliesStyleClearsUnderlineAndStrikethroughPreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("value"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle
        {
            Underline = true,
            Strikethrough = true
        });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));

        var result = session.SetSelectedRangeDoubleUnderline(true);

        result.Success.Should().BeTrue();
        session.IsSelectedRangeStartDoubleUnderline.Should().BeTrue();
        session.IsSelectedRangeStartUnderline.Should().BeFalse();
        session.IsSelectedRangeStartStrikethrough.Should().BeFalse();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b1));
        var a1Style = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
        a1Style.DoubleUnderline.Should().BeTrue();
        a1Style.Underline.Should().BeFalse();
        a1Style.Strikethrough.Should().BeFalse();
        var b1StyleOnly = sheet.GetStyleOnly(b1.Row, b1.Col);
        b1StyleOnly.Should().NotBeNull();
        var b1Style = workbook.GetStyle(b1StyleOnly!.Value);
        b1Style.DoubleUnderline.Should().BeTrue();
        b1Style.Underline.Should().BeFalse();
        b1Style.Strikethrough.Should().BeFalse();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        var restoredA1Style = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
        restoredA1Style.DoubleUnderline.Should().BeFalse();
        restoredA1Style.Underline.Should().BeTrue();
        restoredA1Style.Strikethrough.Should().BeTrue();
        sheet.GetStyleOnly(b1.Row, b1.Col).Should().BeNull();
    }

    [Fact]
    public void IsSelectedRangeStartDoubleUnderline_UsesStyleOnlyFormattingForEmptyCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        session.SetSelectedRangeDoubleUnderline(true);

        sheet.GetCell(a1).Should().BeNull();
        sheet.GetStyleOnly(a1.Row, a1.Col).Should().NotBeNull();
        session.IsSelectedRangeStartDoubleUnderline.Should().BeTrue();
    }

    [Fact]
    public void IsSelectedRangeStartDoubleUnderline_IsTrueForImportedDoubleUnderlineAndStrikethroughCombination()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("styled"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle
        {
            DoubleUnderline = true,
            Strikethrough = true
        });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        session.IsSelectedRangeStartDoubleUnderline.Should().BeTrue();
        session.IsSelectedRangeStartStrikethrough.Should().BeTrue();
        session.IsSelectedRangeStartUnderline.Should().BeFalse();
    }

    [Fact]
    public void SetSelectedRangeDoubleUnderline_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("locked"));
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeDoubleUnderline(true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).DoubleUnderline.Should().BeFalse();
    }

    [Fact]
    public void SetSelectedRangeHorizontalAlignment_AppliesStylePreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("value"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));

        var result = session.SetSelectedRangeHorizontalAlignment(HorizontalAlignment.Center);

        result.Success.Should().BeTrue();
        session.SelectedRangeStartHorizontalAlignment.Should().Be(HorizontalAlignment.Center);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b1));
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).HorizontalAlignment.Should().Be(HorizontalAlignment.Center);
        var b1StyleOnly = sheet.GetStyleOnly(b1.Row, b1.Col);
        b1StyleOnly.Should().NotBeNull();
        workbook.GetStyle(b1StyleOnly!.Value).HorizontalAlignment.Should().Be(HorizontalAlignment.Center);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).HorizontalAlignment.Should().Be(HorizontalAlignment.General);
        sheet.GetStyleOnly(b1.Row, b1.Col).Should().BeNull();
    }

    [Fact]
    public void SelectedRangeStartHorizontalAlignment_UsesStyleOnlyFormattingForEmptyCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        session.SetSelectedRangeHorizontalAlignment(HorizontalAlignment.Right);

        sheet.GetCell(a1).Should().BeNull();
        sheet.GetStyleOnly(a1.Row, a1.Col).Should().NotBeNull();
        session.SelectedRangeStartHorizontalAlignment.Should().Be(HorizontalAlignment.Right);
    }

    [Fact]
    public void SetSelectedRangeHorizontalAlignment_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("locked"));
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeHorizontalAlignment(HorizontalAlignment.Right);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).HorizontalAlignment.Should().Be(HorizontalAlignment.General);
    }

    [Fact]
    public void SetSelectedRangeVerticalAlignment_AppliesStylePreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("value"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));

        var result = session.SetSelectedRangeVerticalAlignment(VerticalAlignment.Center);

        result.Success.Should().BeTrue();
        session.SelectedRangeStartVerticalAlignment.Should().Be(VerticalAlignment.Center);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b1));
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).VerticalAlignment.Should().Be(VerticalAlignment.Center);
        var b1StyleOnly = sheet.GetStyleOnly(b1.Row, b1.Col);
        b1StyleOnly.Should().NotBeNull();
        workbook.GetStyle(b1StyleOnly!.Value).VerticalAlignment.Should().Be(VerticalAlignment.Center);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).VerticalAlignment.Should().Be(VerticalAlignment.Bottom);
        sheet.GetStyleOnly(b1.Row, b1.Col).Should().BeNull();
    }

    [Fact]
    public void SelectedRangeStartVerticalAlignment_UsesStyleOnlyFormattingForEmptyCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        session.SetSelectedRangeVerticalAlignment(VerticalAlignment.Top);

        sheet.GetCell(a1).Should().BeNull();
        sheet.GetStyleOnly(a1.Row, a1.Col).Should().NotBeNull();
        session.SelectedRangeStartVerticalAlignment.Should().Be(VerticalAlignment.Top);
    }

    [Fact]
    public void SetSelectedRangeVerticalAlignment_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("locked"));
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeVerticalAlignment(VerticalAlignment.Top);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).VerticalAlignment.Should().Be(VerticalAlignment.Bottom);
    }

    [Fact]
    public void SetSelectedRangeWrapText_AppliesStylePreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("long value"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));

        var result = session.SetSelectedRangeWrapText(true);

        result.Success.Should().BeTrue();
        session.IsSelectedRangeStartWrapText.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b1));
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).WrapText.Should().BeTrue();
        var b1StyleOnly = sheet.GetStyleOnly(b1.Row, b1.Col);
        b1StyleOnly.Should().NotBeNull();
        workbook.GetStyle(b1StyleOnly!.Value).WrapText.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).WrapText.Should().BeFalse();
        sheet.GetStyleOnly(b1.Row, b1.Col).Should().BeNull();
    }

    [Fact]
    public void IsSelectedRangeStartWrapText_UsesStyleOnlyFormattingForEmptyCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        session.SetSelectedRangeWrapText(true);

        sheet.GetCell(a1).Should().BeNull();
        sheet.GetStyleOnly(a1.Row, a1.Col).Should().NotBeNull();
        session.IsSelectedRangeStartWrapText.Should().BeTrue();
    }

    [Fact]
    public void SetSelectedRangeWrapText_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("locked"));
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeWrapText(true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).WrapText.Should().BeFalse();
    }

    [Fact]
    public void SetSelectedRangeIndentLevel_AppliesClampedStylePreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("value"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));

        var result = session.SetSelectedRangeIndentLevel(99);

        result.Success.Should().BeTrue();
        session.SelectedRangeStartIndentLevel.Should().Be(15);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b1));
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).IndentLevel.Should().Be(15);
        var b1StyleOnly = sheet.GetStyleOnly(b1.Row, b1.Col);
        b1StyleOnly.Should().NotBeNull();
        workbook.GetStyle(b1StyleOnly!.Value).IndentLevel.Should().Be(15);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).IndentLevel.Should().Be(0);
        sheet.GetStyleOnly(b1.Row, b1.Col).Should().BeNull();
    }

    [Fact]
    public void IncreaseSelectedRangeIndent_IncrementsFromSelectedRangeStartAndClampsAtFifteen()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("value"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle { IndentLevel = 15 });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.IncreaseSelectedRangeIndent();

        result.Success.Should().BeTrue();
        session.SelectedRangeStartIndentLevel.Should().Be(15);
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).IndentLevel.Should().Be(15);
    }

    [Fact]
    public void DecreaseSelectedRangeIndent_DecrementsFromSelectedRangeStartAndClampsAtZero()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("value"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle { IndentLevel = 1 });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var firstDecrease = session.DecreaseSelectedRangeIndent();
        var secondDecrease = session.DecreaseSelectedRangeIndent();

        firstDecrease.Success.Should().BeTrue();
        secondDecrease.Success.Should().BeTrue();
        session.SelectedRangeStartIndentLevel.Should().Be(0);
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).IndentLevel.Should().Be(0);
    }

    [Fact]
    public void SelectedRangeStartIndentLevel_UsesStyleOnlyFormattingForEmptyCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        session.SetSelectedRangeIndentLevel(4);

        sheet.GetCell(a1).Should().BeNull();
        sheet.GetStyleOnly(a1.Row, a1.Col).Should().NotBeNull();
        session.SelectedRangeStartIndentLevel.Should().Be(4);
    }

    [Fact]
    public void IncreaseSelectedRangeIndent_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("locked"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle { IndentLevel = 2 });
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.IncreaseSelectedRangeIndent();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).IndentLevel.Should().Be(2);
    }

    [Fact]
    public void SetSelectedRangeFontSize_AppliesStyleFitsRowsPreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("value"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));

        var result = session.SetSelectedRangeFontSize(24);

        result.Success.Should().BeTrue();
        session.SelectedRangeStartFontSize.Should().Be(24);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b1));
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).FontSize.Should().Be(24);
        var b1StyleOnly = sheet.GetStyleOnly(b1.Row, b1.Col);
        b1StyleOnly.Should().NotBeNull();
        workbook.GetStyle(b1StyleOnly!.Value).FontSize.Should().Be(24);
        sheet.RowHeights[1].Should().Be(37);
        session.Viewport.RowMetrics.Single(metric => metric.Row == 1).Height.Should().Be(37);
        session.Viewport.Cells.Single(cell => cell.Row == 1 && cell.Col == 1)
            .Style!.FontSize.Should().Be(24);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        session.CanRedo.Should().BeTrue();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).FontSize.Should().Be(11);
        sheet.GetStyleOnly(b1.Row, b1.Col).Should().BeNull();
        sheet.RowHeights.Should().NotContainKey(1);
    }

    [Fact]
    public void IncreaseSelectedRangeFontSize_UsesSelectedRangeStartStyleOnlyFormat()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);
        session.SetSelectedRangeFontSize(10);

        var result = session.IncreaseSelectedRangeFontSize();

        result.Success.Should().BeTrue();
        sheet.GetCell(a1).Should().BeNull();
        sheet.GetStyleOnly(a1.Row, a1.Col).Should().NotBeNull();
        session.SelectedRangeStartFontSize.Should().Be(12);
        sheet.RowHeights[1].Should().Be(21);
    }

    [Fact]
    public void DecreaseSelectedRangeFontSize_DecrementsFromSelectedRangeStartAndClampsAtOne()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("value"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle { FontSize = 1 });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.DecreaseSelectedRangeFontSize();

        result.Success.Should().BeTrue();
        session.SelectedRangeStartFontSize.Should().Be(1);
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).FontSize.Should().Be(1);
    }

    [Fact]
    public void IncreaseSelectedRangeFontSize_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("locked"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle { FontSize = 11 });
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.IncreaseSelectedRangeFontSize();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).FontSize.Should().Be(11);
        sheet.RowHeights.Should().BeEmpty();
    }

    [Fact]
    public void SetSelectedRangeFontColor_AppliesStylePreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var color = new CellColor(255, 0, 0);
        sheet.SetCell(a1, new TextValue("value"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle
        {
            FontColor = new CellColor(1, 2, 3),
            FontThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1)
        });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));

        var result = session.SetSelectedRangeFontColor(color);

        result.Success.Should().BeTrue();
        session.SelectedRangeStartFontColor.Should().Be(color);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b1));
        var a1Style = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
        a1Style.FontColor.Should().Be(color);
        a1Style.FontThemeColor.Should().BeNull();
        var b1StyleOnly = sheet.GetStyleOnly(b1.Row, b1.Col);
        b1StyleOnly.Should().NotBeNull();
        workbook.GetStyle(b1StyleOnly!.Value).FontColor.Should().Be(color);
        session.Viewport.Cells.Single(cell => cell.Row == 1 && cell.Col == 1)
            .Style!.FontColor.Should().Be(color);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        var restoredA1Style = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
        restoredA1Style.FontColor.Should().Be(new CellColor(1, 2, 3));
        restoredA1Style.FontThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1));
        sheet.GetStyleOnly(b1.Row, b1.Col).Should().BeNull();
    }

    [Fact]
    public void SetSelectedRangeFontName_AppliesFontFamilyToSelection()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("value"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));
        session.SelectRange(new GridRange(a1, a1));

        var result = session.SetSelectedRangeFontName("  Arial  ");

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).FontName.Should().Be("Arial");
    }

    [Fact]
    public void SetSelectedRangeFontName_BlankIsNoOpSuccess()
    {
        var workbook = CreateWorkbook();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));

        var result = session.SetSelectedRangeFontName("   ");

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void ToggleSelectedRangeAutoFilter_EnablesThenDisablesOverSelection()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));
        session.SelectRange(new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)));

        session.ActiveSheetHasAutoFilter.Should().BeFalse();

        var enable = session.ToggleSelectedRangeAutoFilter();
        enable.Success.Should().BeTrue();
        session.ActiveSheetHasAutoFilter.Should().BeTrue();

        var disable = session.ToggleSelectedRangeAutoFilter();
        disable.Success.Should().BeTrue();
        session.ActiveSheetHasAutoFilter.Should().BeFalse();
    }

    [Fact]
    public void SetSelectedRangeFillColor_UsesStyleOnlyFormattingForEmptyCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var color = new CellColor(255, 255, 0);
        sheet.SetStyleOnly(a1.Row, a1.Col, workbook.RegisterStyle(new CellStyle { Bold = true }));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeFillColor(color);

        result.Success.Should().BeTrue();
        sheet.GetCell(a1).Should().BeNull();
        var styleOnly = sheet.GetStyleOnly(a1.Row, a1.Col);
        styleOnly.Should().NotBeNull();
        workbook.GetStyle(styleOnly!.Value).Bold.Should().BeTrue();
        session.SelectedRangeStartFillColor.Should().Be(color);
        session.Viewport.Cells.Single(cell => cell.Row == 1 && cell.Col == 1)
            .Style!.FillColor.Should().Be(color);
    }

    [Fact]
    public void ClearSelectedRangeFill_ClearsDirectThemeAndPatternFillPreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("value"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle
        {
            FillColor = new CellColor(255, 255, 0),
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
            FillPatternStyle = CellFillPatternStyle.Gray125,
            FillPatternColor = new CellColor(10, 20, 30),
            FillPatternThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3)
        });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.ClearSelectedRangeFill();

        result.Success.Should().BeTrue();
        session.SelectedRangeStartFillColor.Should().BeNull();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, a1));
        var style = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
        style.FillColor.Should().BeNull();
        style.FillThemeColor.Should().BeNull();
        style.FillPatternStyle.Should().Be(CellFillPatternStyle.None);
        style.FillPatternColor.Should().BeNull();
        style.FillPatternThemeColor.Should().BeNull();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        var restoredStyle = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
        restoredStyle.FillColor.Should().Be(new CellColor(255, 255, 0));
        restoredStyle.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2));
        restoredStyle.FillPatternStyle.Should().Be(CellFillPatternStyle.Gray125);
        restoredStyle.FillPatternColor.Should().Be(new CellColor(10, 20, 30));
        restoredStyle.FillPatternThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3));
    }

    [Fact]
    public void SetSelectedRangeFillColor_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("locked"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(1, 2, 3) });
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeFillColor(new CellColor(255, 255, 0));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).FillColor.Should().Be(new CellColor(1, 2, 3));
    }

    [Fact]
    public void SetSelectedRangeFontColor_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("locked"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle { FontColor = new CellColor(1, 2, 3) });
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeFontColor(new CellColor(255, 0, 0));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).FontColor.Should().Be(new CellColor(1, 2, 3));
    }

    [Fact]
    public void ClearSelectedRangeFill_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("locked"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(1, 2, 3) });
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.ClearSelectedRangeFill();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).FillColor.Should().Be(new CellColor(1, 2, 3));
    }

    [Fact]
    public void SetSelectedRangeNumberFormat_AppliesStylePreservesSelectionUndoAndViewportText()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(42));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));

        var result = session.SetSelectedRangeNumberFormat("$#,##0.00");

        result.Success.Should().BeTrue();
        session.SelectedRangeStartNumberFormat.Should().Be("$#,##0.00");
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b1));
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).NumberFormat.Should().Be("$#,##0.00");
        var b1StyleOnly = sheet.GetStyleOnly(b1.Row, b1.Col);
        b1StyleOnly.Should().NotBeNull();
        workbook.GetStyle(b1StyleOnly!.Value).NumberFormat.Should().Be("$#,##0.00");
        session.Viewport.Cells.Single(cell => cell.Row == 1 && cell.Col == 1)
            .DisplayText.Should().Be("$42.00");

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        session.CanRedo.Should().BeTrue();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).NumberFormat.Should().Be("General");
        sheet.GetStyleOnly(b1.Row, b1.Col).Should().BeNull();
        session.Viewport.Cells.Single(cell => cell.Row == 1 && cell.Col == 1)
            .DisplayText.Should().Be("42");
    }

    [Fact]
    public void SelectedRangeStartNumberFormat_UsesStyleOnlyFormattingForEmptyCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeNumberFormat("0%");

        result.Success.Should().BeTrue();
        sheet.GetCell(a1).Should().BeNull();
        sheet.GetStyleOnly(a1.Row, a1.Col).Should().NotBeNull();
        session.SelectedRangeStartNumberFormat.Should().Be("0%");
    }

    [Fact]
    public void IncreaseSelectedRangeDecimalPlaces_UsesSelectedRangeStartFormatAndRefreshesViewportText()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(1.2));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.0" });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.IncreaseSelectedRangeDecimalPlaces();

        result.Success.Should().BeTrue();
        session.SelectedRangeStartNumberFormat.Should().Be("0.00");
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).NumberFormat.Should().Be("0.00");
        session.Viewport.Cells.Single(cell => cell.Row == 1 && cell.Col == 1)
            .DisplayText.Should().Be("1.20");
    }

    [Fact]
    public void IncreaseSelectedRangeDecimalPlaces_PreservesCommaStyleSemantics()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        const string commaStyleFormat = "_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)";
        sheet.SetCell(a1, new NumberValue(1234.5));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = commaStyleFormat });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.IncreaseSelectedRangeDecimalPlaces();

        result.Success.Should().BeTrue();
        session.SelectedRangeStartNumberFormat.Should().Be("_(* #,##0.000_);_(* (#,##0.000);_(* \"-\"???_);_(@_)");
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).NumberFormat.Should().Be("_(* #,##0.000_);_(* (#,##0.000);_(* \"-\"???_);_(@_)");
        session.Viewport.Cells.Single(cell => cell.Row == 1 && cell.Col == 1)
            .DisplayText.Should().Contain("1,234.500");
    }

    [Fact]
    public void DecreaseSelectedRangeDecimalPlaces_UsesSelectedRangeStartFormat()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(1234.567));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.000" });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.DecreaseSelectedRangeDecimalPlaces();

        result.Success.Should().BeTrue();
        session.SelectedRangeStartNumberFormat.Should().Be("$#,##0.00");
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).NumberFormat.Should().Be("$#,##0.00");
    }

    [Fact]
    public void SetSelectedRangeNumberFormat_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(10));
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeNumberFormat("0%");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).NumberFormat.Should().Be("General");
    }

    [Fact]
    public void SetSelectedRangeTextRotation_AppliesStylePreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("value"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));

        var result = session.SetSelectedRangeTextRotation(45);

        result.Success.Should().BeTrue();
        session.SelectedRangeStartTextRotation.Should().Be(45);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b1));
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).TextRotation.Should().Be(45);
        var b1StyleOnly = sheet.GetStyleOnly(b1.Row, b1.Col);
        b1StyleOnly.Should().NotBeNull();
        workbook.GetStyle(b1StyleOnly!.Value).TextRotation.Should().Be(45);
        session.Viewport.Cells.Single(cell => cell.Row == 1 && cell.Col == 1)
            .Style!.TextRotation.Should().Be(45);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        session.CanRedo.Should().BeTrue();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).TextRotation.Should().Be(0);
        sheet.GetStyleOnly(b1.Row, b1.Col).Should().BeNull();
    }

    [Fact]
    public void SetSelectedRangeTextRotation_UsesStyleOnlyFormattingForEmptyCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetStyleOnly(a1.Row, a1.Col, workbook.RegisterStyle(new CellStyle { Bold = true }));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeTextRotation(255);

        result.Success.Should().BeTrue();
        sheet.GetCell(a1).Should().BeNull();
        var styleOnly = sheet.GetStyleOnly(a1.Row, a1.Col);
        styleOnly.Should().NotBeNull();
        var style = workbook.GetStyle(styleOnly!.Value);
        style.Bold.Should().BeTrue();
        style.TextRotation.Should().Be(255);
        session.SelectedRangeStartTextRotation.Should().Be(255);
        session.Viewport.Cells.Single(cell => cell.Row == 1 && cell.Col == 1)
            .Style!.TextRotation.Should().Be(255);
    }

    [Fact]
    public void SetSelectedRangeTextRotation_RejectsUnsupportedRotationWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("value"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle { TextRotation = 45 });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeTextRotation(91);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Text rotation");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).TextRotation.Should().Be(45);
    }

    [Fact]
    public void SetSelectedRangeTextRotation_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("locked"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle { TextRotation = -45 });
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeTextRotation(90);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).TextRotation.Should().Be(-45);
    }

    [Fact]
    public void SetSelectedRangeCellStylePreset_AppliesStylePreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new TextValue("value"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b1));

        var result = session.SetSelectedRangeCellStylePreset(CellStylePreset.Input);

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b1));
        var a1Style = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
        a1Style.FillColor.Should().Be(new CellColor(255, 255, 204));
        a1Style.NumberFormat.Should().Be("#,##0.00");
        a1Style.BorderBottom.Style.Should().Be(BorderStyle.Thin);
        var b1StyleOnly = sheet.GetStyleOnly(b1.Row, b1.Col);
        b1StyleOnly.Should().NotBeNull();
        workbook.GetStyle(b1StyleOnly!.Value).FillColor.Should().Be(new CellColor(255, 255, 204));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).FillColor.Should().BeNull();
        sheet.GetStyleOnly(b1.Row, b1.Col).Should().BeNull();
    }

    [Fact]
    public void SetSelectedRangeCellStylePreset_UsesWorkbookThemeForAccentPresets()
    {
        var workbook = CreateWorkbook();
        workbook.Theme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(40, 80, 120));
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("themed"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeCellStylePreset(CellStylePreset.Accent2_40);

        result.Success.Should().BeTrue();
        var style = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
        style.FillColor.Should().Be(workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent2, 0.6));
        style.BorderBottom.Should().Be(new CellBorder(BorderStyle.Thin, workbook.Theme.GetColor(WorkbookThemeColorSlot.Accent2)));
        style.FontColor.Should().Be(CellColor.Black);
    }

    [Fact]
    public void SetSelectedRangeCellStylePreset_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("locked"));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(1, 2, 3) });
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeCellStylePreset(CellStylePreset.Good);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).FillColor.Should().Be(new CellColor(1, 2, 3));
    }

    [Fact]
    public void SetSelectedRangeBorderPreset_AppliesOutsideBordersPreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, new TextValue("value"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b2));

        var result = session.SetSelectedRangeBorderPreset(CellBorderPreset.Outside);

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b2));
        var expected = new CellBorder(BorderStyle.Thin, CellColor.Black);
        var a1Style = GetStyle(workbook, sheet, a1);
        a1Style.BorderTop.Should().Be(expected);
        a1Style.BorderLeft.Should().Be(expected);
        a1Style.BorderRight.Should().Be(new CellBorder());
        a1Style.BorderBottom.Should().Be(new CellBorder());
        var b2Style = GetStyle(workbook, sheet, b2);
        b2Style.BorderRight.Should().Be(expected);
        b2Style.BorderBottom.Should().Be(expected);
        sheet.GetStyleOnly(b2.Row, b2.Col).Should().NotBeNull();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        GetStyle(workbook, sheet, a1).BorderTop.Should().Be(new CellBorder());
        sheet.GetStyleOnly(b2.Row, b2.Col).Should().BeNull();
    }

    [Fact]
    public void SetSelectedRangeBorderPreset_InsideAppliesInteriorBorders()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(a1, new TextValue("value"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b2));

        var result = session.SetSelectedRangeBorderPreset(CellBorderPreset.Inside);

        result.Success.Should().BeTrue();
        var expected = new CellBorder(BorderStyle.Thin, CellColor.Black);
        var a1Style = GetStyle(workbook, sheet, a1);
        a1Style.BorderRight.Should().Be(expected);
        a1Style.BorderBottom.Should().Be(expected);
        a1Style.BorderTop.Should().Be(new CellBorder());
        a1Style.BorderLeft.Should().Be(new CellBorder());
        var a2Style = GetStyle(workbook, sheet, a2);
        a2Style.BorderTop.Should().Be(expected);
        a2Style.BorderRight.Should().Be(expected);
        a2Style.BorderBottom.Should().Be(new CellBorder());
        a2Style.BorderLeft.Should().Be(new CellBorder());
    }

    [Fact]
    public void SetSelectedRangeBorderPreset_InsideSingleCellSucceedsWithoutDirtyingWorkbook()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("value"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeBorderPreset(CellBorderPreset.Inside);

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        GetStyle(workbook, sheet, a1).BorderTop.Should().Be(new CellBorder());
        sheet.GetStyleOnly(a1.Row, a1.Col).Should().BeNull();
    }

    [Fact]
    public void SetSelectedRangeBorderPreset_NoBorderRemovesExistingBordersPreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var existingBorder = new CellBorder(BorderStyle.Thin, CellColor.Black);
        sheet.SetCell(a1, new TextValue("value"));
        var borderedStyle = CellStyle.Default.Clone();
        borderedStyle.BorderTop = existingBorder;
        borderedStyle.BorderRight = existingBorder;
        borderedStyle.BorderBottom = existingBorder;
        borderedStyle.BorderLeft = existingBorder;
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(borderedStyle);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, b2));

        var result = session.SetSelectedRangeBorderPreset(CellBorderPreset.NoBorder);

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(new GridRange(a1, b2));
        var clearedStyle = GetStyle(workbook, sheet, a1);
        clearedStyle.BorderTop.Should().Be(new CellBorder());
        clearedStyle.BorderRight.Should().Be(new CellBorder());
        clearedStyle.BorderBottom.Should().Be(new CellBorder());
        clearedStyle.BorderLeft.Should().Be(new CellBorder());

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        var restoredStyle = GetStyle(workbook, sheet, a1);
        restoredStyle.BorderTop.Should().Be(existingBorder);
        restoredStyle.BorderRight.Should().Be(existingBorder);
        restoredStyle.BorderBottom.Should().Be(existingBorder);
        restoredStyle.BorderLeft.Should().Be(existingBorder);
    }

    [Fact]
    public void SetSelectedRangeBorderPreset_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("locked"));
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.SetSelectedRangeBorderPreset(CellBorderPreset.All);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        GetStyle(workbook, sheet, a1).BorderBottom.Should().Be(new CellBorder());
    }

    [Fact]
    public void IsSelectedRangeMerged_DetectsOverlappingMergedRegion()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        sheet.AddMergedRegion(new GridRange(a1, b2));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        session.SelectCell(b2);
        session.IsSelectedRangeMerged.Should().BeTrue();

        session.SelectCell(c3);
        session.IsSelectedRangeMerged.Should().BeFalse();
    }

    [Fact]
    public void MergeAndCenterSelectedRange_MergesCentersPreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var range = new GridRange(a1, b2);
        sheet.SetCell(a1, new TextValue("kept"));
        sheet.SetCell(b2, new TextValue("restored"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(range);

        var result = session.MergeAndCenterSelectedRange();

        result.Success.Should().BeTrue();
        sheet.MergedRegions.Should().Contain(range);
        sheet.GetValue(b2).Should().Be(BlankValue.Instance);
        GetStyle(workbook, sheet, a1).HorizontalAlignment.Should().Be(HorizontalAlignment.Center);
        GetStyle(workbook, sheet, b2).HorizontalAlignment.Should().Be(HorizontalAlignment.Center);
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(range);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.MergedRegions.Should().NotContain(range);
        sheet.GetValue(b2).Should().Be(new TextValue("restored"));
        GetStyle(workbook, sheet, a1).HorizontalAlignment.Should().Be(HorizontalAlignment.General);
    }

    [Fact]
    public void MergeAndCenterSelectedRange_ConcatenateAllCellsCombinesContentRowMajorAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var range = new GridRange(a1, b2);
        sheet.SetCell(a1, new TextValue("first"));
        sheet.SetCell(b1, new NumberValue(42));
        sheet.SetCell(b2, new TextValue("last"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(range);

        var result = session.MergeAndCenterSelectedRange(MergeCellContentResolution.ConcatenateAllCells);

        result.Success.Should().BeTrue();
        sheet.MergedRegions.Should().Contain(range);
        sheet.GetValue(a1).Should().Be(new TextValue("first 42 last"));
        sheet.GetValue(b1).Should().Be(BlankValue.Instance);
        sheet.GetValue(a2).Should().Be(BlankValue.Instance);
        sheet.GetValue(b2).Should().Be(BlankValue.Instance);
        GetStyle(workbook, sheet, a1).HorizontalAlignment.Should().Be(HorizontalAlignment.Center);
        session.CanUndo.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.MergedRegions.Should().NotContain(range);
        sheet.GetValue(a1).Should().Be(new TextValue("first"));
        sheet.GetValue(b1).Should().Be(new NumberValue(42));
        sheet.GetValue(a2).Should().Be(BlankValue.Instance);
        sheet.GetValue(b2).Should().Be(new TextValue("last"));
        GetStyle(workbook, sheet, a1).HorizontalAlignment.Should().Be(HorizontalAlignment.General);
    }

    [Fact]
    public void MergeAndCenterSelectedRange_SingleCellCentersWithoutMergedRegion()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("value"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.MergeAndCenterSelectedRange();

        result.Success.Should().BeTrue();
        sheet.MergedRegions.Should().BeEmpty();
        GetStyle(workbook, sheet, a1).HorizontalAlignment.Should().Be(HorizontalAlignment.Center);
        session.SelectedRange.Should().Be(new GridRange(a1, a1));
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        GetStyle(workbook, sheet, a1).HorizontalAlignment.Should().Be(HorizontalAlignment.General);
    }

    [Fact]
    public void UnmergeSelectedRange_RemovesOverlappingMergedRegionPreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var range = new GridRange(a1, b2);
        sheet.AddMergedRegion(range);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(b2);

        var result = session.UnmergeSelectedRange();

        result.Success.Should().BeTrue();
        sheet.MergedRegions.Should().BeEmpty();
        session.ActiveCell.Should().Be(b2);
        session.SelectedRange.Should().Be(new GridRange(b2, b2));
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.MergedRegions.Should().Contain(range);
    }

    [Fact]
    public void UnmergeSelectedRange_NoOpsWithoutMarkingDirtyWhenSelectionHasNoMergedRegion()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.UnmergeSelectedRange();

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        session.SelectedRange.Should().Be(new GridRange(a1, a1));
    }

    [Fact]
    public void MergeAndCenterSelectedRange_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var range = new GridRange(a1, b2);
        sheet.SetCell(a1, new TextValue("locked"));
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(range);

        var result = session.MergeAndCenterSelectedRange();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        sheet.MergedRegions.Should().BeEmpty();
        GetStyle(workbook, sheet, a1).HorizontalAlignment.Should().Be(HorizontalAlignment.General);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void UnmergeSelectedRange_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var range = new GridRange(a1, b2);
        sheet.AddMergedRegion(range);
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(b2);

        var result = session.UnmergeSelectedRange();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        sheet.MergedRegions.Should().Contain(range);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void CaptureFormatPainterSource_StoresSourceWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var range = new GridRange(a1, b2);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(range);

        var captured = session.CaptureFormatPainterSource();

        captured.Should().BeTrue();
        session.IsFormatPainterActive.Should().BeTrue();
        session.IsFormatPainterPersistent.Should().BeFalse();
        session.SelectedRange.Should().Be(range);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void ApplyFormatPainterToSelectedRange_AppliesStylePreservesValuesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 3, 2);
        var sourceStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = new CellColor(255, 242, 204),
            NumberFormat = "$#,##0.00",
            HorizontalAlignment = HorizontalAlignment.Center
        });
        sheet.SetStyleOnly(source.Row, source.Col, sourceStyle);
        sheet.SetCell(target, new NumberValue(123));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(source);
        session.CaptureFormatPainterSource();
        session.SelectCell(target);

        var result = session.ApplyFormatPainterToSelectedRange();

        result.Success.Should().BeTrue();
        session.IsFormatPainterActive.Should().BeFalse();
        sheet.GetValue(target).Should().Be(new NumberValue(123));
        GetStyle(workbook, sheet, target).Should().Be(workbook.GetStyle(sourceStyle));
        session.SelectedRange.Should().Be(new GridRange(target, target));
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.GetValue(target).Should().Be(new NumberValue(123));
        GetStyle(workbook, sheet, target).Should().Be(CellStyle.Default);
        session.SelectedRange.Should().Be(new GridRange(target, target));
    }

    [Fact]
    public void ApplyFormatPainterToSelectedRange_CopiesValidationAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 3, 2);
        var sourceStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = new CellColor(198, 239, 206)
        });
        sheet.SetStyleOnly(source.Row, source.Col, sourceStyle);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(source, source),
            Type = DvType.List,
            Formula1 = "Red,Blue",
            AllowBlank = false,
            ErrorTitle = "Pick a color"
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(target, target),
            Type = DvType.WholeNumber,
            Formula1 = "1",
            Formula2 = "9"
        });
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(source);
        session.CaptureFormatPainterSource();
        session.SelectCell(target);

        var result = session.ApplyFormatPainterToSelectedRange();

        result.Success.Should().BeTrue();
        GetStyle(workbook, sheet, target).Should().Be(workbook.GetStyle(sourceStyle));
        var targetValidation = DataValidationService.GetApplicable(sheet, target)
            .Should().ContainSingle().Which;
        targetValidation.Type.Should().Be(DvType.List);
        targetValidation.Formula1.Should().Be("Red,Blue");
        targetValidation.AllowBlank.Should().BeFalse();
        targetValidation.ErrorTitle.Should().Be("Pick a color");

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        GetStyle(workbook, sheet, target).Should().Be(CellStyle.Default);
        DataValidationService.GetApplicable(sheet, target)
            .Should().ContainSingle().Which.Type.Should().Be(DvType.WholeNumber);
        DataValidationService.GetApplicable(sheet, source)
            .Should().ContainSingle().Which.Formula1.Should().Be("Red,Blue");
    }

    [Fact]
    public void ApplyFormatPainterToSelectedRange_SingleUseClearsAndPersistentStaysActiveUntilCancel()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var source = new CellAddress(sheet.Id, 1, 1);
        var firstTarget = new CellAddress(sheet.Id, 2, 1);
        var secondTarget = new CellAddress(sheet.Id, 3, 1);
        var sourceStyle = workbook.RegisterStyle(new CellStyle { Italic = true });
        sheet.SetStyleOnly(source.Row, source.Col, sourceStyle);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(source);
        session.CaptureFormatPainterSource(persistent: true);
        session.SelectCell(firstTarget);

        session.ApplyFormatPainterToSelectedRange().Success.Should().BeTrue();

        session.IsFormatPainterActive.Should().BeTrue();
        session.IsFormatPainterPersistent.Should().BeTrue();
        GetStyle(workbook, sheet, firstTarget).Should().Be(workbook.GetStyle(sourceStyle));

        session.SelectCell(secondTarget);
        session.ApplyFormatPainterToSelectedRange().Success.Should().BeTrue();

        session.IsFormatPainterActive.Should().BeTrue();
        GetStyle(workbook, sheet, secondTarget).Should().Be(workbook.GetStyle(sourceStyle));

        session.CancelFormatPainter();

        session.IsFormatPainterActive.Should().BeFalse();
        session.IsFormatPainterPersistent.Should().BeFalse();
    }

    [Fact]
    public void ApplyFormatPainterToSelectedRange_RejectsProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        var sourceStyle = workbook.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(source.Row, source.Col, sourceStyle);
        sheet.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(source);
        session.CaptureFormatPainterSource();
        session.SelectCell(target);

        var result = session.ApplyFormatPainterToSelectedRange();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsFormatPainterActive.Should().BeFalse();
        GetStyle(workbook, sheet, target).Should().Be(CellStyle.Default);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void PasteClipboardTextAtActiveCell_FallsBackToExternalTextWhenClipboardTextChanges()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetFormula(a1, "B1+1");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);
        session.CopySelectedRangeText();
        session.SelectCell(c1);

        var result = session.PasteClipboardTextAtActiveCell("100");

        result.Success.Should().BeTrue();
        sheet.GetCell(c1)!.FormulaText.Should().BeNull();
        sheet.GetValue(c1).Should().Be(new NumberValue(100));
    }

    [Fact]
    public void PasteClipboardTextAtActiveCell_RejectsEmptyExternalClipboardText()
    {
        var session = CreateSession(new StartupWorkbookLoadResult(
            CreateWorkbook(),
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.PasteClipboardTextAtActiveCell(null);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Clipboard does not contain text.");
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void PasteExternalTextAtActiveCell_PastesTabularTextAndMarksDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var b3 = new CellAddress(sheet.Id, 3, 2);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(b3);

        var result = session.PasteExternalTextAtActiveCell("10\tWest\r\nName");

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(
            b3,
            new CellAddress(sheet.Id, 3, 3),
            new CellAddress(sheet.Id, 4, 2));
        session.IsDirty.Should().BeTrue();
        session.ActiveCell.Should().Be(b3);
        session.SelectedRange.Should().Be(new GridRange(b3, new CellAddress(sheet.Id, 4, 3)));
        session.CanUndo.Should().BeTrue();
        sheet.GetValue(b3).Should().Be(new NumberValue(10));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 3)).Should().Be(new TextValue("West"));
        sheet.GetValue(new CellAddress(sheet.Id, 4, 2)).Should().Be(new TextValue("Name"));
        sheet.GetCell(new CellAddress(sheet.Id, 4, 3)).Should().BeNull();
    }

    [Fact]
    public void PasteExternalTextAtActiveCell_TilesClipboardRowsAcrossLargerSelectedRange()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var b3 = new CellAddress(sheet.Id, 3, 2);
        var e5 = new CellAddress(sheet.Id, 5, 5);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(b3, e5));

        var result = session.PasteExternalTextAtActiveCell("10\tWest\r\nName\tEast");

        result.Success.Should().BeTrue();
        session.SelectedRange.Should().Be(new GridRange(b3, e5));
        sheet.GetValue(b3).Should().Be(new NumberValue(10));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 3)).Should().Be(new TextValue("West"));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 4)).Should().Be(new NumberValue(10));
        sheet.GetValue(new CellAddress(sheet.Id, 4, 2)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 4, 3)).Should().Be(new TextValue("East"));
        sheet.GetValue(e5).Should().Be(new TextValue("West"));
    }

    [Fact]
    public void PasteExternalTextAtActiveCell_CanPreserveNumericLookingFieldsAsText()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(a1);

        var result = session.PasteExternalTextAtActiveCell("00123", preserveText: true);

        result.Success.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(new TextValue("00123"));
    }

    [Fact]
    public void PasteExternalTextAtActiveCell_RecalculatesDependentsAndUndoRestoresThem()
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
            IsFallback: false));
        session.SelectCell(a1);

        var paste = session.PasteExternalTextAtActiveCell("4");
        sheet.GetCell(a1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(4);
        sheet.GetCell(b1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(5);

        var undo = session.UndoLastEdit();

        paste.Success.Should().BeTrue();
        undo.Success.Should().BeTrue();
        session.CanRedo.Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(1);
        sheet.GetCell(b1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(2);
    }

    [Fact]
    public void PasteExternalTextAtActiveCell_RejectsOutOfBoundsPasteWithoutMutation()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var edge = new CellAddress(sheet.Id, 1, CellAddress.MaxCol);
        sheet.SetCell(edge, new TextValue("keep"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(edge);

        var result = session.PasteExternalTextAtActiveCell("A\tB");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("bounds");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        session.ActiveCell.Should().Be(edge);
        sheet.GetValue(edge).Should().Be(new TextValue("keep"));
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
        session.SelectedRange.Should().Be(new GridRange(session.ActiveCell, session.ActiveCell));
        session.SheetTabs.Should().Equal(
            new WorkbookSheetTab(summary.Id, "Sheet1", IsActive: false),
            new WorkbookSheetTab(details.Id, "Details", IsActive: true));
        session.Viewport.Cells.Should().Contain(cell => cell.Row == 3 && cell.Col == 2);
    }

    [Fact]
    public void RenameActiveSheet_TrimsNameRefreshesTabsAndPreservesSelection()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(b2, new TextValue("selected"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(b2);

        var result = session.RenameActiveSheet("  Data  ");

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        sheet.Name.Should().Be("Data");
        session.ActiveSheet.Should().BeSameAs(sheet);
        session.ActiveCell.Should().Be(b2);
        session.SelectedRange.Should().Be(new GridRange(b2, b2));
        session.SheetTabs.Should().ContainSingle()
            .Which.Should().Be(new WorkbookSheetTab(sheet.Id, "Data", IsActive: true));
        session.Viewport.Cells.Should().Contain(cell => cell.Row == 2 && cell.Col == 2);
    }

    [Fact]
    public void RenameActiveSheet_RewritesFormulaReferencesAndKeepsUndoRedoCoherent()
    {
        var workbook = CreateWorkbook();
        var source = workbook.Sheets.Single();
        var formulas = workbook.AddSheet("Formulas");
        var formulaAddress = new CellAddress(formulas.Id, 1, 1);
        formulas.SetFormula(formulaAddress, "Sheet1!A1");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.RenameActiveSheet("Data");

        result.Success.Should().BeTrue();
        source.Name.Should().Be("Data");
        formulas.GetCell(formulaAddress)!.FormulaText.Should().Be("Data!A1");

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        source.Name.Should().Be("Sheet1");
        formulas.GetCell(formulaAddress)!.FormulaText.Should().Be("Sheet1!A1");
        session.ActiveSheet.Should().BeSameAs(source);
        session.CanRedo.Should().BeTrue();

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        source.Name.Should().Be("Data");
        formulas.GetCell(formulaAddress)!.FormulaText.Should().Be("Data!A1");
        session.ActiveSheet.Should().BeSameAs(source);
    }

    [Fact]
    public void RenameActiveSheet_NoOpsSameNameWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.RenameActiveSheet("Sheet1");

        result.Success.Should().BeTrue();
        sheet.Name.Should().Be("Sheet1");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void RenameActiveSheet_RejectsInvalidOrDuplicateNameWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        workbook.AddSheet("Data");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var duplicate = session.RenameActiveSheet("data");
        var invalid = session.RenameActiveSheet("Bad/Name");
        var blank = session.RenameActiveSheet("   ");

        duplicate.Success.Should().BeFalse();
        invalid.Success.Should().BeFalse();
        blank.Success.Should().BeFalse();
        duplicate.ErrorMessage.Should().Contain("already exists");
        invalid.ErrorMessage.Should().Contain("cannot contain");
        blank.ErrorMessage.Should().Contain("cannot be blank");
        workbook.Sheets[0].Name.Should().Be("Sheet1");
        session.ActiveSheet.Name.Should().Be("Sheet1");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SetShowFormulas_RefreshesViewportPreservesSelectionAndKeepsUndoRedoCoherent()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var formulaAddress = new CellAddress(sheet.Id, 1, 1);
        var formulaCell = Cell.FromFormula("B1+1");
        formulaCell.Value = new NumberValue(5);
        sheet.SetCell(formulaAddress, formulaCell);
        var selectedCell = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(selectedCell, new TextValue("selected"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(selectedCell);

        session.Viewport.Cells.Should().Contain(cell =>
            cell.Row == 1 &&
            cell.Col == 1 &&
            cell.DisplayText == "5");

        var result = session.SetShowFormulas(true);

        result.Success.Should().BeTrue();
        session.IsShowingFormulas.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(selectedCell);
        session.SelectedRange.Should().Be(new GridRange(selectedCell, selectedCell));
        session.Viewport.Cells.Should().Contain(cell =>
            cell.Row == 1 &&
            cell.Col == 1 &&
            cell.DisplayText == "=B1+1");

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        session.IsShowingFormulas.Should().BeFalse();
        session.ActiveCell.Should().Be(selectedCell);
        session.Viewport.Cells.Should().Contain(cell =>
            cell.Row == 1 &&
            cell.Col == 1 &&
            cell.DisplayText == "5");
        session.CanRedo.Should().BeTrue();

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        session.IsShowingFormulas.Should().BeTrue();
        session.ActiveCell.Should().Be(selectedCell);
        session.Viewport.Cells.Should().Contain(cell =>
            cell.Row == 1 &&
            cell.Col == 1 &&
            cell.DisplayText == "=B1+1");
    }

    [Fact]
    public void SetShowFormulas_NoOpsSameStateWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.SetShowFormulas(false);

        result.Success.Should().BeTrue();
        session.IsShowingFormulas.Should().BeFalse();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SetShowGridlinesAndHeadings_PreservesOtherViewOptionsSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.ShowGridlines = true;
        sheet.ShowHeadings = false;
        sheet.ShowRulers = false;
        var selectedCell = new CellAddress(sheet.Id, 3, 2);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(selectedCell);

        var gridlines = session.SetShowGridlines(false);

        gridlines.Success.Should().BeTrue();
        sheet.ShowGridlines.Should().BeFalse();
        sheet.ShowHeadings.Should().BeFalse();
        sheet.ShowRulers.Should().BeFalse();
        session.IsShowingGridlines.Should().BeFalse();
        session.IsShowingHeadings.Should().BeFalse();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(selectedCell);
        session.SelectedRange.Should().Be(new GridRange(selectedCell, selectedCell));

        var headings = session.SetShowHeadings(true);

        headings.Success.Should().BeTrue();
        sheet.ShowGridlines.Should().BeFalse();
        sheet.ShowHeadings.Should().BeTrue();
        sheet.ShowRulers.Should().BeFalse();
        session.IsShowingHeadings.Should().BeTrue();

        var undoHeadings = session.UndoLastEdit();

        undoHeadings.Success.Should().BeTrue();
        sheet.ShowGridlines.Should().BeFalse();
        sheet.ShowHeadings.Should().BeFalse();
        sheet.ShowRulers.Should().BeFalse();
        session.IsShowingHeadings.Should().BeFalse();

        var undoGridlines = session.UndoLastEdit();

        undoGridlines.Success.Should().BeTrue();
        sheet.ShowGridlines.Should().BeTrue();
        sheet.ShowHeadings.Should().BeFalse();
        sheet.ShowRulers.Should().BeFalse();
        session.IsShowingGridlines.Should().BeTrue();
    }

    [Fact]
    public void SetShowGridlinesAndHeadings_NoOpsSameStateWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var gridlines = session.SetShowGridlines(true);
        var headings = session.SetShowHeadings(true);

        gridlines.Success.Should().BeTrue();
        headings.Success.Should().BeTrue();
        session.IsShowingGridlines.Should().BeTrue();
        session.IsShowingHeadings.Should().BeTrue();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SetZoomPercent_ClampsPreservesSelectionAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.ZoomPercent = 125;
        var selectedCell = new CellAddress(sheet.Id, 3, 4);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(selectedCell);

        var result = session.SetZoomPercent(999);

        result.Success.Should().BeTrue();
        sheet.ZoomPercent.Should().Be(SetWorksheetZoomCommand.MaxZoomPercent);
        session.ZoomPercent.Should().Be(SetWorksheetZoomCommand.MaxZoomPercent);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(selectedCell);
        session.SelectedRange.Should().Be(new GridRange(selectedCell, selectedCell));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.ZoomPercent.Should().Be(125);
        session.ZoomPercent.Should().Be(125);
        session.ActiveCell.Should().Be(selectedCell);
        session.CanRedo.Should().BeTrue();

        var minResult = session.SetZoomPercent(-10);

        minResult.Success.Should().BeTrue();
        sheet.ZoomPercent.Should().Be(SetWorksheetZoomCommand.MinZoomPercent);
        session.ZoomPercent.Should().Be(SetWorksheetZoomCommand.MinZoomPercent);
    }

    [Fact]
    public void SetZoomPercent_NoOpsSameStateWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.SetZoomPercent(100);

        result.Success.Should().BeTrue();
        session.ZoomPercent.Should().Be(100);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void FreezePanesAtActiveCell_SetsFrozenRowsAndColumnsClearsSplitRefreshesViewportAndUndo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.SplitRow = 8;
        sheet.SplitColumn = 4;
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 100,
            viewportWidth: 220);
        var c4 = new CellAddress(sheet.Id, 4, 3);
        session.SelectCell(c4);

        var result = session.FreezePanesAtActiveCell();

        result.Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(3);
        sheet.FrozenCols.Should().Be(2);
        sheet.SplitRow.Should().BeNull();
        sheet.SplitColumn.Should().BeNull();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(c4);
        session.SelectedRange.Should().Be(new GridRange(c4, c4));
        session.Viewport.FrozenPanes.Should().Be(new FrozenPaneState(3, 2));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(0);
        sheet.FrozenCols.Should().Be(0);
        sheet.SplitRow.Should().Be(8);
        sheet.SplitColumn.Should().Be(4);
        session.Viewport.FrozenPanes.Should().BeNull();
    }

    [Fact]
    public void FreezeTopRowFreezeFirstColumnAndUnfreezePanes_RouteThroughSharedCommand()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        session.FreezeTopRow().Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(1);
        sheet.FrozenCols.Should().Be(0);
        session.Viewport.FrozenPanes.Should().Be(new FrozenPaneState(1, 0));

        session.FreezeFirstColumn().Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(0);
        sheet.FrozenCols.Should().Be(1);
        session.Viewport.FrozenPanes.Should().Be(new FrozenPaneState(0, 1));

        session.UnfreezePanes().Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(0);
        sheet.FrozenCols.Should().Be(0);
        session.Viewport.FrozenPanes.Should().BeNull();
    }

    [Fact]
    public void AddSheet_AppendsSelectsNewSheetAndKeepsUndoRedoCoherent()
    {
        var workbook = CreateWorkbook();
        var original = workbook.Sheets.Single();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var originalSelection = new GridRange(
            new CellAddress(original.Id, 4, 3),
            new CellAddress(original.Id, 5, 4));
        session.SelectRange(originalSelection);
        session.SetViewportOrigin(8, 3).Should().BeTrue();

        var result = session.AddSheet();

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Sheet1", "Sheet2");
        session.ActiveSheet.Name.Should().Be("Sheet2");
        workbook.ActiveSheetIndex.Should().Be(1);
        session.ActiveCell.Should().Be(new CellAddress(session.ActiveSheet.Id, 1, 1));
        session.SelectedRange.Should().Be(new GridRange(session.ActiveCell, session.ActiveCell));
        session.ActiveSheet.ActiveRow.Should().Be(1);
        session.ActiveSheet.ActiveCol.Should().Be(1);
        session.ActiveSheet.ViewTopRow.Should().Be(1);
        session.ActiveSheet.ViewLeftCol.Should().Be(1);
        session.SheetTabs.Should().Equal(
            new WorkbookSheetTab(original.Id, "Sheet1", IsActive: false),
            new WorkbookSheetTab(session.ActiveSheet.Id, "Sheet2", IsActive: true));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        workbook.Sheets.Should().ContainSingle().Which.Id.Should().Be(original.Id);
        session.ActiveSheet.Should().BeSameAs(original);
        session.ActiveCell.Should().Be(originalSelection.Start);
        session.SelectedRange.Should().Be(originalSelection);
        session.CanRedo.Should().BeTrue();

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Sheet1", "Sheet2");
        session.ActiveSheet.Name.Should().Be("Sheet2");
        workbook.ActiveSheetIndex.Should().Be(1);
        session.ActiveCell.Should().Be(new CellAddress(session.ActiveSheet.Id, 1, 1));
        session.SelectedRange.Should().Be(new GridRange(session.ActiveCell, session.ActiveCell));
    }

    [Fact]
    public void DuplicateActiveSheet_CopiesSheetContentSelectsCopyAndKeepsUndoRedoCoherent()
    {
        var workbook = CreateWorkbook();
        var source = workbook.Sheets.Single();
        source.SetCell(new CellAddress(source.Id, 2, 3), new TextValue("copied"));
        source.TabColor = new CellColor(255, 192, 0);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.DuplicateActiveSheet();

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Sheet1", "Sheet1 (2)");
        var copy = workbook.Sheets[1];
        copy.Id.Should().NotBe(source.Id);
        copy.GetValue(new CellAddress(copy.Id, 2, 3)).Should().Be(new TextValue("copied"));
        copy.TabColor.Should().Be(new CellColor(255, 192, 0));
        session.ActiveSheet.Should().BeSameAs(copy);
        workbook.ActiveSheetIndex.Should().Be(1);
        session.SheetTabs.Should().Equal(
            new WorkbookSheetTab(source.Id, "Sheet1", IsActive: false, source.TabColor),
            new WorkbookSheetTab(copy.Id, "Sheet1 (2)", IsActive: true, copy.TabColor));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        workbook.Sheets.Should().ContainSingle().Which.Id.Should().Be(source.Id);
        session.ActiveSheet.Should().BeSameAs(source);
        session.CanRedo.Should().BeTrue();

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Sheet1", "Sheet1 (2)");
        session.ActiveSheet.Name.Should().Be("Sheet1 (2)");
        workbook.ActiveSheetIndex.Should().Be(1);
    }

    [Fact]
    public void DuplicateActiveSheet_CopiesDrawingObjectsIntoPreviewViewport()
    {
        var source = PortPreviewWorkbookFactory.Create("Opened preview.", isFallback: false);
        var session = new WorkbookSessionFactory().Create(
            source,
            viewportHeight: 240,
            viewportWidth: 320,
            includeObjects: true);

        var result = session.DuplicateActiveSheet();

        result.Success.Should().BeTrue();
        session.ActiveSheet.Name.Should().Be("Port Plan (2)");
        session.ActiveSheet.DrawingShapes.Should().ContainSingle(shape => shape.Name == PortPreviewWorkbookFactory.PreviewShapeName);
        session.ActiveSheet.TextBoxes.Should().ContainSingle(textBox => textBox.Name == PortPreviewWorkbookFactory.PreviewTextBoxName);
        session.ActiveSheet.Pictures.Should().ContainSingle(picture => picture.Name == PortPreviewWorkbookFactory.PreviewPictureName);
        var drawingObjectNames = session.Viewport.DrawingObjects.Select(drawingObject => drawingObject.DisplayName);
        drawingObjectNames.Should().Contain(PortPreviewWorkbookFactory.PreviewShapeName);
        drawingObjectNames.Should().Contain(PortPreviewWorkbookFactory.PreviewTextBoxName);
        drawingObjectNames.Should().Contain(PortPreviewWorkbookFactory.PreviewPictureName);
    }

    [Fact]
    public void MoveActiveSheetLeft_ReordersTabsPreservesActiveSheetAndUndoRedo()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var charts = workbook.AddSheet("Charts");
        var selectedCell = new CellAddress(charts.Id, 3, 2);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectSheet(charts.Id);
        session.SelectCell(selectedCell);

        var result = session.MoveActiveSheetLeft();

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Sheet1", "Charts", "Details");
        session.ActiveSheet.Should().BeSameAs(charts);
        workbook.ActiveSheetIndex.Should().Be(1);
        session.ActiveCell.Should().Be(selectedCell);
        session.SelectedRange.Should().Be(new GridRange(selectedCell, selectedCell));
        session.SheetTabs.Should().Equal(
            new WorkbookSheetTab(summary.Id, "Sheet1", IsActive: false),
            new WorkbookSheetTab(charts.Id, "Charts", IsActive: true),
            new WorkbookSheetTab(details.Id, "Details", IsActive: false));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Sheet1", "Details", "Charts");
        session.ActiveSheet.Should().BeSameAs(charts);
        workbook.ActiveSheetIndex.Should().Be(2);
        session.ActiveCell.Should().Be(selectedCell);
        session.CanRedo.Should().BeTrue();

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Sheet1", "Charts", "Details");
        session.ActiveSheet.Should().BeSameAs(charts);
        workbook.ActiveSheetIndex.Should().Be(1);
        session.ActiveCell.Should().Be(selectedCell);
    }

    [Fact]
    public void MoveActiveSheetRight_ReordersTabsPreservesActiveSheetAndUndoRedo()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var charts = workbook.AddSheet("Charts");
        var selectedCell = new CellAddress(summary.Id, 4, 4);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(selectedCell);

        var result = session.MoveActiveSheetRight();

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Details", "Sheet1", "Charts");
        session.ActiveSheet.Should().BeSameAs(summary);
        workbook.ActiveSheetIndex.Should().Be(1);
        session.ActiveCell.Should().Be(selectedCell);
        session.SelectedRange.Should().Be(new GridRange(selectedCell, selectedCell));
        session.SheetTabs.Should().Equal(
            new WorkbookSheetTab(details.Id, "Details", IsActive: false),
            new WorkbookSheetTab(summary.Id, "Sheet1", IsActive: true),
            new WorkbookSheetTab(charts.Id, "Charts", IsActive: false));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Sheet1", "Details", "Charts");
        session.ActiveSheet.Should().BeSameAs(summary);
        workbook.ActiveSheetIndex.Should().Be(0);
        session.ActiveCell.Should().Be(selectedCell);
        session.CanRedo.Should().BeTrue();

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Details", "Sheet1", "Charts");
        session.ActiveSheet.Should().BeSameAs(summary);
        workbook.ActiveSheetIndex.Should().Be(1);
        session.ActiveCell.Should().Be(selectedCell);
    }

    [Fact]
    public void MoveActiveSheetLeftRight_RejectAtEdgesWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        workbook.AddSheet("Details");
        var charts = workbook.AddSheet("Charts");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var left = session.MoveActiveSheetLeft();
        session.SelectSheet(charts.Id);
        var right = session.MoveActiveSheetRight();

        left.Success.Should().BeFalse();
        right.Success.Should().BeFalse();
        left.ErrorMessage.Should().Contain("first");
        right.ErrorMessage.Should().Contain("last");
        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Sheet1", "Details", "Charts");
        session.ActiveSheet.Should().BeSameAs(charts);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void MoveActiveSheetLeftRight_RejectProtectedWorkbookWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var details = workbook.AddSheet("Details");
        workbook.IsStructureProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var right = session.MoveActiveSheetRight();
        session.SelectSheet(details.Id);
        var left = session.MoveActiveSheetLeft();

        right.Success.Should().BeFalse();
        left.Success.Should().BeFalse();
        right.ErrorMessage.Should().Contain("protected");
        left.ErrorMessage.Should().Contain("protected");
        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Sheet1", "Details");
        session.ActiveSheet.Should().BeSameAs(details);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SetActiveSheetTabColor_SetsColorPreservesSelectionAndUndoRedo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var selectedCell = new CellAddress(sheet.Id, 3, 2);
        sheet.SetCell(selectedCell, new TextValue("selected"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectCell(selectedCell);
        var tabColor = new CellColor(0, 176, 80);

        var result = session.SetActiveSheetTabColor(tabColor);

        result.Success.Should().BeTrue();
        sheet.TabColor.Should().Be(tabColor);
        session.SheetTabs.Should().ContainSingle()
            .Which.Should().Be(new WorkbookSheetTab(sheet.Id, "Sheet1", IsActive: true, tabColor));
        session.ActiveCell.Should().Be(selectedCell);
        session.SelectedRange.Should().Be(new GridRange(selectedCell, selectedCell));
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.TabColor.Should().BeNull();
        session.SheetTabs.Should().ContainSingle()
            .Which.TabColor.Should().BeNull();
        session.ActiveCell.Should().Be(selectedCell);
        session.CanRedo.Should().BeTrue();

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        sheet.TabColor.Should().Be(tabColor);
        session.SheetTabs.Should().ContainSingle()
            .Which.TabColor.Should().Be(tabColor);
        session.ActiveCell.Should().Be(selectedCell);
    }

    [Fact]
    public void SetActiveSheetTabColor_ClearsColorAndNoOpsSameStateWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var originalColor = new CellColor(255, 0, 0);
        sheet.TabColor = originalColor;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var noOp = session.SetActiveSheetTabColor(originalColor);

        noOp.Success.Should().BeTrue();
        sheet.TabColor.Should().Be(originalColor);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();

        var clear = session.SetActiveSheetTabColor(null);

        clear.Success.Should().BeTrue();
        sheet.TabColor.Should().BeNull();
        session.SheetTabs.Should().ContainSingle()
            .Which.TabColor.Should().BeNull();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.TabColor.Should().Be(originalColor);
        session.SheetTabs.Should().ContainSingle()
            .Which.TabColor.Should().Be(originalColor);
    }

    [Fact]
    public void SetActiveSheetTabColor_RejectsProtectedWorkbookWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        workbook.IsStructureProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.SetActiveSheetTabColor(new CellColor(0, 112, 192));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        sheet.TabColor.Should().BeNull();
        session.SheetTabs.Should().ContainSingle()
            .Which.TabColor.Should().BeNull();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SelectAllVisibleSheets_GroupsTabsWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectSheet(details.Id);

        var changed = session.SelectAllVisibleSheets();

        changed.Should().BeTrue();
        session.IsWorkbookGrouped.Should().BeTrue();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        session.SheetTabs.Should().Equal(
            new WorkbookSheetTab(summary.Id, "Sheet1", IsActive: false, TabColor: null, IsGrouped: true),
            new WorkbookSheetTab(details.Id, "Details", IsActive: true, TabColor: null, IsGrouped: true));
    }

    [Fact]
    public void SelectSheetFromTab_TogglesSheetGroupWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var charts = workbook.AddSheet("Charts");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var changed = session.SelectSheetFromTab(details.Id, selectRange: false, toggle: true);

        changed.Should().BeTrue();
        session.ActiveSheet.Should().BeSameAs(details);
        session.IsWorkbookGrouped.Should().BeTrue();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        session.SheetTabs.Should().Equal(
            new WorkbookSheetTab(summary.Id, "Sheet1", IsActive: false, TabColor: null, IsGrouped: true),
            new WorkbookSheetTab(details.Id, "Details", IsActive: true, TabColor: null, IsGrouped: true),
            new WorkbookSheetTab(charts.Id, "Charts", IsActive: false));
    }

    [Fact]
    public void SelectSheetFromTab_ShiftSelectsVisibleSheetRangeWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        var charts = workbook.AddSheet("Charts");
        hidden.IsHidden = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectSheet(details.Id);

        var changed = session.SelectSheetFromTab(charts.Id, selectRange: true, toggle: false);

        changed.Should().BeTrue();
        session.ActiveSheet.Should().BeSameAs(charts);
        session.IsWorkbookGrouped.Should().BeTrue();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        session.SheetTabs.Should().Equal(
            new WorkbookSheetTab(summary.Id, "Sheet1", IsActive: false),
            new WorkbookSheetTab(details.Id, "Details", IsActive: false, TabColor: null, IsGrouped: true),
            new WorkbookSheetTab(charts.Id, "Charts", IsActive: true, TabColor: null, IsGrouped: true));
    }

    [Fact]
    public void UngroupSheets_RestoresActiveSheetGroupWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectSheet(details.Id);
        session.SelectAllVisibleSheets();

        var changed = session.UngroupSheets();

        changed.Should().BeTrue();
        session.IsWorkbookGrouped.Should().BeFalse();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        session.SheetTabs.Should().Equal(
            new WorkbookSheetTab(summary.Id, "Sheet1", IsActive: false),
            new WorkbookSheetTab(details.Id, "Details", IsActive: true));
    }

    [Fact]
    public void SelectSheet_ClearsGroupedSheetsEvenWhenActiveSheetDoesNotChange()
    {
        var workbook = CreateWorkbook();
        var details = workbook.AddSheet("Details");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectSheet(details.Id);
        session.SelectAllVisibleSheets();

        var changed = session.SelectSheet(details.Id);

        changed.Should().BeTrue();
        session.IsWorkbookGrouped.Should().BeFalse();
        session.ActiveSheet.Should().BeSameAs(details);
        session.IsDirty.Should().BeFalse();
        session.SheetTabs.Should().OnlyContain(tab => !tab.IsGrouped);
    }

    [Fact]
    public void CommitCellText_PropagatesAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var summaryB2 = new CellAddress(summary.Id, 2, 2);
        var detailsB2 = new CellAddress(details.Id, 2, 2);
        var hiddenB2 = new CellAddress(hidden.Id, 2, 2);
        details.SetCell(detailsB2, new TextValue("old"));
        hidden.SetCell(hiddenB2, new TextValue("hidden"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryB2);

        var result = session.CommitCellText("42");

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(summaryB2, detailsB2);
        result.AffectedCells.Should().NotContain(hiddenB2);
        session.ActiveSheet.Should().BeSameAs(summary);
        session.ActiveCell.Should().Be(summaryB2);
        session.IsWorkbookGrouped.Should().BeTrue();
        summary.GetValue(summaryB2).Should().Be(new NumberValue(42));
        details.GetValue(detailsB2).Should().Be(new NumberValue(42));
        hidden.GetValue(hiddenB2).Should().Be(new TextValue("hidden"));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.GetCell(summaryB2).Should().BeNull();
        details.GetValue(detailsB2).Should().Be(new TextValue("old"));
        hidden.GetValue(hiddenB2).Should().Be(new TextValue("hidden"));
        session.IsWorkbookGrouped.Should().BeTrue();
        session.ActiveSheet.Should().BeSameAs(summary);
    }

    [Fact]
    public void CommitCellText_RejectsProtectedGroupedTargetWithoutChangingActiveSheet()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var summaryB2 = new CellAddress(summary.Id, 2, 2);
        var detailsB2 = new CellAddress(details.Id, 2, 2);
        details.SetCell(detailsB2, new TextValue("locked"));
        details.IsProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryB2);

        var result = session.CommitCellText("42");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        summary.GetCell(summaryB2).Should().BeNull();
        details.GetValue(detailsB2).Should().Be(new TextValue("locked"));
        session.ActiveSheet.Should().BeSameAs(summary);
        session.ActiveCell.Should().Be(summaryB2);
        session.IsWorkbookGrouped.Should().BeTrue();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void ClearSelectedRangeContents_PropagatesAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryB1 = new CellAddress(summary.Id, 1, 2);
        var detailsA1 = new CellAddress(details.Id, 1, 1);
        var detailsB1 = new CellAddress(details.Id, 1, 2);
        summary.SetCell(summaryA1, new NumberValue(10));
        summary.SetFormula(summaryB1, "A1+1");
        details.SetCell(detailsA1, new NumberValue(20));
        details.SetFormula(detailsB1, "A1+2");
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectAllVisibleSheets();
        session.SelectRange(new GridRange(summaryA1, summaryB1));

        var result = session.ClearSelectedRangeContents();

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(summaryA1, summaryB1, detailsA1, detailsB1);
        summary.GetValue(summaryA1).Should().Be(BlankValue.Instance);
        summary.GetCell(summaryB1)!.FormulaText.Should().BeNull();
        details.GetValue(detailsA1).Should().Be(BlankValue.Instance);
        details.GetCell(detailsB1)!.FormulaText.Should().BeNull();
        session.SelectedRange.Should().Be(new GridRange(summaryA1, summaryB1));
        session.IsWorkbookGrouped.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.GetValue(summaryA1).Should().Be(new NumberValue(10));
        summary.GetCell(summaryB1)!.FormulaText.Should().Be("A1+1");
        details.GetValue(detailsA1).Should().Be(new NumberValue(20));
        details.GetCell(detailsB1)!.FormulaText.Should().Be("A1+2");
    }

    [Fact]
    public void SetSelectedRangeBold_PropagatesAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryB1 = new CellAddress(summary.Id, 1, 2);
        var detailsA1 = new CellAddress(details.Id, 1, 1);
        var detailsB1 = new CellAddress(details.Id, 1, 2);
        summary.SetCell(summaryA1, new TextValue("summary"));
        details.SetCell(detailsA1, new TextValue("details"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectAllVisibleSheets();
        session.SelectRange(new GridRange(summaryA1, summaryB1));

        var result = session.SetSelectedRangeBold(true);

        result.Success.Should().BeTrue();
        GetStyle(workbook, summary, summaryA1).Bold.Should().BeTrue();
        GetStyle(workbook, summary, summaryB1).Bold.Should().BeTrue();
        GetStyle(workbook, details, detailsA1).Bold.Should().BeTrue();
        GetStyle(workbook, details, detailsB1).Bold.Should().BeTrue();
        session.SelectedRange.Should().Be(new GridRange(summaryA1, summaryB1));
        session.IsWorkbookGrouped.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        GetStyle(workbook, summary, summaryA1).Bold.Should().BeFalse();
        summary.GetStyleOnly(summaryB1.Row, summaryB1.Col).Should().BeNull();
        GetStyle(workbook, details, detailsA1).Bold.Should().BeFalse();
        details.GetStyleOnly(detailsB1.Row, detailsB1.Col).Should().BeNull();
    }

    [Fact]
    public void SetSelectedRangeBorderPreset_PropagatesAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryB2 = new CellAddress(summary.Id, 2, 2);
        var detailsA1 = new CellAddress(details.Id, 1, 1);
        var detailsB2 = new CellAddress(details.Id, 2, 2);
        summary.SetCell(summaryA1, new TextValue("summary"));
        details.SetCell(detailsA1, new TextValue("details"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectAllVisibleSheets();
        session.SelectRange(new GridRange(summaryA1, summaryB2));

        var result = session.SetSelectedRangeBorderPreset(CellBorderPreset.Outside);

        result.Success.Should().BeTrue();
        var expected = new CellBorder(BorderStyle.Thin, CellColor.Black);
        GetStyle(workbook, summary, summaryA1).BorderTop.Should().Be(expected);
        GetStyle(workbook, summary, summaryB2).BorderBottom.Should().Be(expected);
        GetStyle(workbook, details, detailsA1).BorderTop.Should().Be(expected);
        GetStyle(workbook, details, detailsB2).BorderBottom.Should().Be(expected);
        session.SelectedRange.Should().Be(new GridRange(summaryA1, summaryB2));
        session.IsWorkbookGrouped.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        GetStyle(workbook, summary, summaryA1).BorderTop.Should().Be(new CellBorder());
        summary.GetStyleOnly(summaryB2.Row, summaryB2.Col).Should().BeNull();
        GetStyle(workbook, details, detailsA1).BorderTop.Should().Be(new CellBorder());
        details.GetStyleOnly(detailsB2.Row, detailsB2.Col).Should().BeNull();
    }

    [Fact]
    public void MergeAndCenterSelectedRange_PropagatesAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryB2 = new CellAddress(summary.Id, 2, 2);
        var detailsA1 = new CellAddress(details.Id, 1, 1);
        var detailsB2 = new CellAddress(details.Id, 2, 2);
        var summaryRange = new GridRange(summaryA1, summaryB2);
        var detailsRange = new GridRange(detailsA1, detailsB2);
        summary.SetCell(summaryA1, new TextValue("summary"));
        details.SetCell(detailsA1, new TextValue("details"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectAllVisibleSheets();
        session.SelectRange(summaryRange);

        var result = session.MergeAndCenterSelectedRange();

        result.Success.Should().BeTrue();
        summary.MergedRegions.Should().Contain(summaryRange);
        details.MergedRegions.Should().Contain(detailsRange);
        GetStyle(workbook, summary, summaryA1).HorizontalAlignment.Should().Be(HorizontalAlignment.Center);
        GetStyle(workbook, details, detailsA1).HorizontalAlignment.Should().Be(HorizontalAlignment.Center);
        session.SelectedRange.Should().Be(summaryRange);
        session.IsWorkbookGrouped.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.MergedRegions.Should().BeEmpty();
        details.MergedRegions.Should().BeEmpty();
        GetStyle(workbook, summary, summaryA1).HorizontalAlignment.Should().Be(HorizontalAlignment.General);
        GetStyle(workbook, details, detailsA1).HorizontalAlignment.Should().Be(HorizontalAlignment.General);
    }

    [Fact]
    public void ApplyFormatPainterToSelectedRange_PropagatesAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var summarySource = new CellAddress(summary.Id, 1, 1);
        var summaryTarget = new CellAddress(summary.Id, 2, 2);
        var detailsTarget = new CellAddress(details.Id, 2, 2);
        var sourceStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FontColor = new CellColor(192, 0, 0),
            FillColor = new CellColor(255, 242, 204)
        });
        summary.SetStyleOnly(summarySource.Row, summarySource.Col, sourceStyle);
        summary.SetCell(summaryTarget, new TextValue("summary"));
        details.SetCell(detailsTarget, new TextValue("details"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectAllVisibleSheets();
        session.SelectCell(summarySource);
        session.CaptureFormatPainterSource();
        session.SelectCell(summaryTarget);

        var result = session.ApplyFormatPainterToSelectedRange();

        result.Success.Should().BeTrue();
        GetStyle(workbook, summary, summaryTarget).Should().Be(workbook.GetStyle(sourceStyle));
        GetStyle(workbook, details, detailsTarget).Should().Be(workbook.GetStyle(sourceStyle));
        summary.GetValue(summaryTarget).Should().Be(new TextValue("summary"));
        details.GetValue(detailsTarget).Should().Be(new TextValue("details"));
        session.SelectedRange.Should().Be(new GridRange(summaryTarget, summaryTarget));
        session.IsWorkbookGrouped.Should().BeTrue();
        session.IsFormatPainterActive.Should().BeFalse();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        GetStyle(workbook, summary, summaryTarget).Should().Be(CellStyle.Default);
        GetStyle(workbook, details, detailsTarget).Should().Be(CellStyle.Default);
    }

    [Fact]
    public void UnmergeSelectedRange_PropagatesAcrossGroupedSheetsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryB2 = new CellAddress(summary.Id, 2, 2);
        var detailsA1 = new CellAddress(details.Id, 1, 1);
        var detailsB2 = new CellAddress(details.Id, 2, 2);
        var summaryRange = new GridRange(summaryA1, summaryB2);
        var detailsRange = new GridRange(detailsA1, detailsB2);
        summary.AddMergedRegion(summaryRange);
        details.AddMergedRegion(detailsRange);
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryB2);

        var result = session.UnmergeSelectedRange();

        result.Success.Should().BeTrue();
        summary.MergedRegions.Should().BeEmpty();
        details.MergedRegions.Should().BeEmpty();
        session.ActiveCell.Should().Be(summaryB2);
        session.SelectedRange.Should().Be(new GridRange(summaryB2, summaryB2));
        session.IsWorkbookGrouped.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.MergedRegions.Should().Contain(summaryRange);
        details.MergedRegions.Should().Contain(detailsRange);
    }

    [Fact]
    public void SetSelectedRangeFontSize_PropagatesStyleAndRowHeightAcrossGroupedSheets()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var detailsA1 = new CellAddress(details.Id, 1, 1);
        summary.SetCell(summaryA1, new TextValue("summary"));
        details.SetCell(detailsA1, new TextValue("details"));
        var expectedRowHeight = Math.Min(409.5, FontSizePlanner.EstimateFittingRowHeight(24));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectAllVisibleSheets();
        session.SelectCell(summaryA1);

        var result = session.SetSelectedRangeFontSize(24);

        result.Success.Should().BeTrue();
        GetStyle(workbook, summary, summaryA1).FontSize.Should().Be(24);
        GetStyle(workbook, details, detailsA1).FontSize.Should().Be(24);
        summary.RowHeights[1].Should().Be(expectedRowHeight);
        details.RowHeights[1].Should().Be(expectedRowHeight);
        session.ActiveSheet.Should().BeSameAs(summary);
        session.IsWorkbookGrouped.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        GetStyle(workbook, summary, summaryA1).FontSize.Should().Be(11);
        GetStyle(workbook, details, detailsA1).FontSize.Should().Be(11);
        summary.RowHeights.Should().NotContainKey(1);
        details.RowHeights.Should().NotContainKey(1);
    }

    [Fact]
    public void HideActiveSheet_HidesSheetSelectsVisibleSurvivorAndKeepsUndoRedoCoherent()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var charts = workbook.AddSheet("Charts");
        charts.SetCell(new CellAddress(charts.Id, 3, 3), new TextValue("visible survivor"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectSheet(details.Id);

        var result = session.HideActiveSheet();

        result.Success.Should().BeTrue();
        details.IsHidden.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveSheet.Should().BeSameAs(charts);
        workbook.ActiveSheetIndex.Should().Be(2);
        session.HiddenSheets.Should().ContainSingle()
            .Which.Should().Be(new WorkbookHiddenSheet(details.Id, "Details"));
        session.SheetTabs.Should().Equal(
            new WorkbookSheetTab(summary.Id, "Sheet1", IsActive: false),
            new WorkbookSheetTab(charts.Id, "Charts", IsActive: true));
        session.Viewport.Cells.Should().Contain(cell => cell.Row == 3 && cell.Col == 3);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        details.IsHidden.Should().BeFalse();
        session.HiddenSheets.Should().BeEmpty();
        session.ActiveSheet.Should().BeSameAs(charts);
        session.SheetTabs.Should().Equal(
            new WorkbookSheetTab(summary.Id, "Sheet1", IsActive: false),
            new WorkbookSheetTab(details.Id, "Details", IsActive: false),
            new WorkbookSheetTab(charts.Id, "Charts", IsActive: true));
        session.CanRedo.Should().BeTrue();

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        details.IsHidden.Should().BeTrue();
        session.HiddenSheets.Should().ContainSingle()
            .Which.Should().Be(new WorkbookHiddenSheet(details.Id, "Details"));
        session.ActiveSheet.Should().BeSameAs(charts);
    }

    [Fact]
    public void UnhideSheet_ListsNormalHiddenSheetsSelectsUnhiddenSheetAndKeepsUndoRedoCoherent()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var audit = workbook.AddSheet("Audit");
        details.IsHidden = true;
        audit.IsHidden = true;
        audit.IsVeryHidden = true;
        details.SetCell(new CellAddress(details.Id, 4, 2), new TextValue("restored"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        session.HiddenSheets.Should().ContainSingle()
            .Which.Should().Be(new WorkbookHiddenSheet(details.Id, "Details"));

        var result = session.UnhideSheet(details.Id);

        result.Success.Should().BeTrue();
        details.IsHidden.Should().BeFalse();
        audit.IsHidden.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveSheet.Should().BeSameAs(details);
        workbook.ActiveSheetIndex.Should().Be(1);
        session.HiddenSheets.Should().BeEmpty();
        session.SheetTabs.Should().Equal(
            new WorkbookSheetTab(summary.Id, "Sheet1", IsActive: false),
            new WorkbookSheetTab(details.Id, "Details", IsActive: true));
        session.Viewport.Cells.Should().Contain(cell => cell.Row == 4 && cell.Col == 2);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        details.IsHidden.Should().BeTrue();
        session.HiddenSheets.Should().ContainSingle()
            .Which.Should().Be(new WorkbookHiddenSheet(details.Id, "Details"));
        session.ActiveSheet.Should().BeSameAs(summary);
        session.CanRedo.Should().BeTrue();

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        details.IsHidden.Should().BeFalse();
        session.HiddenSheets.Should().BeEmpty();
        session.ActiveSheet.Should().BeSameAs(summary);
    }

    [Fact]
    public void HideActiveSheet_RejectsOnlyUserVisibleSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var audit = workbook.AddSheet("Audit");
        audit.IsVeryHidden = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        session.CanHideActiveSheet.Should().BeFalse();

        var result = session.HideActiveSheet();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("visible");
        workbook.Sheets[0].IsHidden.Should().BeFalse();
        session.ActiveSheet.Should().BeSameAs(workbook.Sheets[0]);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void HideUnhideSheet_RejectProtectedWorkbookWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var details = workbook.AddSheet("Details");
        details.IsHidden = true;
        workbook.IsStructureProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var hide = session.HideActiveSheet();
        var unhide = session.UnhideSheet(details.Id);

        hide.Success.Should().BeFalse();
        unhide.Success.Should().BeFalse();
        hide.ErrorMessage.Should().Contain("protected");
        unhide.ErrorMessage.Should().Contain("protected");
        workbook.Sheets[0].IsHidden.Should().BeFalse();
        details.IsHidden.Should().BeTrue();
        session.ActiveSheet.Should().BeSameAs(workbook.Sheets[0]);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void UnhideSheet_RejectsVeryHiddenSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var audit = workbook.AddSheet("Audit");
        audit.IsHidden = true;
        audit.IsVeryHidden = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.UnhideSheet(audit.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Very hidden");
        audit.IsHidden.Should().BeTrue();
        session.HiddenSheets.Should().BeEmpty();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void DeleteActiveSheet_RemovesSelectsNextSheetAndKeepsUndoRedoCoherent()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var charts = workbook.AddSheet("Charts");
        details.SetCell(new CellAddress(details.Id, 2, 2), new TextValue("remove me"));
        charts.SetCell(new CellAddress(charts.Id, 3, 3), new TextValue("keep me"));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectSheet(details.Id);

        var result = session.DeleteActiveSheet();

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Sheet1", "Charts");
        session.ActiveSheet.Should().BeSameAs(charts);
        workbook.ActiveSheetIndex.Should().Be(1);
        session.ActiveCell.Should().Be(new CellAddress(charts.Id, 1, 1));
        session.SelectedRange.Should().Be(new GridRange(session.ActiveCell, session.ActiveCell));
        session.SheetTabs.Should().Equal(
            new WorkbookSheetTab(summary.Id, "Sheet1", IsActive: false),
            new WorkbookSheetTab(charts.Id, "Charts", IsActive: true));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Sheet1", "Details", "Charts");
        session.ActiveSheet.Should().BeSameAs(details);
        details.GetValue(new CellAddress(details.Id, 2, 2)).Should().Be(new TextValue("remove me"));
        session.CanRedo.Should().BeTrue();

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Sheet1", "Charts");
        session.ActiveSheet.Should().BeSameAs(charts);
        charts.GetValue(new CellAddress(charts.Id, 3, 3)).Should().Be(new TextValue("keep me"));
        workbook.ActiveSheetIndex.Should().Be(1);
    }

    [Fact]
    public void DeleteActiveSheet_RejectsOnlySheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.DeleteActiveSheet();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("only sheet");
        workbook.Sheets.Should().ContainSingle().Which.Name.Should().Be("Sheet1");
        session.ActiveSheet.Name.Should().Be("Sheet1");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SheetLifecycleCommands_RejectProtectedWorkbookWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        workbook.IsStructureProtected = true;
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var add = session.AddSheet();
        var duplicate = session.DuplicateActiveSheet();
        var delete = session.DeleteActiveSheet();
        var rename = session.RenameActiveSheet("Data");

        add.Success.Should().BeFalse();
        duplicate.Success.Should().BeFalse();
        delete.Success.Should().BeFalse();
        rename.Success.Should().BeFalse();
        add.ErrorMessage.Should().Contain("protected");
        duplicate.ErrorMessage.Should().Contain("protected");
        delete.ErrorMessage.Should().Contain("protected");
        rename.ErrorMessage.Should().Contain("protected");
        workbook.Sheets.Should().ContainSingle().Which.Name.Should().Be("Sheet1");
        session.ActiveSheet.Name.Should().Be("Sheet1");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void MoveActiveCell_PansViewportWhenSelectionMovesPastVisibleRange()
    {
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(CreateWorkbook(), "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 60,
            viewportWidth: 160);
        var initialLastRow = session.Viewport.RowMetrics[^1].Row;
        var initialLastCol = session.Viewport.ColMetrics[^1].Col;

        session.MoveActiveCell((int)initialLastRow, (int)initialLastCol);

        session.ActiveCell.Row.Should().Be(initialLastRow + 1);
        session.ActiveCell.Col.Should().Be(initialLastCol + 1);
        session.ActiveSheet.ViewTopRow.Should().Be(2);
        session.ActiveSheet.ViewLeftCol.Should().Be(2);
        session.Viewport.RowMetrics.Select(row => row.Row).Should().Contain(session.ActiveCell.Row);
        session.Viewport.ColMetrics.Select(col => col.Col).Should().Contain(session.ActiveCell.Col);
    }

    [Fact]
    public void PanViewport_UpdatesViewOriginAndRefreshesViewport()
    {
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(CreateWorkbook(), "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 60,
            viewportWidth: 160);

        var changed = session.PanViewport(rowDelta: 2, colDelta: 3);

        changed.Should().BeTrue();
        session.ActiveSheet.ViewTopRow.Should().Be(3);
        session.ActiveSheet.ViewLeftCol.Should().Be(4);
        session.Viewport.RowMetrics.Select(row => row.Row).Should().StartWith([3u]);
        session.Viewport.ColMetrics.Select(col => col.Col).Should().StartWith([4u]);
    }

    [Fact]
    public void PanViewport_ClampsAtWorksheetEdges()
    {
        var session = CreateSession(new StartupWorkbookLoadResult(
            CreateWorkbook(),
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        session.PanViewport(rowDelta: -10, colDelta: -10).Should().BeFalse();
        session.ActiveSheet.ViewTopRow.Should().BeNull();
        session.ActiveSheet.ViewLeftCol.Should().BeNull();

        session.SetViewportOrigin(CellAddress.MaxRow, CellAddress.MaxCol).Should().BeTrue();
        session.PanViewport(rowDelta: 10, colDelta: 10).Should().BeFalse();
        session.ActiveSheet.ViewTopRow.Should().Be(CellAddress.MaxRow);
        session.ActiveSheet.ViewLeftCol.Should().Be(CellAddress.MaxCol);
    }

    [Fact]
    public void PanViewport_AccountsForFrozenPanes()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 1;
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 60,
            viewportWidth: 160);

        session.SetViewportOrigin(5, 5).Should().BeTrue();
        session.SetViewportOrigin(1, 1).Should().BeTrue();

        session.ActiveSheet.ViewTopRow.Should().Be(2);
        session.ActiveSheet.ViewLeftCol.Should().Be(2);
        session.Viewport.RowMetrics.Select(row => row.Row).Should().StartWith([1u, 2u]);
        session.Viewport.ColMetrics.Select(col => col.Col).Should().StartWith([1u, 2u]);
    }

    [Fact]
    public void SelectCell_PansViewportToDistantSelection()
    {
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(CreateWorkbook(), "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 60,
            viewportWidth: 160);
        var target = new CellAddress(session.ActiveSheet.Id, 25, 8);

        session.SelectCell(target);

        session.ActiveCell.Should().Be(target);
        session.SelectedRange.Should().Be(new GridRange(target, target));
        session.ActiveSheet.ActiveRow.Should().Be(25);
        session.ActiveSheet.ActiveCol.Should().Be(8);
        session.Viewport.RowMetrics.Select(row => row.Row).Should().Contain(25);
        session.Viewport.ColMetrics.Select(col => col.Col).Should().Contain(8);
    }

    [Fact]
    public void MoveActiveCell_ClampsAtWorksheetEdges()
    {
        var session = CreateSession(new StartupWorkbookLoadResult(
            CreateWorkbook(),
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        session.MoveActiveCell(-10, -10);

        session.ActiveCell.Should().Be(new CellAddress(session.ActiveSheet.Id, 1, 1));
    }

    [Fact]
    public void UpdateViewportSize_RebuildsViewportWithNewDimensions()
    {
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(CreateWorkbook(), "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 60,
            viewportWidth: 160);
        var initialRows = session.Viewport.RowMetrics.Count;
        var initialColumns = session.Viewport.ColMetrics.Count;

        var changed = session.UpdateViewportSize(viewportHeight: 140.2, viewportWidth: 280.2);

        changed.Should().BeTrue();
        session.ViewportHeight.Should().Be(141);
        session.ViewportWidth.Should().Be(281);
        session.Viewport.RowMetrics.Count.Should().BeGreaterThan(initialRows);
        session.Viewport.ColMetrics.Count.Should().BeGreaterThan(initialColumns);
    }

    [Fact]
    public void UpdateViewportSize_IgnoresInvalidDimensions()
    {
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(CreateWorkbook(), "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 60,
            viewportWidth: 160);

        var changed = session.UpdateViewportSize(double.NaN, double.NegativeInfinity);

        changed.Should().BeFalse();
        session.ViewportHeight.Should().Be(60);
        session.ViewportWidth.Should().Be(160);
    }

    [Fact]
    public void UpdateViewportSize_ReturnsFalseForSameDimensions()
    {
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(CreateWorkbook(), "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 60,
            viewportWidth: 160);

        session.UpdateViewportSize(viewportHeight: 60, viewportWidth: 160).Should().BeFalse();
    }

    [Fact]
    public void UpdateViewportSize_KeepsActiveCellVisibleAfterShrink()
    {
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(CreateWorkbook(), "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 480);
        var target = new CellAddress(
            session.ActiveSheet.Id,
            session.Viewport.RowMetrics[^1].Row,
            session.Viewport.ColMetrics[^1].Col);
        session.SelectCell(target);

        session.UpdateViewportSize(viewportHeight: 60, viewportWidth: 160).Should().BeTrue();

        session.ActiveCell.Should().Be(target);
        session.Viewport.RowMetrics.Select(row => row.Row).Should().Contain(target.Row);
        session.Viewport.ColMetrics.Select(col => col.Col).Should().Contain(target.Col);
    }

    [Fact]
    public void MarkSaved_UpdatesDisplayNameAndClearsDirtyFeatureReport()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), "Book.xlsx");
        var savedPath = Path.Combine(Path.GetTempPath(), "Saved.fxl");
        var sourceIdentity = new WorkbookFileAccessIdentity(
            sourcePath,
            "macos-security-scoped-bookmark",
            "source-token");
        var session = CreateSession(new StartupWorkbookLoadResult(
            CreateWorkbook(),
            "Book.xlsx",
            "Opened .xlsx.",
            IsFallback: false,
            SourcePath: sourcePath,
            FeatureReport: new XlsxFeatureReport(
            [
                new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Charts, "xl/charts/chart1.xml")
            ]),
            SourceFileAccessIdentity: sourceIdentity));
        session.SelectCell(session.ActiveCell);
        session.CommitCellText("changed");

        session.MarkSaved(savedPath);

        session.IsDirty.Should().BeFalse();
        session.CurrentFilePath.Should().Be(savedPath);
        session.CurrentFileAccessIdentity.Should().NotBeNull();
        session.CurrentFileAccessIdentity!.LocalPath.Should().Be(savedPath);
        session.CurrentFileAccessIdentity.HasBookmark.Should().BeFalse();
        session.CurrentXlsxFeatureReport.Should().BeNull();
        session.DisplayName.Should().Be("Saved.fxl");
        session.Workbook.Name.Should().Be("Saved.fxl");
    }

    [Fact]
    public void MarkSaved_PreservesCurrentFileAccessIdentityWhenSavingSamePath()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), "Book.fxl");
        var sourceIdentity = new WorkbookFileAccessIdentity(
            sourcePath,
            "macos-security-scoped-bookmark",
            "same-path-token");
        var session = CreateSession(new StartupWorkbookLoadResult(
            CreateWorkbook(),
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false,
            SourcePath: sourcePath,
            SourceFileAccessIdentity: sourceIdentity));

        session.MarkSaved(sourcePath);

        session.CurrentFilePath.Should().Be(sourcePath);
        session.CurrentFileAccessIdentity.Should().NotBeNull();
        session.CurrentFileAccessIdentity!.LocalPath.Should().Be(sourcePath);
        session.CurrentFileAccessIdentity.BookmarkKind.Should().Be("macos-security-scoped-bookmark");
        session.CurrentFileAccessIdentity.BookmarkPayload.Should().Be("same-path-token");
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

    private static (WorkbookSession Session, Sheet Sheet, CellAddress A1, CellAddress C1)
        CreateSessionWithMultipleSelectedRanges()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(42));
        sheet.SetCell(b1, new TextValue("text"));
        sheet.SetCell(c1, new BoolValue(true));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        session.SelectRange(new GridRange(a1, c1));
        session.GoToSpecial(
            GoToSpecialKind.Constants,
            new GoToSpecialOptions(GoToSpecialValueTypes.Numbers | GoToSpecialValueTypes.Logicals));
        session.SelectedRanges.Should().HaveCount(2);
        return (session, sheet, a1, c1);
    }

    private static CellStyle GetStyle(Workbook workbook, Sheet sheet, CellAddress address)
    {
        var styleId = sheet.GetCell(address)?.StyleId ??
            sheet.GetStyleOnly(address.Row, address.Col) ??
            StyleId.Default;
        return workbook.GetStyle(styleId);
    }

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
