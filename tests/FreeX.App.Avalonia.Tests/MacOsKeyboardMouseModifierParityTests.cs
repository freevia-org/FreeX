using Free.Shared.Shell;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;
using FreeX.App.Presentation.FormulaBar;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// macOS modifier mapping for editing keys and mouse gestures: the WPF host keys these on Ctrl, and
/// on a Mac the command modifier (Cmd) or Option takes that role.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class MacOsKeyboardMouseModifierParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(KeyModifiers.Control, true, FreeKeyModifiers.Control)]
    [InlineData(KeyModifiers.Meta, true, FreeKeyModifiers.Control)]
    [InlineData(KeyModifiers.Meta | KeyModifiers.Shift, true, FreeKeyModifiers.Control | FreeKeyModifiers.Shift)]
    [InlineData(KeyModifiers.Meta, false, FreeKeyModifiers.Meta)]
    [InlineData(KeyModifiers.Control, false, FreeKeyModifiers.Control)]
    public void FreeKeyModifiers_ProjectCommandModifierOnlyOnMac(
        KeyModifiers modifiers,
        bool metaIsCommand,
        FreeKeyModifiers expected)
    {
        FormulaBarAvaloniaInputAdapter.ToFormulaEditorModifiers(modifiers, metaIsCommand).Should().Be(expected);
    }

    [Fact]
    public void CellDragCopyAndWheelZoomModifiers_FollowThePlatform()
    {
        MainWindow.IsCellDragCopyModifier(KeyModifiers.Control).Should().BeTrue();
        MainWindow.IsCellDragCopyModifier(KeyModifiers.None).Should().BeFalse();
        MainWindow.IsCellDragCopyModifier(KeyModifiers.Shift).Should().BeFalse();
        MainWindow.IsWheelZoomModifier(KeyModifiers.Control).Should().BeTrue();
        MainWindow.IsWheelZoomModifier(KeyModifiers.None).Should().BeFalse();

        MainWindow.IsCellDragCopyModifier(KeyModifiers.Alt).Should().Be(OperatingSystem.IsMacOS());
        MainWindow.IsWheelZoomModifier(KeyModifiers.Meta).Should().Be(OperatingSystem.IsMacOS());
    }

    [Fact]
    public void CommandT_CreatesTableOnMacOnly()
    {
        MainWindow.TryResolveApplicationShortcutForTest(Key.T, KeyModifiers.Control, out var ctrl).Should().BeTrue();
        ctrl.Should().Be(FreeX.App.Presentation.Shell.KeyboardCommandShortcut.CreateTable);
        MainWindow.TryResolveApplicationShortcutForTest(Key.T, KeyModifiers.Meta, out var cmd)
            .Should().Be(OperatingSystem.IsMacOS());
        if (OperatingSystem.IsMacOS())
            cmd.Should().Be(FreeX.App.Presentation.Shell.KeyboardCommandShortcut.CreateTable);
    }

    [Fact]
    public async Task CommandEnterInCellEditor_FillsTheSelectionLikeCtrlEnter()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CmdEnter");
            window.Session.SelectSheet(sheet.Id);
            try
            {
                var a1 = new CellAddress(sheet.Id, 1, 1);
                var b2 = new CellAddress(sheet.Id, 2, 2);
                window.Session.SelectRange(new GridRange(a1, b2));
                window.BeginInlineCellEditForTest(a1, "7", 1);

                var args = new KeyEventArgs { Key = Key.Enter, KeyModifiers = KeyModifiers.Meta };
                window.RaiseInlineCellEditorKeyDownForTest(args);

                args.Handled.Should().BeTrue();
                foreach (var cell in new GridRange(a1, b2).AllCells())
                    sheet.GetValue(cell).Should().Be(new NumberValue(7), cell.ToString());
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CommandWheel_ZoomsTheWorksheetLikeCtrlWheel()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var before = window.Session.ZoomPercent;
                var pointer = new Pointer(1, PointerType.Mouse, isPrimary: true);
                var args = new PointerWheelEventArgs(
                    window,
                    pointer,
                    window.SheetGridHostForTest,
                    new Point(10, 10),
                    0,
                    new PointerPointProperties(),
                    KeyModifiers.Meta,
                    new Vector(0, 1));

                window.RaisePointerWheelChangedForTest(args);

                args.Handled.Should().BeTrue();
                window.Session.ZoomPercent.Should().BeGreaterThan(before);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }
}
