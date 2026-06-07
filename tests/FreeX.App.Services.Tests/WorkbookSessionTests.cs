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
    public void AddSheet_AppendsSelectsNewSheetAndKeepsUndoRedoCoherent()
    {
        var workbook = CreateWorkbook();
        var original = workbook.Sheets.Single();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.AddSheet();

        result.Success.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Sheet1", "Sheet2");
        session.ActiveSheet.Name.Should().Be("Sheet2");
        workbook.ActiveSheetIndex.Should().Be(1);
        session.ActiveCell.Should().Be(new CellAddress(session.ActiveSheet.Id, 1, 1));
        session.SelectedRange.Should().Be(new GridRange(session.ActiveCell, session.ActiveCell));
        session.SheetTabs.Should().Equal(
            new WorkbookSheetTab(original.Id, "Sheet1", IsActive: false),
            new WorkbookSheetTab(session.ActiveSheet.Id, "Sheet2", IsActive: true));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        workbook.Sheets.Should().ContainSingle().Which.Id.Should().Be(original.Id);
        session.ActiveSheet.Should().BeSameAs(original);
        session.ActiveCell.Should().Be(new CellAddress(original.Id, 1, 1));
        session.CanRedo.Should().BeTrue();

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Sheet1", "Sheet2");
        session.ActiveSheet.Name.Should().Be("Sheet2");
        workbook.ActiveSheetIndex.Should().Be(1);
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
            new WorkbookSheetTab(source.Id, "Sheet1", IsActive: false),
            new WorkbookSheetTab(copy.Id, "Sheet1 (2)", IsActive: true));

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
