using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using FreeX.Core.Model;
using FreeX.App.Presentation;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R68-app-selection-navigation-6-1: the Avalonia worksheet's plain cell-click handler ignored F8
/// (Extend Selection mode) entirely -- a plain click always collapsed the selection to the clicked
/// cell, even with F8 armed, unlike the keyboard path (<c>TryHandleStickySelectionNavigation</c>),
/// which treats Extend mode as an implicit Shift on arrow-key navigation. The fix (extracted into
/// the internal <see cref="MainWindow.SelectClickedCell"/> helper the real click handler now calls)
/// makes a plain click behave like a Shift+click while Extend mode is active.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R68_F8ExtendSelectionClickTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task PlainClick_WhileF8ExtendModeActive_ExtendsSelectionFromTheAnchor()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("F8ExtendClickFixture");
                window.Session.SelectSheet(sheet.Id);

                var anchor = new CellAddress(sheet.Id, 2, 2);
                var clicked = new CellAddress(sheet.Id, 4, 4);
                window.Session.SelectCell(anchor);

                ArmF8ExtendMode(window);
                window.KeyboardSelectionModeForTest.Should().Be(ExcelSelectionMode.Extend, "F8 must arm Extend mode");

                window.SelectClickedCell(clicked, KeyModifiers.None);

                window.Session.SelectedRange.Should().Be(new GridRange(anchor, clicked),
                    "a plain click while F8 Extend mode is active must extend the selection from the anchor to the clicked cell, not collapse it");
                window.Session.ActiveCell.Should().Be(anchor,
                    "extending keeps the ORIGINAL cell as the anchor (matching SelectRange/Shift-click semantics)");
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
    public async Task PlainClick_WithoutF8_NoRegression_StillCollapsesSelection()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("PlainClickNoRegressionFixture");
                window.Session.SelectSheet(sheet.Id);

                var anchor = new CellAddress(sheet.Id, 2, 2);
                var clicked = new CellAddress(sheet.Id, 4, 4);
                window.Session.SelectCell(anchor);

                window.KeyboardSelectionModeForTest.Should().Be(ExcelSelectionMode.Normal);
                window.SelectClickedCell(clicked, KeyModifiers.None);

                window.Session.SelectedRange.Should().Be(new GridRange(clicked, clicked),
                    "without F8 (or Shift) a plain click must still collapse the selection to the clicked cell");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    /// <summary>Presses F8 through the real shortcut-routing path to arm keyboard Extend Selection mode.</summary>
    private static void ArmF8ExtendMode(MainWindow window)
    {
        var toggleMethod = typeof(MainWindow).GetMethod(
            "ToggleKeyboardSelectionMode", BindingFlags.Instance | BindingFlags.NonPublic)!;
        toggleMethod.Invoke(window, [ExcelWorksheetNavigationModifiers.None]);
    }
}
