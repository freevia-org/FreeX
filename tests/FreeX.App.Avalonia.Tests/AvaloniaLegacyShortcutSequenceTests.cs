using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;

using FluentAssertions;

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaLegacyShortcutSequenceTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AltDataFilterSequence_SelectsDataAndTogglesOnlyAfterSecondF(bool standaloneAlt)
    {
        await Run(async (window, sheet) =>
        {
            var range = SeedFilterRange(sheet);
            window.Session.SelectRange(range);

            if (standaloneAlt)
            {
                await PressHandled(window, Key.LeftAlt);
                window.RibbonKeyTipsVisibleForTest.Should().BeTrue();
                await PressHandled(window, Key.D);
            }
            else
            {
                await PressHandled(window, Key.D, KeyModifiers.Alt);
            }

            window.LegacyDataFilterSequenceStateForTest.Should().Be(
                MainWindow.LegacyDataFilterSequenceState.AwaitingFirstFilterKey);
            sheet.AutoFilter.Should().BeNull();

            await PressHandled(window, Key.F);
            window.LegacyDataFilterSequenceStateForTest.Should().Be(
                MainWindow.LegacyDataFilterSequenceState.AwaitingSecondFilterKey);
            sheet.AutoFilter.Should().BeNull("the first F is the legacy Data > Filter prefix");

            await PressHandled(window, Key.F);
            window.LegacyDataFilterSequenceStateForTest.Should().Be(
                MainWindow.LegacyDataFilterSequenceState.None);
            window.RibbonKeyTipsVisibleForTest.Should().BeFalse();
            sheet.AutoFilter.Should().NotBeNull();

            await PressHandled(window, Key.D, KeyModifiers.Alt);
            await PressHandled(window, Key.F);
            await PressHandled(window, Key.F);
            sheet.AutoFilter.Should().BeNull("the legacy route toggles the existing AutoFilter off");
        });
    }

    [Fact]
    public async Task EscapeAndInvalidContinuation_ResetWithoutChangingFilter()
    {
        await Run(async (window, sheet) =>
        {
            window.Session.SelectRange(SeedFilterRange(sheet));

            await PressHandled(window, Key.D, KeyModifiers.Alt);
            await PressHandled(window, Key.F);
            await PressHandled(window, Key.Escape);
            window.LegacyDataFilterSequenceStateForTest.Should().Be(
                MainWindow.LegacyDataFilterSequenceState.None);
            sheet.AutoFilter.Should().BeNull();

            await PressHandled(window, Key.D, KeyModifiers.Alt);
            await PressHandled(window, Key.X);
            window.LegacyDataFilterSequenceStateForTest.Should().Be(
                MainWindow.LegacyDataFilterSequenceState.None);
            sheet.AutoFilter.Should().BeNull();
        });
    }

    [Fact]
    public async Task TextEditingAndBackstage_BlockSequenceWithoutConsumingTheKey()
    {
        await Run(async (window, sheet) =>
        {
            var address = new CellAddress(sheet.Id, 1, 1);
            window.BeginFormulaEditForTest(address, "=1");
            var editingStart = await Press(window, Key.D, KeyModifiers.Alt);
            editingStart.Handled.Should().BeFalse();
            window.LegacyDataFilterSequenceStateForTest.Should().Be(
                MainWindow.LegacyDataFilterSequenceState.None);
            window.Session.CancelFormulaEdit();

            await PressHandled(window, Key.D, KeyModifiers.Alt);
            window.BeginFormulaEditForTest(address, "=2");
            var editingContinuation = await Press(window, Key.F);
            editingContinuation.Handled.Should().BeFalse();
            window.LegacyDataFilterSequenceStateForTest.Should().Be(
                MainWindow.LegacyDataFilterSequenceState.None);
            window.Session.CancelFormulaEdit();

            window.ShowBackstageOverlayForTest();
            var backstageStart = await Press(window, Key.D, KeyModifiers.Alt);
            backstageStart.Handled.Should().BeFalse();
            window.LegacyDataFilterSequenceStateForTest.Should().Be(
                MainWindow.LegacyDataFilterSequenceState.None);
        });
    }

    [Fact]
    public async Task DirectToggleAndReapplyRoutes_CoexistWithAndResetPendingLegacySequence()
    {
        await Run(async (window, sheet) =>
        {
            var range = SeedFilterRange(sheet);
            window.Session.SelectRange(range);

            await PressHandled(window, Key.L, KeyModifiers.Control | KeyModifiers.Shift);
            sheet.AutoFilter.Should().NotBeNull();

            window.Session.ExecuteReviewCommand(
                new FilterCommand(sheet.Id, range, 0, ["Keep"])).Success.Should().BeTrue();
            sheet.FilterHiddenRows.Should().Contain(3);
            sheet.FilterHiddenRows.Clear();
            sheet.ValueFilterHiddenRows.Clear();

            await PressHandled(window, Key.D, KeyModifiers.Alt);
            await PressHandled(window, Key.L, KeyModifiers.Control | KeyModifiers.Alt);
            window.LegacyDataFilterSequenceStateForTest.Should().Be(
                MainWindow.LegacyDataFilterSequenceState.None);
            sheet.FilterHiddenRows.Should().Contain(3);
        });
    }

    [Fact]
    public async Task NestedRibbonKeytips_PersistPastTabSelectionAndResetAfterCommand()
    {
        await Run(async (window, _) =>
        {
            await PressHandled(window, Key.H, KeyModifiers.Alt);
            window.RibbonKeyTipInputForTest.Should().Be("H");

            await PressHandled(window, Key.B);
            window.RibbonKeyTipInputForTest.Should().Be("HB");
            await PressHandled(window, Key.S);
            window.RibbonKeyTipInputForTest.Should().Be("HBS");
            await PressHandled(window, Key.D);
            window.RibbonKeyTipInputForTest.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task FreshAltAndInvalidContinuation_ResetOnlyRibbonKeytipState()
    {
        await Run(async (window, _) =>
        {
            await PressHandled(window, Key.H, KeyModifiers.Alt);
            window.RibbonKeyTipInputForTest.Should().Be("H");

            await PressHandled(window, Key.LeftAlt);
            window.RibbonKeyTipInputForTest.Should().BeEmpty();
            window.RibbonKeyTipsVisibleForTest.Should().BeTrue();

            await PressHandled(window, Key.X);
            window.RibbonKeyTipInputForTest.Should().BeEmpty();
            window.RibbonKeyTipsVisibleForTest.Should().BeFalse();
            window.LegacyDataFilterSequenceStateForTest.Should().Be(
                MainWindow.LegacyDataFilterSequenceState.None);
        });
    }

    [Fact]
    public async Task RibbonMenuKeyTip_EscapeAndUnmatchedInputCloseLiveFlyout()
    {
        await Run(async (window, _) =>
        {
            window.Show();
            var borders = window.RibbonControlForTest!.GetLogicalDescendants()
                .OfType<Button>()
                .First(button => Equals(button.Tag, "Borders"));
            var flyout = borders.Flyout.Should().BeOfType<MenuFlyout>().Subject;

            await PressHandled(window, Key.H, KeyModifiers.Alt);
            await PressHandled(window, Key.B);
            flyout.IsOpen.Should().BeTrue();

            await PressHandled(window, Key.Escape);
            flyout.IsOpen.Should().BeFalse();
            window.RibbonKeyTipInputForTest.Should().BeEmpty();

            await PressHandled(window, Key.H, KeyModifiers.Alt);
            await PressHandled(window, Key.B);
            flyout.IsOpen.Should().BeTrue();
            await PressHandled(window, Key.X);
            flyout.IsOpen.Should().BeFalse();
            window.RibbonKeyTipInputForTest.Should().BeEmpty();
        });
    }

    private static async Task Run(Func<MainWindow, Sheet, Task> test)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("Legacy shortcuts");
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

    private static GridRange SeedFilterRange(Sheet sheet)
    {
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Drop"));
        return range;
    }

    private static async Task PressHandled(
        MainWindow window,
        Key key,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = await Press(window, key, modifiers);
        args.Handled.Should().BeTrue($"{modifiers}+{key} should be consumed");
    }

    private static async Task<KeyEventArgs> Press(
        MainWindow window,
        Key key,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = modifiers };
        await window.RaiseKeyDownForTest(args);
        return args;
    }
}
