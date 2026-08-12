using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using FreeX.App.Presentation.Shell;
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

    [Fact]
    public async Task DateTimeAndOutlineShortcuts_ExecuteThroughWorksheetSession()
    {
        await Run(async (window, sheet) =>
        {
            var dateAddress = new CellAddress(sheet.Id, 1, 1);
            window.Session.SelectCell(dateAddress);
            await Press(window, Key.OemSemicolon, KeyModifiers.Control);
            var date = sheet.GetValue(dateAddress).Should().BeOfType<DateTimeValue>().Subject;
            date.ToDateTime().Date.Should().Be(DateTime.Today);

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

            var zoom = window.Session.ZoomPercent;
            await Press(window, Key.OemPlus, KeyModifiers.Control | KeyModifiers.Alt);
            window.Session.ZoomPercent.Should().BeGreaterThan(zoom);
            await Press(window, Key.OemMinus, KeyModifiers.Control | KeyModifiers.Alt);
            window.Session.ZoomPercent.Should().Be(zoom);

            window.Session.SelectCell(formulaAddress);
            await Press(window, Key.F2, KeyModifiers.Control);
            window.Session.FormulaEditAddress.Should().Be(formulaAddress);
        });
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
        });
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

    private static async Task Run(Func<MainWindow, Sheet, Task> test)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
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
