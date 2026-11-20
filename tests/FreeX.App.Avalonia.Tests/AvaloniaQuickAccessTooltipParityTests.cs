using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using FluentAssertions;
using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Localization;
using FreeX.App.Services.Ribbon;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// WPF parity: the quick access toolbar buttons carry the same RibbonTooltip content as
/// MainWindow.QuickAccessToolbar.cs (title, description from the catalog resource, QAT key tip).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaQuickAccessTooltipParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static string[] TipLines(Control control)
    {
        var tip = ToolTip.GetTip(control);
        var panel = Assert.IsAssignableFrom<Panel>(tip);
        return panel.Children.OfType<TextBlock>().Select(text => text.Text ?? string.Empty).ToArray();
    }

    [Fact]
    public async Task QuickAccessButtons_ShowTitleKeyTipAndCatalogDescription()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
                var expectedKeyTips = new Dictionary<string, string>();
                var index = 1;
                foreach (var command in QuickAccessToolbarCatalog.Normalize(null))
                    expectedKeyTips[command.Id] = QuickAccessToolbarCatalog.FormatKeyTip(index++);

                foreach (var id in new[] { QuickAccessToolbarCommandIds.Save, QuickAccessToolbarCommandIds.Undo, QuickAccessToolbarCommandIds.Redo })
                {
                    QuickAccessToolbarCatalog.TryGet(id, out var command).Should().BeTrue();
                    var button = buttons.Single(b => AutomationProperties.GetAutomationId(b) == command.AutomationId);
                    var lines = TipLines(button);
                    var title = UiText.Get(command.TitleResourceKey);
                    var description = UiText.Get(command.DescriptionResourceKey);

                    description.Should().NotBeNullOrWhiteSpace();
                    lines.Should().HaveCount(2);
                    lines[0].Should().Be($"{title} ({expectedKeyTips[id]})");
                    lines[1].Should().Be(description);
                }

                var undoHistory = buttons.Single(b => AutomationProperties.GetAutomationId(b) == "UndoQatHistoryButton");
                var historyLines = TipLines(undoHistory);
                historyLines.Should().HaveCount(2);
                historyLines[1].Should().Be(UiText.Format(
                    "QuickAccessToolbar_HistoryDescriptionFormat",
                    UiText.Get("MainWindow_TooltipTitle_Undo").ToLowerInvariant()));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void ProtectRibbonControls_CarryWpfProtectionDescriptions()
    {
        var controls = AvaloniaRibbonComposition.BuildDefinition().Tabs
            .SelectMany(tab => tab.Groups).SelectMany(group => group.Controls).ToArray();

        controls.First(c => c.CommandId.Value == FreeXRibbonCommandIds.ReviewProtectSheet).TooltipDescription.Should().Be(
            UiText.Get("MainWindow_TooltipDescription_SetSheetProtectionForLockedCellsWithAnOptionalPassword"));
        controls.First(c => c.CommandId.Value == "Protect Workbook").TooltipDescription.Should().Be(
            UiText.Get("MainWindow_TooltipDescription_PreventStructuralChangesToTheWorkbookSuchAsAddingDeletingOrRenamingSheet_47267D4F"));
    }
}
