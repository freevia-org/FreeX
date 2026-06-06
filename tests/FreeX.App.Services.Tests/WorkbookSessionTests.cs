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
