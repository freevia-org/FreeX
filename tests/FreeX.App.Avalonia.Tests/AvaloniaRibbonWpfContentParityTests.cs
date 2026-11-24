using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FreeX.App.Localization;
using FreeX.App.Services;
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

    private static async Task WithWindow(Action<MainWindow> body, AppOptions? options = null)
    {
        await Session.Dispatch(() =>
        {
            var window = options is null
                ? new MainWindow([])
                : new MainWindow([], null!, new FreeXOptionsRuntimeSession(
                    load: () => options.Clone(), save: static _ => true));
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

    [Theory]
    [InlineData("Calibri")]
    [InlineData("Aptos Narrow")]
    public Task HomeFontCombo_ListsSortedPlatformFontsAndKeepsConfiguredWorkbookFontSelected(string fontName) => WithWindow(window =>
    {
        var combo = Ribbon(window).GetLogicalDescendants().OfType<ComboBox>().First(c => Equals(c.Tag, "Font"));
        var names = combo.Items.Cast<object>().Select(i => i.ToString()!).ToList();
        names.Should().Contain("Calibri");
        names.Should().Contain(fontName);
        names.Should().BeInAscendingOrder(StringComparer.CurrentCultureIgnoreCase);
        names.Distinct(StringComparer.CurrentCultureIgnoreCase).Should().HaveCount(names.Count);
        combo.SelectedItem.Should().Be(fontName);
    }, new AppOptions { DefaultFontName = fontName });

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

    // The window registry is process-static, so windows leaked by other tests in the same run change
    // the visible/hidden counts. Derive the expectation from the live registry instead of assuming one window.
    private static (int Visible, int Hidden) RegistryCounts()
    {
        var registry = typeof(MainWindow).GetField("WindowRegistry", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var type = registry.GetType();
        var visible = (int)type.GetProperty("VisibleCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(registry)!;
        var hidden = ((System.Collections.ICollection)type.GetProperty("HiddenWindows", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(registry)!).Count;
        return (visible, hidden);
    }

    [Fact]
    public Task WindowCommands_DescribeAvailabilityFromTheLiveWindowRegistry() => WithWindow(window =>
    {
        window.ApplyDynamicRibbonChrome();
        var (visible, hidden) = RegistryCounts();
        var multiple = visible > 1;
        Tip(Buttons(window, "Switch Windows").First()).Should().Contain(UiText.Get(multiple
            ? "MainWindow_TooltipDescription_SwitchToAnotherVisibleWorkbookWindow"
            : "MainWindow_TooltipDescription_UnavailableSwitchWindowsRequiresSecondVisibleWindow"));
        Tip(Buttons(window, "Hide").First()).Should().Contain(UiText.Get(multiple
            ? "MainWindow_TooltipDescription_HideThisWorkbookWindowFromView"
            : "MainWindow_TooltipDescription_UnavailableHideRequiresSecondVisibleWindow"));
        Tip(Buttons(window, "Unhide").First()).Should().Contain(UiText.Get(hidden > 0
            ? "MainWindow_TooltipDescription_RestoreAHiddenWorkbookWindow"
            : "MainWindow_TooltipDescription_UnavailableUnhideRequiresAHiddenWindow"));
        Tip(Buttons(window, "New Window").First())
            .Should().Contain(UiText.Get("MainWindow_TooltipDescription_OpenAnotherLiveWindowForThisWorkbook"));
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
