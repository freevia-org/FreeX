using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using FreeX.App.Presentation;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaMainWindowKeyboardParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    public static IEnumerable<object[]> ApplicationCommandRoutes()
    {
        foreach (var shortcut in WorkbookKeyboardShortcutCatalog.ApplicationCommandShortcuts)
        {
            yield return Route(
                Enum.Parse<Key>(shortcut.Key.ToString()),
                ToAvaloniaModifiers(shortcut.Modifiers),
                shortcut.Command);
        }
    }

    [Theory]
    [MemberData(nameof(ApplicationCommandRoutes))]
    public void ApplicationMatcher_ProjectsEveryPortableChord(
        Key key,
        KeyModifiers modifiers,
        KeyboardCommandShortcut expected)
    {
        MainWindow.TryResolveApplicationShortcutForTest(key, modifiers, out var actual)
            .Should().BeTrue();
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData(Key.Add, KeyModifiers.Control | KeyModifiers.Alt, KeyboardCommandShortcut.ZoomIn)]
    [InlineData(Key.Subtract, KeyModifiers.Control | KeyModifiers.Alt, KeyboardCommandShortcut.ZoomOut)]
    [InlineData(Key.Decimal, KeyModifiers.Control, KeyboardCommandShortcut.CycleSelectionCorner)]
    public void ApplicationMatcher_PreservesNativeKeyAliases(
        Key key,
        KeyModifiers modifiers,
        KeyboardCommandShortcut expected)
    {
        MainWindow.TryResolveApplicationShortcutForTest(key, modifiers, out var actual)
            .Should().BeTrue();
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData(Key.T, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(Key.F7, KeyModifiers.Shift)]
    [InlineData(Key.F11, KeyModifiers.Control)]
    [InlineData(Key.OemOpenBrackets, KeyModifiers.Alt)]
    [InlineData(Key.Back, KeyModifiers.Alt)]
    public void ApplicationMatcher_RejectsExtraOrWrongModifiers(Key key, KeyModifiers modifiers)
    {
        MainWindow.TryResolveApplicationShortcutForTest(key, modifiers, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(Key.D, KeyModifiers.Meta, 2, 1)]
    [InlineData(Key.R, KeyModifiers.Meta, 1, 2)]
    [InlineData(Key.D, KeyModifiers.Control, 2, 1)]
    [InlineData(Key.R, KeyModifiers.Control, 1, 2)]
    public async Task FillShortcut_CopiesTheSourceToTheSelectedDestination(
        Key key,
        KeyModifiers modifiers,
        int row,
        int column)
    {
        await Run(async (window, sheet) =>
        {
            var source = new CellAddress(sheet.Id, 1, 1);
            var destination = new CellAddress(sheet.Id, (uint)row, (uint)column);
            sheet.SetCell(source, new TextValue("Fill source"));
            window.Session.SelectRange(new GridRange(source, destination));

            await Press(window, key, modifiers);

            sheet.GetValue(destination).Should().Be(new TextValue("Fill source"));
        });
    }

    [Theory]
    [InlineData(KeyModifiers.Meta)]
    [InlineData(KeyModifiers.Control)]
    public async Task InsertHyperlinkShortcut_OpensTheWorkbookHyperlinkDialog(KeyModifiers modifiers)
    {
        await Run(async (window, _) =>
        {
            window.Show();
            var shortcut = Press(window, Key.K, modifiers);
            try
            {
                var dialog = window.OwnedWindows.OfType<Window>().Should().ContainSingle().Which;
                dialog.Title.Should().Be(UiText.Get("Hyperlink_InsertHyperlink"));
            }
            finally
            {
                foreach (var dialog in window.OwnedWindows.ToArray())
                    dialog.Close();
            }

            await shortcut;
        });
    }

    [Fact]
    public async Task DateTimeAndOutlineShortcuts_ExecuteThroughWorksheetSession()
    {
        await Run(async (window, sheet) =>
        {
            var dateAddress = new CellAddress(sheet.Id, 1, 1);
            window.Session.SelectCell(dateAddress);
            await Press(window, Key.OemSemicolon, KeyModifiers.Control);
            var date = sheet.GetValue(dateAddress).Should().BeOfType<NumberValue>().Subject;
            new DateTimeValue(date.Value).ToDateTime().Date.Should().Be(DateTime.Today);
            var dateCell = sheet.GetCell(dateAddress)!;
            window.Session.Workbook.GetStyle(dateCell.StyleId).NumberFormat.Should().Be(DateTimeEntryService.CurrentDateNumberFormat);
            window.FormulaBoxTextForTest.Should().Be(DateTime.Today.ToString("d", CultureInfo.CurrentCulture));
            window.Session.UndoLastEdit().Success.Should().BeTrue();
            sheet.GetValue(dateAddress).Should().Be(BlankValue.Instance);
            window.Session.RedoLastEdit().Success.Should().BeTrue();
            sheet.GetValue(dateAddress).Should().Be(date);

            var timeAddress = new CellAddress(sheet.Id, 2, 1);
            window.Session.SelectCell(timeAddress);
            await Press(window, Key.OemSemicolon, KeyModifiers.Control | KeyModifiers.Shift);
            var time = sheet.GetValue(timeAddress).Should().BeOfType<DateTimeValue>().Subject;
            time.Value.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(1);

            sheet.ShowOutlineSymbols = true;
            await Press(window, Key.D8, KeyModifiers.Control);
            sheet.ShowOutlineSymbols.Should().BeFalse();
            await Press(window, Key.D8, KeyModifiers.Control);
            sheet.ShowOutlineSymbols.Should().BeTrue();
        });
    }

    [Fact]
    public async Task CalculationFormulaBarAndZoomShortcuts_ExecuteRealHandlers()
    {
        var savedOptions = new AppOptions();
        var optionsSession = new FreeXOptionsRuntimeSession(
            load: () => savedOptions.Clone(),
            save: options =>
            {
                savedOptions.CopyFrom(options);
                return true;
            });
        await Run(async (window, sheet) =>
        {
            var valueAddress = new CellAddress(sheet.Id, 1, 1);
            var formulaAddress = new CellAddress(sheet.Id, 1, 2);
            sheet.SetCell(valueAddress, new NumberValue(4));
            sheet.SetFormula(formulaAddress, "A1*3");

            await Press(window, Key.F9, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift);
            sheet.GetValue(formulaAddress).Should().Be(new NumberValue(12));

            window.FormulaBarExpandedForTest.Should().BeFalse();
            await Press(window, Key.U, KeyModifiers.Control | KeyModifiers.Shift);
            window.FormulaBarExpandedForTest.Should().BeTrue();
            savedOptions.FormulaBarExpanded.Should().BeTrue();

            var zoom = window.Session.ZoomPercent;
            await Press(window, Key.OemPlus, KeyModifiers.Control | KeyModifiers.Alt);
            window.Session.ZoomPercent.Should().BeGreaterThan(zoom);
            await Press(window, Key.OemMinus, KeyModifiers.Control | KeyModifiers.Alt);
            window.Session.ZoomPercent.Should().Be(zoom);

            // Ctrl+F2 is Excel's Print Preview shortcut, so it must not start a formula-bar edit.
            window.Session.SelectCell(formulaAddress);
            await Press(window, Key.F2, KeyModifiers.Control);
            window.Session.FormulaEditAddress.Should().BeNull();
        }, optionsSession);
    }

    [Fact]
    public async Task CopyAboveBackspaceAndScrollShortcuts_PreserveExcelIntent()
    {
        await Run(async (window, sheet) =>
        {
            var source = new CellAddress(sheet.Id, 1, 1);
            sheet.SetFormula(source, "1+1");
            window.Session.RecalculateWorkbook();

            var formulaTarget = new CellAddress(sheet.Id, 2, 1);
            window.Session.SelectCell(formulaTarget);
            await Press(window, Key.OemQuotes, KeyModifiers.Control);
            sheet.GetCell(formulaTarget)!.FormulaText.Should().Be("1+1");

            var valueTarget = new CellAddress(sheet.Id, 3, 1);
            window.Session.SelectCell(valueTarget);
            await Press(window, Key.OemQuotes, KeyModifiers.Control | KeyModifiers.Shift);
            sheet.GetCell(valueTarget)!.FormulaText.Should().BeNull();
            sheet.GetValue(valueTarget).Should().Be(new NumberValue(2));

            sheet.SetCell(valueTarget, new TextValue("clear me"));
            window.Session.SelectCell(valueTarget);
            await Press(window, Key.Back, KeyModifiers.None);
            sheet.GetValue(valueTarget).Should().Be(BlankValue.Instance);
            window.Session.FormulaEditAddress.Should().Be(valueTarget);
            window.Session.CancelFormulaEdit();

            var distant = new CellAddress(sheet.Id, 80, 25);
            window.Session.SelectCell(distant);
            window.Session.SetViewportOrigin(1, 1);
            await Press(window, Key.Back, KeyModifiers.Control);
            sheet.ViewTopRow.Should().Be(80);
            sheet.ViewLeftCol.Should().Be(25);
            window.Session.ActiveCell.Should().Be(distant);
        });
    }

    [Fact]
    public async Task FilterReapplyAndGroupUngroupShortcuts_UseExistingCommands()
    {
        await Run(async (window, sheet) =>
        {
            var filterRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 1));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Keep"));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Drop"));
            window.Session.SelectRange(filterRange);

            await Press(window, Key.L, KeyModifiers.Control | KeyModifiers.Shift);
            sheet.AutoFilter.Should().NotBeNull();
            window.Session.ExecuteReviewCommand(
                new FilterCommand(sheet.Id, filterRange, 0, ["Keep"])).Success.Should().BeTrue();
            sheet.FilterHiddenRows.Should().Contain(3);
            sheet.FilterHiddenRows.Clear();
            sheet.ValueFilterHiddenRows.Clear();

            await Press(window, Key.L, KeyModifiers.Control | KeyModifiers.Alt);
            sheet.FilterHiddenRows.Should().Contain(3);

            var groupRange = new GridRange(
                new CellAddress(sheet.Id, 5, 1),
                new CellAddress(sheet.Id, 7, 3));
            window.Session.SelectRange(groupRange);
            await Press(window, Key.Right, KeyModifiers.Alt | KeyModifiers.Shift);
            sheet.RowOutlineLevels.Should().ContainKeys(5, 6, 7);

            window.Session.SelectRange(groupRange);
            await Press(window, Key.Left, KeyModifiers.Alt | KeyModifiers.Shift);
            sheet.RowOutlineLevels.Should().NotContainKeys(5, 6, 7);
        });
    }

    [Fact]
    public async Task EmbeddedAndChartSheetShortcuts_CreateTheirExpectedChartHosts()
    {
        await Run(async (window, sheet) =>
        {
            var range = SeedChartData(sheet);
            window.Session.SelectRange(range);
            await Press(window, Key.F1, KeyModifiers.Alt);
            sheet.Charts.Should().ContainSingle();
        });

        await Run(async (window, sheet) =>
        {
            var range = SeedChartData(sheet);
            window.Session.SelectRange(range);
            var sheetCount = window.Session.Workbook.Sheets.Count;
            await Press(window, Key.F11, KeyModifiers.None);
            window.Session.Workbook.Sheets.Should().HaveCount(sheetCount + 1);
            window.Session.ActiveSheet.Name.Should().StartWith("Chart");
            window.Session.ActiveSheet.Charts.Should().ContainSingle();
        });
    }

    [Fact]
    public async Task FormulaAuditShortcuts_SelectDirectAndTransitiveReferences()
    {
        await Run(async (window, sheet) =>
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b1 = new CellAddress(sheet.Id, 1, 2);
            var c1 = new CellAddress(sheet.Id, 1, 3);
            var d1 = new CellAddress(sheet.Id, 1, 4);
            sheet.SetCell(a1, new NumberValue(1));
            sheet.SetFormula(b1, "A1");
            sheet.SetFormula(c1, "B1");
            sheet.SetFormula(d1, "C1");

            window.Session.SelectCell(c1);
            await Press(window, Key.OemOpenBrackets, KeyModifiers.Control);
            SelectedCells(window).Should().BeEquivalentTo([b1]);

            window.Session.SelectCell(c1);
            await Press(window, Key.OemOpenBrackets, KeyModifiers.Control | KeyModifiers.Shift);
            SelectedCells(window).Should().BeEquivalentTo([a1, b1]);

            window.Session.SelectCell(a1);
            await Press(window, Key.OemCloseBrackets, KeyModifiers.Control);
            SelectedCells(window).Should().BeEquivalentTo([b1]);

            window.Session.SelectCell(a1);
            await Press(window, Key.OemCloseBrackets, KeyModifiers.Control | KeyModifiers.Shift);
            SelectedCells(window).Should().BeEquivalentTo([b1, c1, d1]);
        });
    }

    [Fact]
    public async Task CycleSelectionCorner_KeepsRectangleAndMovesClockwise()
    {
        await Run(async (window, sheet) =>
        {
            var range = new GridRange(
                new CellAddress(sheet.Id, 2, 3),
                new CellAddress(sheet.Id, 5, 7));
            window.Session.SelectRange(range);
            var expected = new[]
            {
                new CellAddress(sheet.Id, 2, 7),
                new CellAddress(sheet.Id, 5, 7),
                new CellAddress(sheet.Id, 5, 3),
                new CellAddress(sheet.Id, 2, 3),
            };

            foreach (var corner in expected)
            {
                await Press(window, Key.OemPeriod, KeyModifiers.Control);
                window.Session.ActiveCell.Should().Be(corner);
                window.Session.SelectedRanges.Should().Contain(range);
                // The full rectangle must stay the primary selection (not collapse to the 1x1 corner):
                // Define Name, Insert Chart, Conditional Format and multi-cell command gating all read
                // the primary SelectedRange, so a collapsed value would silently shrink the operation.
                window.Session.SelectedRange.Should().Be(range);
                window.Session.SelectedRange.CellCount.Should().Be(range.CellCount);
            }
        });
    }

    [Fact]
    public async Task F8SelectionModes_UseIndependentStickyStatesForNavigationAndEscapeResets()
    {
        await Run(async (window, sheet) =>
        {
            var formulaOwner = new MainWindow([]);
            var formulaAddress = new CellAddress(formulaOwner.Session.ActiveSheet.Id, 1, 1);
            formulaOwner.Session.SelectCell(formulaAddress);
            formulaOwner.BeginFormulaPointModeEditForTest(formulaAddress, "=SUM(");

            try
            {
                window.KeyboardSelectionModeForTest.Should().Be(FreeX.App.Presentation.ExcelSelectionMode.Normal);
                var origin = new CellAddress(sheet.Id, 2, 2);
                window.Session.SelectCell(origin);

                await Press(window, Key.F8, KeyModifiers.None);
                window.KeyboardSelectionModeForTest.Should().Be(FreeX.App.Presentation.ExcelSelectionMode.Extend);
                await Press(window, Key.Right, KeyModifiers.None);
                window.Session.SelectedRange.Should().Be(new GridRange(
                    origin,
                    new CellAddress(sheet.Id, 2, 3)));
                await Press(window, Key.F8, KeyModifiers.None);
                window.KeyboardSelectionModeForTest.Should().Be(FreeX.App.Presentation.ExcelSelectionMode.Normal);

                window.Session.SelectCell(origin);
                await Press(window, Key.F8, KeyModifiers.Shift);
                window.KeyboardSelectionModeForTest.Should().Be(FreeX.App.Presentation.ExcelSelectionMode.Add);
                await Press(window, Key.Down, KeyModifiers.None);
                window.Session.SelectedRanges.Should().BeEquivalentTo([
                    new GridRange(origin, origin),
                    new GridRange(
                        new CellAddress(sheet.Id, 3, 2),
                        new CellAddress(sheet.Id, 3, 2)),
                ]);
                await Press(window, Key.Escape, KeyModifiers.None);
                window.KeyboardSelectionModeForTest.Should().Be(FreeX.App.Presentation.ExcelSelectionMode.Normal);
                formulaOwner.HasActiveFormulaPointMode.Should().BeTrue(
                    "Escape belongs to the local sticky selection mode before another workbook's formula edit");
            }
            finally
            {
                formulaOwner.AllowCloseWithoutDirtyPromptForParityCapture();
                formulaOwner.Close();
            }
        });
    }

    [Fact]
    public async Task ShiftF8_AddsAreasAndResizesOnlyTheLastArea()
    {
        await Run(async (window, sheet) =>
        {
            var b2 = new CellAddress(sheet.Id, 2, 2);
            var b3 = new CellAddress(sheet.Id, 3, 2);
            var a3 = new CellAddress(sheet.Id, 3, 1);
            var c3 = new CellAddress(sheet.Id, 3, 3);
            var originalArea = new GridRange(b2, b2);
            var additionalArea = new GridRange(b3, b3);
            window.Session.SelectCell(b2);

            await Press(window, Key.F8, KeyModifiers.Shift);
            await Press(window, Key.Down, KeyModifiers.None);
            AssertSelection(additionalArea, b3);

            await Press(window, Key.Left, KeyModifiers.Shift);
            AssertSelection(new GridRange(a3, b3), b3);

            await Press(window, Key.Right, KeyModifiers.Shift);
            AssertSelection(additionalArea, b3);

            await Press(window, Key.Right, KeyModifiers.Shift);
            var finalArea = new GridRange(b3, c3);
            AssertSelection(finalArea, b3);

            await Press(window, Key.Escape, KeyModifiers.None);
            window.KeyboardSelectionModeForTest.Should().Be(ExcelSelectionMode.Normal);
            AssertSelection(finalArea, b3);

            void AssertSelection(GridRange primary, CellAddress activeCell)
            {
                window.Session.SelectedRanges.Should().Equal(originalArea, primary);
                window.Session.SelectedRange.Should().Be(primary);
                window.Session.ActiveCell.Should().Be(activeCell);
            }
        });
    }

    [Theory]
    [InlineData(Key.Enter, KeyModifiers.None, 4u, 2u)]
    [InlineData(Key.Enter, KeyModifiers.Shift, 2u, 2u)]
    [InlineData(Key.Tab, KeyModifiers.None, 3u, 3u)]
    [InlineData(Key.Tab, KeyModifiers.Shift, 3u, 1u)]
    public async Task ShiftF8_EnterAndTabMoveFromPrimaryRangeAndLeaveModeArmed(
        Key key,
        KeyModifiers modifiers,
        uint expectedRow,
        uint expectedCol)
    {
        var options = new AppOptions { MoveSelectionAfterEnter = true, AfterEnterDirection = AppOptionsEnterDirection.Down };
        var optionsSession = new FreeXOptionsRuntimeSession(
            initialOptions: options,
            load: () => options.Clone(),
            save: _ => true);
        await Run(async (window, sheet) =>
        {
            var c2 = new CellAddress(sheet.Id, 2, 3);
            var b3 = new CellAddress(sheet.Id, 3, 2);
            var c3 = new CellAddress(sheet.Id, 3, 3);
            window.Session.SelectCell(c2);
            await Press(window, Key.F8, KeyModifiers.Shift);
            await Press(window, Key.Down, KeyModifiers.None);
            await Press(window, Key.Left, KeyModifiers.Shift);
            window.Session.SelectedRanges.Should().Equal(new GridRange(c2, c2), new GridRange(b3, c3));
            window.Session.ActiveCell.Should().Be(c3);

            await Press(window, key, modifiers);

            var expected = new CellAddress(sheet.Id, expectedRow, expectedCol);
            var expectedRange = new GridRange(expected, expected);
            window.Session.SelectedRanges.Should().Equal(expectedRange);
            window.Session.SelectedRange.Should().Be(expectedRange);
            window.Session.ActiveCell.Should().Be(expected);
            window.KeyboardSelectionModeForTest.Should().Be(ExcelSelectionMode.Add);
        }, optionsSession);
    }

    private static object[] Route(
        Key key,
        KeyModifiers modifiers,
        KeyboardCommandShortcut shortcut) => [key, modifiers, shortcut];

    private static KeyModifiers ToAvaloniaModifiers(WorkbookShortcutModifiers modifiers)
    {
        var result = KeyModifiers.None;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Control)) result |= KeyModifiers.Control;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Meta)) result |= KeyModifiers.Meta;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Alt)) result |= KeyModifiers.Alt;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Shift)) result |= KeyModifiers.Shift;
        return result;
    }

    private static async Task Run(
        Func<MainWindow, Sheet, Task> test,
        FreeXOptionsRuntimeSession? optionsSession = null)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([], null!, optionsSession);
            var sheet = window.Session.Workbook.AddSheet("KeyboardParity");
            window.Session.SelectSheet(sheet.Id);
            try
            {
                await test(window, sheet);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static async Task Press(MainWindow window, Key key, KeyModifiers modifiers)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = modifiers };
        await window.RaiseKeyDownForTest(args);
        args.Handled.Should().BeTrue($"{modifiers}+{key} should be consumed by MainWindow");
    }

    private static GridRange SeedChartData(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        return new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
    }

    private static IReadOnlyList<CellAddress> SelectedCells(MainWindow window) =>
        window.Session.SelectedRanges.SelectMany(range => range.AllCells()).Distinct().ToList();
}
