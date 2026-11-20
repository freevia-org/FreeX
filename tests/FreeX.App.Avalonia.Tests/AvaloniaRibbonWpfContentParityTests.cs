using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FreeX.App.Localization;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// WPF parity for ribbon content the host layers on top of the shared declarative definition:
/// Home combo item sources, Protect Sheet/Workbook dynamic labels and tooltips, and View > Window
/// availability descriptions.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaRibbonWpfContentParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static Control Ribbon(MainWindow window) =>
        (Control)typeof(MainWindow).GetField("_ribbonControl", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static IEnumerable<Button> Buttons(MainWindow window, string tag) =>
        window.FindRibbonButtons(tag);

    private static string Caption(Button button) =>
        button.GetLogicalDescendants().OfType<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text)).Text!;

    private static string Tip(Button button) =>
        string.Join("|", ((Panel)ToolTip.GetTip(button)!).Children.OfType<TextBlock>().Select(t => t.Text));

    private static async Task WithWindow(Action<MainWindow> body)
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            window.Show();
            try { body(window); }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public Task HomeFontSizeCombo_OffersTheFullWpfSizeList() => WithWindow(window =>
    {
        var combo = Ribbon(window).GetLogicalDescendants().OfType<ComboBox>().First(c => Equals(c.Tag, "Font Size"));
        combo.Items.Cast<object>().Select(i => i.ToString()).Should().Equal(
            "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48", "72");
    });

    [Fact]
    public Task HomeFontCombo_ListsSortedPlatformFontsAndKeepsCalibriSelected() => WithWindow(window =>
    {
        var combo = Ribbon(window).GetLogicalDescendants().OfType<ComboBox>().First(c => Equals(c.Tag, "Font"));
        var names = combo.Items.Cast<object>().Select(i => i.ToString()!).ToList();
        names.Should().Contain("Calibri");
        names.Should().BeInAscendingOrder(StringComparer.CurrentCultureIgnoreCase);
        names.Distinct(StringComparer.CurrentCultureIgnoreCase).Should().HaveCount(names.Count);
        combo.SelectedItem.Should().Be("Calibri");
    });

    [Fact]
    public Task ProtectSheetAndWorkbook_ToggleLabelAndTooltipWithProtectionState() => WithWindow(window =>
    {
        window.ApplyDynamicRibbonChrome();
        var sheetButton = Buttons(window, FreeXRibbonCommandIds.ReviewProtectSheet).First();
        Caption(sheetButton).Should().Be(UiText.Get("MainWindow_Content_ProtectSheet"));

        window.Session.ActiveSheet.IsProtected = true;
        window.ApplyDynamicRibbonChrome();
        Caption(sheetButton).Should().Be(UiText.Get("Protection_UnprotectSheetButton"));
        Tip(sheetButton).Should().StartWith(UiText.Get("Protection_UnprotectSheetTitle"));
        Tip(sheetButton).Should().Contain(UiText.Get("Protection_UnprotectSheetDescription"));

        window.Session.ActiveSheet.IsProtected = false;
        window.ApplyDynamicRibbonChrome();
        Caption(sheetButton).Should().Be(UiText.Get("MainWindow_Content_ProtectSheet"));
    });

    [Fact]
    public Task WindowCommands_ExplainUnavailabilityWithASingleVisibleWindow() => WithWindow(window =>
    {
        window.ApplyDynamicRibbonChrome();
        Tip(Buttons(window, "Switch Windows").First())
            .Should().Contain(UiText.Get("MainWindow_TooltipDescription_UnavailableSwitchWindowsRequiresSecondVisibleWindow"));
        Tip(Buttons(window, "Hide").First())
            .Should().Contain(UiText.Get("MainWindow_TooltipDescription_UnavailableHideRequiresSecondVisibleWindow"));
        Tip(Buttons(window, "Unhide").First())
            .Should().Contain(UiText.Get("MainWindow_TooltipDescription_UnavailableUnhideRequiresAHiddenWindow"));
        Tip(Buttons(window, "New Window").First())
            .Should().Contain(UiText.Get("MainWindow_TooltipDescription_OpenAnotherLiveWindowForThisWorkbook"));
        Buttons(window, "Switch Windows").First().IsEnabled.Should().BeFalse();
    });

    [Fact]
    public Task ArrangeAllMenu_ChecksTheStoredArrangement() => WithWindow(window =>
    {
        window.Session.ExecuteReviewCommand(new FreeX.Core.Commands.SetWorkbookWindowArrangementCommand(
            FreeX.Core.Model.WorkbookWindowArrangement.Cascade)).Success.Should().BeTrue();

        window.GetArrangeAllRibbonState(FreeX.Core.Model.WorkbookWindowArrangement.Cascade).IsChecked.Should().BeTrue();
        window.GetArrangeAllRibbonState(FreeX.Core.Model.WorkbookWindowArrangement.Tiled).IsChecked.Should().BeFalse();
    });

    [Fact]
    public Task InsertPivotChart_IsDisabledUntilTheSelectionIsInsideAPivotTable() => WithWindow(window =>
    {
        window.ApplyDynamicRibbonChrome();
        window.FindRibbonButtons(FreeXRibbonCommandIds.PivotChartInsert).First().IsEnabled.Should().BeFalse();
    });
}
