using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.Automation;
using FreeW.App.Localization;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class BordersAndShadingDialogVisualParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Uses_the_Wpf_three_tab_geometry_and_action_contract()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new BordersAndShadingDialog(ParagraphFormatting.Default, null);

            dialog.Width.Should().Be(420);
            var tabs = dialog.TabsForTest;
            tabs.Items.OfType<TabItem>().Select(item => item.Header?.ToString())
                .Should().Equal("Borders", "Page Border", "Shading");
            tabs.Items.OfType<TabItem>().Select(item => item.Content).Should()
                .OnlyContain(content => content is Grid);

            var buttons = dialog.GetLogicalDescendants()
                .OfType<Button>()
                .Where(button => button is not global::Avalonia.Controls.Primitives.ToggleButton)
                .ToArray();
            buttons.Select(button => button.Content?.ToString()).Should().Equal(LocalizedUiText.Ok, LocalizedUiText.Cancel);
            buttons.Single(button => button.IsDefault).Content.Should().Be(LocalizedUiText.Ok);
            buttons.Single(button => button.IsCancel).Content.Should().Be(LocalizedUiText.Cancel);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Exposes_WPF_control_metadata_and_selects_the_width_on_open()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new BordersAndShadingDialog(ParagraphFormatting.Default, null);
            AutomationProperties.GetAutomationId(dialog).Should().Be("BordersAndShadingDialog");
            AutomationProperties.GetAutomationId(dialog.TabsForTest).Should().Be("BordersAndShadingTabs");

            var controls = dialog.GetLogicalDescendants().OfType<Control>().ToArray();
            controls.Select(AutomationProperties.GetAutomationId).Should().Contain([
                "BordersAndShadingParagraphSetting",
                "BordersAndShadingParagraphStyle",
                "BordersAndShadingParagraphColor",
                "BordersAndShadingParagraphWidth",
                "BordersAndShadingPageSetting",
                "BordersAndShadingPageStyle",
                "BordersAndShadingPageColor",
                "BordersAndShadingPageWidth",
                "BordersAndShadingPageArt",
                "BordersAndShadingShadingColor",
                "BordersAndShadingShadingPattern",
                "BordersAndShadingBordersTab",
                "BordersAndShadingPageBorderTab",
                "BordersAndShadingShadingTab",
                "BordersAndShadingOkButton",
                "BordersAndShadingCancelButton",
            ]);

            try
            {
                dialog.Show();
                dialog.Measure(new Size(420, 600));
                dialog.Arrange(new Rect(0, 0, 420, 600));
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                dialog.ParagraphWidthForTest.IsFocused.Should().BeTrue();
                dialog.ParagraphWidthForTest.SelectionStart.Should().Be(0);
                dialog.ParagraphWidthForTest.SelectionEnd.Should().Be(dialog.ParagraphWidthForTest.Text?.Length ?? 0);
                dialog.ParagraphWidthForTest.Height.Should().Be(20);

                dialog.TabsForTest.SelectedIndex = 1;
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                dialog.PageSettingForTest.IsFocused.Should().BeTrue();

                dialog.TabsForTest.SelectedIndex = 2;
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                dialog.ShadingColorForTest.IsFocused.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Invalid_width_keeps_dialog_open_and_surfaces_validation_message()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new BordersAndShadingDialog(ParagraphFormatting.Default, null);
            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                var ok = dialog.GetLogicalDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetAutomationId(button) == "BordersAndShadingOkButton");
                dialog.ParagraphWidthForTest.Text = "13";

                ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                dialog.StatusForTest.IsVisible.Should().BeTrue();
                dialog.StatusForTest.Text.Should().Be(BordersAndShadingDialogPlanner.WidthValidationMessage);
                dialog.IsVisible.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void Wpf_authority_declares_the_same_control_metadata_contract()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "BordersAndShadingDialog.cs"));

        source.Should().Contain("BordersAndShadingDialog");
        foreach (var id in new[]
        {
            "BordersAndShadingParagraphSetting",
            "BordersAndShadingParagraphWidth",
            "BordersAndShadingPageBorderTab",
            "BordersAndShadingShadingPattern",
            "BordersAndShadingOkButton",
            "BordersAndShadingCancelButton",
        })
        {
            source.Should().Contain(id);
        }
    }

    [Fact]
    public void Visual_harness_keeps_the_combined_route_on_both_hosts()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var avaloniaFactory = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "tools",
            "FreeW.DialogVisualHarness.Avalonia",
            "AvaloniaDialogRouteFactory.cs"));
        var wpfFactory = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "tools",
            "FreeW.DialogVisualHarness.Wpf",
            "WpfDialogRouteFactory.cs"));
        var inventoryBuilder = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "tools",
            "FreeW.DialogVisualHarness",
            "Program.cs"));

        avaloniaFactory.Should().Contain("[\"borders-and-shading\"] = \"BordersAndShadingDialog\"");
        wpfFactory.Should().Contain("[\"borders-and-shading\"] = \"BordersAndShadingDialog\"");
        inventoryBuilder.Should().Contain("var classText = text[match.Index..classEnd]");
    }
}
