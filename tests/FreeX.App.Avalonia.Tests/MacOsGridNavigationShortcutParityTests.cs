using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>Cmd-key worksheet navigation/selection/edit chords must match the WPF host's Ctrl chords.</summary>
[Collection("AvaloniaHeadless")]
public sealed class MacOsGridNavigationShortcutParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(KeyModifiers.Control)]
    [InlineData(KeyModifiers.Meta)]
    public async Task CommandArrowJumpsAndCommandShiftArrowExtendsToDataEdge(KeyModifiers command)
    {
        await Run((window, sheet) =>
        {
            for (uint r = 1; r <= 4; r++)
                for (uint c = 1; c <= 3; c++)
                    sheet.SetCell(new CellAddress(sheet.Id, r, c), new NumberValue(r * 10 + c));
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

            Press(window, Key.Down, command).Result.Should().BeTrue();
            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 4, 1));

            Press(window, Key.Right, command | KeyModifiers.Shift).Result.Should().BeTrue();
            window.Session.SelectedRange.Should().Be(new GridRange(new CellAddress(sheet.Id, 4, 1), new CellAddress(sheet.Id, 4, 3)));

            Press(window, Key.Home, command).Result.Should().BeTrue();
            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 1, 1));
            Press(window, Key.End, command).Result.Should().BeTrue();
            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 4, 3));
        });
    }

    [Theory]
    [InlineData(KeyModifiers.Control)]
    [InlineData(KeyModifiers.Meta)]
    public async Task CommandUndoRedoChords(KeyModifiers command)
    {
        await Run((window, sheet) =>
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            window.Session.SelectCell(a1);
            window.BeginInlineCellEditForTest(a1, "5", 1);
            var enter = new KeyEventArgs { Key = Key.Enter };
            window.RaiseInlineCellEditorKeyDownForTest(enter);
            sheet.GetValue(a1).Should().Be(new NumberValue(5));

            window.Session.SelectCell(a1);
            Press(window, Key.Z, command).Result.Should().BeTrue();
            sheet.GetValue(a1).Should().NotBe(new NumberValue(5));
            var redo = command == KeyModifiers.Meta ? KeyModifiers.Meta | KeyModifiers.Shift : KeyModifiers.Control;
            var redoKey = command == KeyModifiers.Meta ? Key.Z : Key.Y;
            Press(window, redoKey, redo).Result.Should().BeTrue();
            sheet.GetValue(a1).Should().Be(new NumberValue(5));
        });
    }

    private static Task<bool> Press(MainWindow window, Key key, KeyModifiers modifiers)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = modifiers };
        return window.RaiseKeyDownForTest(args).ContinueWith(_ => args.Handled);
    }

    private static async Task Run(Action<MainWindow, Sheet> test)
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("MacNav");
            window.Session.SelectSheet(sheet.Id);
            try { test(window, sheet); }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }
}
