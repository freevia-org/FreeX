using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;

using FluentAssertions;

using FreeX.App.Presentation.Backstage;
using FreeX.App.Services.Ribbon;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AltEditPasteSpecialSequence_RoutesToExistingWorkflow(bool standaloneAlt)
    {
        await Run(async (window, _) =>
        {
            var invocations = 0;
            window.PasteSpecialWorkflowOverrideForTest = () =>
            {
                invocations++;
                return Task.CompletedTask;
            };

            if (standaloneAlt)
            {
                await PressHandled(window, Key.LeftAlt);
                window.RibbonKeyTipsVisibleForTest.Should().BeTrue();
                await PressHandled(window, Key.E);
            }
            else
            {
                await PressHandled(window, Key.E, KeyModifiers.Alt);
            }

            window.LegacyEditPasteSpecialSequenceStateForTest.Should().Be(
                MainWindow.LegacyEditPasteSpecialSequenceState.AwaitingPasteSpecialKey);
            invocations.Should().Be(0);

            await PressHandled(window, Key.S);

            window.LegacyEditPasteSpecialSequenceStateForTest.Should().Be(
                MainWindow.LegacyEditPasteSpecialSequenceState.None);
            window.RibbonKeyTipInputForTest.Should().BeEmpty();
            window.RibbonKeyTipsVisibleForTest.Should().BeFalse();
            invocations.Should().Be(1);
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
    public async Task DataWhatIfKeytip_OpensRealGoalSeekWorkflowAndSettlesAllContinuationStates()
    {
        await Run(async (window, _) =>
        {
            window.Show();

            await PressHandled(window, Key.A, KeyModifiers.Alt);
            window.RibbonKeyTipInputForTest.Should().Be("A");
            var tabs = window.RibbonControlForTest.Should().BeOfType<TabControl>().Subject;
            ((TabItem)tabs.SelectedItem!).Tag.Should().Be("DataTab");
            await PressHandled(window, Key.W);
            window.RibbonKeyTipInputForTest.Should().Be("AW");

            var whatIfFlyouts = window.RibbonControlForTest!.GetLogicalDescendants()
                .OfType<Button>()
                .Where(button => Equals(button.Tag, "What-If Analysis"))
                .Select(button => button.Flyout)
                .OfType<MenuFlyout>()
                .ToArray();
            whatIfFlyouts.Should().NotBeEmpty();
            var flyout = whatIfFlyouts.First(candidate => candidate.IsOpen);
            flyout.Items.OfType<MenuItem>().Select(item => item.Header?.ToString())
                .Should().Equal("Goal Seek...", "Scenario Manager...", "Data Table...");

            await PressHandled(window, Key.G);
            var goalSeek = await WaitForOwnedWindow(window, "Goal Seek");
            goalSeek.Should().NotBeNull("the G continuation must invoke the existing Goal Seek workflow");
            goalSeek!.Close();
            window.RibbonKeyTipInputForTest.Should().BeEmpty();

            await PressHandled(window, Key.A, KeyModifiers.Alt);
            await PressHandled(window, Key.W);
            await PressHandled(window, Key.Escape);
            window.RibbonKeyTipInputForTest.Should().BeEmpty();
            flyout.IsOpen.Should().BeFalse();

            await PressHandled(window, Key.A, KeyModifiers.Alt);
            await PressHandled(window, Key.W);
            await PressHandled(window, Key.X);
            window.RibbonKeyTipInputForTest.Should().BeEmpty();
            flyout.IsOpen.Should().BeFalse();
        });
    }

    [Fact]
    public async Task DataWhatIfKeytip_IsExcludedFromFormulaEditingAndBackstage()
    {
        await Run(async (window, sheet) =>
        {
            window.BeginFormulaEditForTest(new CellAddress(sheet.Id, 1, 1), "=1");
            var editingStart = await Press(window, Key.A, KeyModifiers.Alt);
            editingStart.Handled.Should().BeFalse();
            window.RibbonKeyTipInputForTest.Should().BeEmpty();
            window.Session.CancelFormulaEdit();

            window.ShowBackstageOverlayForTest();
            var backstageStart = await Press(window, Key.A, KeyModifiers.Alt);
            backstageStart.Handled.Should().BeFalse();
            window.RibbonKeyTipInputForTest.Should().BeEmpty();
            window.IsBackstageOverlayVisibleForTest.Should().BeTrue();
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

    [Fact]
    public async Task BackstageKeytipsActivateRenderedPaneAndKeepOverlayVisible()
    {
        await Run(async (window, _) =>
        {
            window.Show();

            await PressHandled(window, Key.F, KeyModifiers.Alt);
            window.IsBackstageOverlayVisibleForTest.Should().BeTrue();
            window.RibbonKeyTipInputForTest.Should().Be("F");

            var infoButton = window.BackstagePaneButtonForTest(FreeXBackstagePaneId.Info);
            infoButton.Should().NotBeNull();
            infoButton!.IsVisible.Should().BeTrue();
            infoButton.IsEffectivelyEnabled.Should().BeTrue();

            await PressHandled(window, Key.I);

            window.IsBackstageOverlayVisibleForTest.Should().BeTrue();
            window.ActiveBackstagePaneForTest.Should().Be(FreeXBackstagePaneId.Info);
            window.RibbonKeyTipInputForTest.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task CtrlP_EntersBackstagePrintPaneBeforeChoosingPreviewOrPrint()
    {
        await Run(async (window, _) =>
        {
            window.Show();

            await PressHandled(window, Key.P, KeyModifiers.Control);

            window.IsBackstageOverlayVisibleForTest.Should().BeTrue();
            window.ActiveBackstagePaneForTest.Should().Be(FreeXBackstagePaneId.Print);
            window.BackstagePaneButtonForTest(FreeXBackstagePaneId.Print)!.IsEffectivelyEnabled.Should().BeTrue();
        });
    }

    [Fact]
    public async Task BackstageCommandKeytipHonorsDisabledRenderedCommand()
    {
        await Run(async (window, _) =>
        {
            window.Show();
            await PressHandled(window, Key.F, KeyModifiers.Alt);

            var accountButton = window.BackstageCommandButtonForTest(FreeXBackstageCommandId.Account);
            accountButton.Should().NotBeNull();
            accountButton!.IsEnabled = false;

            await PressHandled(window, Key.D);

            window.IsBackstageOverlayVisibleForTest.Should().BeTrue();
            window.ActiveBackstagePaneForTest.Should().Be(FreeXBackstagePaneId.Home);
            window.RibbonKeyTipInputForTest.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task BackstageSaveAsAndAccountKeytipsInvokeIndependentRenderedCommands()
    {
        await Run(async (window, _) =>
        {
            window.Show();
            var activated = new List<FreeXBackstageCommandId>();
            window.BackstageCommandActivationOverrideForTest = activated.Add;

            await PressHandled(window, Key.F, KeyModifiers.Alt);
            var saveAsButton = window.BackstageCommandButtonForTest(FreeXBackstageCommandId.SaveAs);
            saveAsButton.Should().NotBeNull();
            saveAsButton!.IsVisible.Should().BeTrue();

            await PressHandled(window, Key.A);

            activated.Should().ContainSingle().Which.Should().Be(FreeXBackstageCommandId.SaveAs);
            window.IsBackstageOverlayVisibleForTest.Should().BeFalse();

            await PressHandled(window, Key.F, KeyModifiers.Alt);
            var accountButton = window.BackstageCommandButtonForTest(FreeXBackstageCommandId.Account);
            accountButton.Should().NotBeNull();
            accountButton!.IsVisible.Should().BeTrue();

            await PressHandled(window, Key.D);

            activated.Should().Equal(
                FreeXBackstageCommandId.SaveAs,
                FreeXBackstageCommandId.Account);
            window.IsBackstageOverlayVisibleForTest.Should().BeFalse();
        });
    }

    [Fact]
    public async Task BackstageKeytipEscapeAndUnmatchedContinuationCloseLiveOverlay()
    {
        await Run(async (window, _) =>
        {
            window.Show();

            await PressHandled(window, Key.F, KeyModifiers.Alt);
            await PressHandled(window, Key.Escape);
            window.IsBackstageOverlayVisibleForTest.Should().BeFalse();
            window.RibbonKeyTipInputForTest.Should().BeEmpty();

            await PressHandled(window, Key.F, KeyModifiers.Alt);
            await PressHandled(window, Key.X);
            window.IsBackstageOverlayVisibleForTest.Should().BeFalse();
            window.RibbonKeyTipInputForTest.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task QuickAccessKeytipRaisesTheRenderedButtonAndHonorsEffectiveEnabledState()
    {
        await Run(async (window, _) =>
        {
            window.Show();

            var visibleButtons = window.AvaloniaQuickAccessToolbarForTest.Children
                .OfType<Button>()
                .Where(button => button.Tag is string tag &&
                    !tag.EndsWith(".History", StringComparison.Ordinal) &&
                    button.IsVisible)
                .ToArray();
            var redoIndex = Array.FindIndex(visibleButtons,
                button => string.Equals(button.Tag as string, QuickAccessToolbarCommandIds.Redo,
                    StringComparison.OrdinalIgnoreCase));
            redoIndex.Should().BeInRange(0, 2);

            var redoButton = visibleButtons[redoIndex];
            var clicks = 0;
            redoButton.Click += (_, _) => clicks++;

            redoButton.IsEnabled = true;
            await PressHandled(window, DigitKey(redoIndex + 1), KeyModifiers.Alt);
            clicks.Should().Be(1);

            redoButton.IsEnabled = false;
            await PressHandled(window, DigitKey(redoIndex + 1), KeyModifiers.Alt);
            clicks.Should().Be(1);
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

    private static Key DigitKey(int digit) =>
        digit switch
        {
            1 => Key.D1,
            2 => Key.D2,
            3 => Key.D3,
            _ => throw new ArgumentOutOfRangeException(nameof(digit), digit, "QAT key tips use 1-3 in this host."),
        };

    private static async Task<KeyEventArgs> Press(
        MainWindow window,
        Key key,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = modifiers };
        await window.RaiseKeyDownForTest(args);
        return args;
    }

    private static async Task<Window?> WaitForOwnedWindow(MainWindow owner, string title)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var dialog = owner.OwnedWindows.FirstOrDefault(candidate =>
                candidate.IsVisible && string.Equals(candidate.Title, title, StringComparison.Ordinal));
            if (dialog is not null)
                return dialog;

            await Task.Delay(10);
        }

        return null;
    }
}
