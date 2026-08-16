using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeX.App.Presentation.FormulaBar;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaWorksheetPhysicalEditingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task RawF2PerCharacterTextInputAndEnter_KeepInlineEditorAttachedAndCommitCompleteText()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                var address = window.Session.ActiveCell;
                window.ActiveCellBorderForTest.Should().NotBeNull();
                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();

                Press(window, Key.F2, PhysicalKey.F2);
                var editor = FindByAutomationId<TextBox>(window, "WorksheetInlineCellEditor");
                editor.IsFocused.Should().BeTrue("F2 must finish the physical focus handoff before X11 sends the first character");

                // X11 can deliver the first character to the newly focused TextBox and a later
                // packet through the worksheet while its input focus settles. Keep that ordering
                // intact here: the second path must append to the TextBox-owned caret, not the
                // selection snapshot captured before the first character.
                RaiseRawTextInput(editor, "X");
                await DrainInputAsync();
                editor.Text.Should().Be("X");
                editor.CaretIndex.Should().Be(1);

                var rawTarget = window.ActiveCellBorderForTest!;
                foreach (var character in "11InlineCommit")
                {
                    RaiseRawTextInput(rawTarget, character.ToString());
                    await DrainInputAsync();

                    FindByAutomationId<TextBox>(window, "WorksheetInlineCellEditor").Should().BeSameAs(editor);
                }

                editor.Text.Should().Be("X11InlineCommit");

                Press(window, Key.Enter, PhysicalKey.Enter);
                await DrainInputAsync();

                sheet.GetValue(address).Should().Be(new TextValue("X11InlineCommit"));
                window.InlineCellEditorTextForTest.Should().BeNull();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EnterEditContinuation_FocusesNextCellForASecondPhysicalF2Edit()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                var first = window.Session.ActiveCell;
                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
                Press(window, Key.F2, PhysicalKey.F2);
                var firstEditor = FindByAutomationId<TextBox>(window, "WorksheetInlineCellEditor");
                RaiseRawTextInput(firstEditor, "first");
                await DrainInputAsync();
                Press(window, Key.Enter, PhysicalKey.Enter);
                await DrainInputAsync();

                var next = new CellAddress(first.Sheet, first.Row + 1, first.Col);
                window.Session.ActiveCell.Should().Be(next);
                window.ActiveCellBorderForTest.Should().NotBeNull();
                window.ActiveCellBorderForTest!.IsFocused.Should().BeTrue(
                    "Enter must hand worksheet focus to the next active cell before the next physical F2");

                Press(window, Key.F2, PhysicalKey.F2);
                var secondEditor = FindByAutomationId<TextBox>(window, "WorksheetInlineCellEditor");
                RaiseRawTextInput(secondEditor, "second");
                await DrainInputAsync();
                Press(window, Key.Enter, PhysicalKey.Enter);
                await DrainInputAsync();

                sheet.GetValue(first).Should().Be(new TextValue("first"));
                sheet.GetValue(next).Should().Be(new TextValue("second"));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClickF2EditThenCtrlS_PersistsCellBeyondLoadedUsedRange()
    {
        await Session.Dispatch(async () =>
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                $"freex-avalonia-live-save-{Guid.NewGuid():N}.csv");
            MainWindow? window = null;
            try
            {
                File.WriteAllText(path, CreateCsvRows(11));
                using var input = File.OpenRead(path);
                var source = new StartupWorkbookLoadResult(
                    new CsvFileAdapter().Load(input),
                    Path.GetFileName(path),
                    "Opened CSV.",
                    IsFallback: false,
                    SourcePath: path);
                var session = new WorkbookSessionFactory().Create(
                    source,
                    viewportHeight: 240,
                    viewportWidth: 320);
                window = CreateShownWindow(session, out var sheet);

                // The actual session is loaded from a CSV whose last populated row is 11. The
                // second edit is the physical G12 scenario from the Linux probe. This drives the
                // real click/F2/TextInput/Enter/Ctrl+S path rather than calling the writer.
                var firstEdit = new CellAddress(sheet.Id, 8, 7);
                await EditCellThroughPhysicalPath(window, firstEdit, "first-save");
                await SaveThroughWindowHandler(window);

                var beyondUsedRange = new CellAddress(sheet.Id, 12, 7);
                FindByAutomationId<TextBox>(window, "FormulaBox").Focus().Should().BeTrue();
                window.SelectClickedCell(beyondUsedRange, KeyModifiers.None);
                await DrainInputAsync();
                window.SheetGridHostForTest.IsFocused.Should().BeTrue(
                    "a worksheet click must restore keyboard focus before F2 is dispatched");
                await EditActiveCellThroughPhysicalPath(window, "X11ContextClear");
                sheet.GetValue(beyondUsedRange).Should().Be(new TextValue("X11ContextClear"));

                await SaveThroughWindowHandler(window);

                using var stream = File.OpenRead(path);
                var savedWorkbook = new DelimitedTextFileAdapter(".csv", "CSV", ',').Load(stream);
                savedWorkbook.GetSheet(sheet.Name)!
                    .GetValue(beyondUsedRange)
                    .Should()
                    .Be(new TextValue("X11ContextClear"));
            }
            finally
            {
                if (window is not null)
                {
                    window.WorkbookSaveAsPickerOverrideForTest = null;
                    window.AllowCloseWithoutDirtyPromptForParityCapture();
                    window.Close();
                }
                File.Delete(path);
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task WorksheetSourcedTextInput_AfterInlineEditorClaimsFocus_UsesTheInlineCaretBoundary()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                var address = window.Session.ActiveCell;
                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
                Press(window, Key.F2, PhysicalKey.F2);
                await DrainInputAsync();

                var editor = FindByAutomationId<TextBox>(window, "WorksheetInlineCellEditor");
                editor.IsFocused.Should().BeTrue();
                RaiseRawTextInput(editor, "X");
                await DrainInputAsync();

                // X11 can report this packet from the worksheet after the editor has focused.
                // The packet must retain the inline editor's caret ownership and append.
                RaiseRawTextInput(window.ActiveCellBorderForTest!, "Y");
                await DrainInputAsync();

                editor.Text.Should().Be("XY");
                editor.CaretIndex.Should().Be(2);

                Press(window, Key.Enter, PhysicalKey.Enter);
                await DrainInputAsync();
                sheet.GetValue(address).Should().Be(new TextValue("XY"));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RawF2PerCharacterTextInputAndEscape_RestoresCommittedCellAndFormulaBar()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                var address = window.Session.ActiveCell;
                sheet.SetCell(address, new TextValue("Original"));
                Refresh(window);
                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();

                Press(window, Key.F2, PhysicalKey.F2);
                FindByAutomationId<TextBox>(window, "WorksheetInlineCellEditor").IsFocused.Should().BeTrue();

                var rawTarget = window.ActiveCellBorderForTest!;
                foreach (var character in "X11Cancel")
                {
                    rawTarget.Focus().Should().BeTrue();
                    RaiseRawTextInput(rawTarget, character.ToString());
                    await DrainInputAsync();
                }

                FindByAutomationId<TextBox>(window, "FormulaBox").Focus().Should().BeTrue();
                Press(window, Key.Escape, PhysicalKey.Escape);
                await DrainInputAsync();

                sheet.GetValue(address).Should().Be(new TextValue("Original"));
                window.FormulaBoxTextForTest.Should().Be("Original");
                window.InlineCellEditorTextForTest.Should().BeNull();
                window.Session.FormulaEditAddress.Should().BeNull();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RawInlinePointModeClick_InsertsReferenceAndCommitsFormula()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                var formulaAddress = window.Session.ActiveCell;
                window.ActiveCellBorderForTest!.Focus();
                Press(window, Key.F2, PhysicalKey.F2);
                await DrainInputAsync();

                var editor = FindByAutomationId<TextBox>(window, "WorksheetInlineCellEditor");
                var referenceOverlay = FindByAutomationId<Canvas>(window, "WorksheetFormulaReferenceOverlay");
                referenceOverlay.IsAttachedToVisualTree().Should().BeTrue();
                FormulaReferenceHighlights(window).Should().BeEmpty();
                RaiseRawTextInput(editor, "=");
                await DrainInputAsync();
                editor.Text.Should().Be("=");
                editor.CaretIndex.Should().Be(1);
                window.FormulaPointModeForTest.Should().BeTrue();

                ClickCell(window, "Cell_B2");
                await DrainInputAsync();

                FindByAutomationId<TextBox>(window, "WorksheetInlineCellEditor").Should().BeSameAs(editor);
                editor.IsFocused.Should().BeTrue();
                FindByAutomationId<Canvas>(window, "WorksheetFormulaReferenceOverlay").Should().BeSameAs(referenceOverlay);
                var referenceHighlight = FormulaReferenceHighlights(window).Should().ContainSingle().Which;
                referenceHighlight.IsAttachedToVisualTree().Should().BeTrue();
                // ISolidColorBrush, not the concrete SolidColorBrush: Avalonia hands back an
                // ImmutableSolidColorBrush here, which is not a SolidColorBrush. The colour is what this
                // assertion is about.
                referenceHighlight.BorderBrush.Should().BeAssignableTo<ISolidColorBrush>()
                    .Which.Color.Should().Be(Color.FromRgb(32, 112, 214));
                window.InlineCellEditorTextForTest.Should().Be("=B2");
                window.Session.FormulaEditAddress.Should().Be(formulaAddress);

                Press(window, Key.Enter, PhysicalKey.Enter);
                await DrainInputAsync();

                sheet.GetCell(formulaAddress)!.FormulaText.Should().Be("B2");
                FindByAutomationId<Canvas>(window, "WorksheetFormulaReferenceOverlay")
                    .IsAttachedToVisualTree().Should().BeTrue();
                FormulaReferenceHighlights(window).Should().BeEmpty();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RawInlinePointModeDrag_ReplacesAnchorAndKeepsEditorFocus()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out _);
            try
            {
                var formulaAddress = window.Session.ActiveCell;
                window.ActiveCellBorderForTest!.Focus();
                Press(window, Key.F2, PhysicalKey.F2);
                await DrainInputAsync();

                var editor = FindByAutomationId<TextBox>(window, "WorksheetInlineCellEditor");
                RaiseRawTextInput(editor, "=");
                await DrainInputAsync();

                DragCells(
                    window,
                    "Cell_B2",
                    "Cell_D4",
                    () =>
                    {
                        GetField<FormulaRangeEditingSession>(window, "_formulaRangeEditingSession")
                            .ClearReferenceSpan();
                    });
                await DrainInputAsync();

                window.InlineCellEditorTextForTest.Should().Be("=B2:D4");
                editor.IsFocused.Should().BeTrue();
                window.Session.SelectedRange.Should().Be(new GridRange(formulaAddress, formulaAddress));
                FormulaReferenceHighlights(window).Should().ContainSingle();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RawFormulaBarPointModeClick_AfterF2Toggle_InsertsReferenceAndCommitsFormula()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                var formulaAddress = window.Session.ActiveCell;
                var formulaBox = FindByAutomationId<TextBox>(window, "FormulaBox");
                formulaBox.Focus().Should().BeTrue();
                await DrainInputAsync();

                formulaBox.SelectAll();
                window.KeyTextInput("=");
                await DrainInputAsync();
                window.FormulaPointModeForTest.Should().BeTrue();

                Press(window, Key.F2, PhysicalKey.F2);
                window.FormulaPointModeForTest.Should().BeFalse();
                Press(window, Key.F2, PhysicalKey.F2);
                window.FormulaPointModeForTest.Should().BeTrue();

                ClickCell(window, "Cell_B2");
                await DrainInputAsync();

                formulaBox.Text.Should().Be("=B2");
                window.Session.FormulaEditAddress.Should().Be(formulaAddress);
                window.Session.SelectedRange.Should().Be(new GridRange(
                    new CellAddress(sheet.Id, 2, 2),
                    new CellAddress(sheet.Id, 2, 2)));
                window.CellAddressBoxTextForTest.Should().Be("B2");

                Press(window, Key.Enter, PhysicalKey.Enter);
                await DrainInputAsync();

                sheet.GetCell(formulaAddress)!.FormulaText.Should().Be("B2");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaBarPointMode_HeaderClicks_InsertWholeColumnAndWholeRowReferencesAndRoundTrip()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10)); // B2
                sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20)); // B3
                sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(30)); // C3

                var columnFormulaAddress = new CellAddress(sheet.Id, 10, 7); // G10
                window.BeginFormulaPointModeEditForTest(columnFormulaAddress, "=SUM()");
                window.FormulaPointModeForTest.Should().BeTrue();
                GetField<TextBox>(window, "_formulaBox").CaretIndex = "=SUM(".Length;
                InvokeHeaderSelection(window, "SelectEntireColumn", 2u, false);

                window.FormulaBoxTextForTest.Should().Be("=SUM(B:B)");
                window.Session.SelectedRange.Should().Be(new GridRange(
                    new CellAddress(sheet.Id, 1, 2),
                    new CellAddress(sheet.Id, CellAddress.MaxRow, 2)));
                CommitFormulaBar(window);
                sheet.GetCell(columnFormulaAddress)!.FormulaText.Should().Be("SUM(B:B)");
                sheet.GetValue(columnFormulaAddress).Should().Be(new NumberValue(30));

                var rowFormulaAddress = new CellAddress(sheet.Id, 11, 7); // G11
                window.BeginFormulaPointModeEditForTest(rowFormulaAddress, "=SUM()");
                window.FormulaPointModeForTest.Should().BeTrue();
                GetField<TextBox>(window, "_formulaBox").CaretIndex = "=SUM(".Length;
                InvokeHeaderSelection(window, "SelectEntireRow", 3u, false);

                window.FormulaBoxTextForTest.Should().Be("=SUM(3:3)");
                window.Session.SelectedRange.Should().Be(new GridRange(
                    new CellAddress(sheet.Id, 3, 1),
                    new CellAddress(sheet.Id, 3, CellAddress.MaxCol)));
                CommitFormulaBar(window);
                sheet.GetCell(rowFormulaAddress)!.FormulaText.Should().Be("SUM(3:3)");
                sheet.GetValue(rowFormulaAddress).Should().Be(new NumberValue(50));

                using var stream = new MemoryStream();
                new NativeJsonAdapter().Save(window.Session.Workbook, stream);
                stream.Position = 0;
                var reopened = new NativeJsonAdapter().Load(stream);
                var reopenedSheet = reopened.Sheets.Single(candidate => candidate.Name == sheet.Name);
                reopenedSheet.GetCell(new CellAddress(reopenedSheet.Id, 10, 7))!.FormulaText.Should().Be("SUM(B:B)");
                reopenedSheet.GetCell(new CellAddress(reopenedSheet.Id, 11, 7))!.FormulaText.Should().Be("SUM(3:3)");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaBarPointMode_SelectAllCorner_InsertsWholeGridReferenceAndKeepsEditing()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                var formulaAddress = new CellAddress(sheet.Id, 2, 2);
                window.BeginFormulaPointModeEditForTest(formulaAddress, "=SUM(");
                window.FormulaPointModeForTest.Should().BeTrue();
                typeof(MainWindow).GetMethod("SelectAllCells", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, null);

                window.FormulaBoxTextForTest.Should().Be("=SUM(A1:XFD1048576");
                GetField<TextBox>(window, "_formulaBox").IsFocused.Should().BeTrue();
                sheet.GetCell(formulaAddress)?.HasFormula.Should().BeFalse(
                    "whole-grid selection must not commit formula editing");
                window.Session.SelectedRange.Should().Be(new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, CellAddress.MaxRow, CellAddress.MaxCol)));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaReferenceAdornment_RemainsAttachedAndClearsOnCancel()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                var formulaAddress = window.Session.ActiveCell;
                var formulaBox = FindByAutomationId<TextBox>(window, "FormulaBox");
                var referenceOverlay = FindByAutomationId<Canvas>(window, "WorksheetFormulaReferenceOverlay");
                referenceOverlay.IsAttachedToVisualTree().Should().BeTrue();

                formulaBox.Focus().Should().BeTrue();
                await DrainInputAsync();
                formulaBox.SelectAll();
                window.KeyTextInput("=");
                await DrainInputAsync();
                ClickCell(window, "Cell_B2");
                await DrainInputAsync();

                FindByAutomationId<Canvas>(window, "WorksheetFormulaReferenceOverlay").Should().BeSameAs(referenceOverlay);
                FormulaReferenceHighlights(window).Should().ContainSingle()
                    .Which.IsAttachedToVisualTree().Should().BeTrue();

                Press(window, Key.Escape, PhysicalKey.Escape);
                await DrainInputAsync();

                FindByAutomationId<Canvas>(window, "WorksheetFormulaReferenceOverlay")
                    .IsAttachedToVisualTree().Should().BeTrue();
                FormulaReferenceHighlights(window).Should().BeEmpty();
                sheet.GetCell(formulaAddress)?.FormulaText.Should().BeNull();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    private static MainWindow CreateShownWindow(out Sheet sheet)
    {
        var window = new MainWindow([]);
        sheet = window.Session.Workbook.AddSheet("PhysicalEditingFixture");
        window.Session.SelectSheet(sheet.Id);
        window.Show();
        window.Measure(new Size(1120, 720));
        window.Arrange(new Rect(0, 0, 1120, 720));
        Refresh(window);
        return window;
    }

    private static MainWindow CreateShownWindow(WorkbookSession session, out Sheet sheet)
    {
        var window = new MainWindow([], session);
        sheet = window.Session.ActiveSheet;
        window.Show();
        window.Measure(new Size(1120, 720));
        window.Arrange(new Rect(0, 0, 1120, 720));
        Refresh(window);
        return window;
    }

    private static string CreateCsvRows(int count) =>
        string.Join("\r\n", Enumerable.Range(1, count).Select(row => $"r{row},c{row},v{row}")) + "\r\n";

    private static void Refresh(MainWindow window) =>
        typeof(MainWindow)
            .GetMethod("RefreshShell", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, ["Ready"]);

    private static void Press(
        MainWindow window,
        Key key,
        PhysicalKey physicalKey,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physicalKey, null);
        window.KeyRelease(key, modifiers, physicalKey, null);
    }

    private static async Task EditCellThroughPhysicalPath(
        MainWindow window,
        CellAddress address,
        string text)
    {
        ClickCell(window, $"Cell_{CellAddress.NumberToColumnName(address.Col)}{address.Row}");
        await DrainInputAsync();

        await EditActiveCellThroughPhysicalPath(window, text);
    }

    private static async Task EditActiveCellThroughPhysicalPath(MainWindow window, string text)
    {
        Press(window, Key.F2, PhysicalKey.F2);
        await DrainInputAsync();
        FindByAutomationId<TextBox>(window, "WorksheetInlineCellEditor")
            .Should()
            .NotBeNull("a worksheet click must leave F2 routed to the inline editor");

        Press(window, Key.A, PhysicalKey.A, RawInputModifiers.Control);
        Press(window, Key.Back, PhysicalKey.Backspace);
        window.KeyTextInput(text);
        await DrainInputAsync();

        Press(window, Key.Enter, PhysicalKey.Enter);
        await DrainInputAsync();
    }

    private static async Task SaveThroughWindowHandler(MainWindow window)
    {
        var args = new KeyEventArgs
        {
            Key = Key.S,
            KeyModifiers = KeyModifiers.Control,
            PhysicalKey = PhysicalKey.None,
        };
        await window.RaiseKeyDownForTest(args);
        args.Handled.Should().BeTrue();
        await DrainInputAsync();
    }

    private static void RaiseRawTextInput(InputElement target, string text) =>
        target.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Source = target,
            Text = text,
        });

    private static void ClickCell(MainWindow window, string automationId)
    {
        var cell = FindByAutomationId<Border>(window, automationId);
        var translatedPoint = cell.TranslatePoint(
            new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2),
            window);
        translatedPoint.Should().NotBeNull();
        var point = translatedPoint!.Value;
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }

    private static void DragCells(
        MainWindow window,
        string startAutomationId,
        string endAutomationId,
        Action? afterPointerDown = null)
    {
        var start = FindByAutomationId<Border>(window, startAutomationId);
        var end = FindByAutomationId<Border>(window, endAutomationId);
        var startPoint = start.TranslatePoint(
            new Point(start.Bounds.Width / 2, start.Bounds.Height / 2),
            window);
        var endPoint = end.TranslatePoint(
            new Point(end.Bounds.Width / 2, end.Bounds.Height / 2),
            window);
        startPoint.Should().NotBeNull();
        endPoint.Should().NotBeNull();

        window.MouseMove(startPoint!.Value, RawInputModifiers.None);
        window.MouseDown(startPoint.Value, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        afterPointerDown?.Invoke();
        window.MouseMove(endPoint!.Value, RawInputModifiers.LeftMouseButton);
        window.MouseUp(endPoint.Value, MouseButton.Left, RawInputModifiers.None);
    }

    private static T GetField<T>(MainWindow window, string name) where T : class =>
        (T)typeof(MainWindow)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;

    private static T FindByAutomationId<T>(MainWindow window, string automationId)
        where T : Control =>
        window.GetVisualDescendants()
            .OfType<T>()
            .Single(control => AutomationProperties.GetAutomationId(control) == automationId);

    private static IReadOnlyList<Border> FormulaReferenceHighlights(MainWindow window) =>
        window.GetVisualDescendants()
            .OfType<Border>()
            .Where(control => AutomationProperties.GetAutomationId(control) == "WorksheetFormulaReferenceHighlight")
            .ToList();

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static void InvokeHeaderSelection(MainWindow window, string methodName, uint index, bool extend)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(window, [index, extend]);
    }

    private static void CommitFormulaBar(MainWindow window)
    {
        window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs
        {
            Key = Key.Enter,
            PhysicalKey = PhysicalKey.Enter,
        });
    }
}
