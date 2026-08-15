using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.Automation;
using Free.Shared.AppServices;
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
            buttons.Select(UserFacingButtonText).Should().Equal(LocalizedUiText.Ok, LocalizedUiText.Cancel);
            UserFacingButtonText(buttons.Single(button => button.IsDefault)).Should().Be(LocalizedUiText.Ok);
            UserFacingButtonText(buttons.Single(button => button.IsCancel)).Should().Be(LocalizedUiText.Cancel);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Constructor_seeds_edge_checks_like_the_Wpf_authority_capture()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new BordersAndShadingDialog(ParagraphFormatting.Default, null);
            var edges = dialog.GetLogicalDescendants()
                .OfType<CheckBox>()
                .Where(check => check.Content is "Top" or "Left" or "Bottom" or "Right")
                .ToArray();

            edges.Should().HaveCount(4);
            edges.Select(check => check.IsChecked).Should().Equal(true, true, true, true);
            edges.Select(check => check.IsEnabled).Should().Equal(true, true, true, true);
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
                dialog.GetLogicalDescendants()
                    .OfType<Button>()
                    .Where(button => AutomationProperties.GetAutomationId(button) is "BordersAndShadingOkButton" or "BordersAndShadingCancelButton")
                    .Select(button => button.Bounds.Height)
                    .Should().Equal(26, 26);

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
            var messages = new RecordingMessageService();
            var dialog = new BordersAndShadingDialog(
                ParagraphFormatting.Default,
                null,
                messages);
            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                var ok = dialog.GetLogicalDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetAutomationId(button) == "BordersAndShadingOkButton");
                dialog.ParagraphWidthForTest.Text = "13";

                ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                dialog.StatusForTest.IsVisible.Should().BeFalse();
                messages.Request.Should().Be(new UserMessageRequest(
                    BordersAndShadingDialogPlanner.WidthValidationMessage,
                    "Warning",
                    UserMessageButtons.Ok,
                    UserMessageIcon.Warning));
                dialog.IsVisible.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    private sealed class RecordingMessageService : IUserMessageService
    {
        public UserMessageRequest? Request { get; private set; }

        public ValueTask<UserMessageResult> ShowMessageAsync(
            UserMessageRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return ValueTask.FromResult(UserMessageResult.Ok);
        }
    }

    [Fact]
    public void Wpf_authority_declares_the_same_control_metadata_contract()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "BordersAndShadingDialog.cs"));

        source.Should().Contain("BordersAndShadingDialogPlanner.AutomationId");
        foreach (var id in new[]
        {
            "BordersAndShadingDialogPlanner.ParagraphSettingAutomationId",
            "BordersAndShadingDialogPlanner.ParagraphWidthAutomationId",
            "BordersAndShadingDialogPlanner.PageBorderTabAutomationId",
            "BordersAndShadingDialogPlanner.ShadingPatternAutomationId",
            "BordersAndShadingDialogPlanner.AcceptButtonAutomationId",
            "BordersAndShadingDialogPlanner.CancelButtonAutomationId",
        })
        {
            source.Should().Contain(id);
        }
    }

    [Fact]
    public void Visual_harness_keeps_the_combined_route_on_both_hosts()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var catalog = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "tools",
            "FreeW.DialogVisualHarness",
            "FreeWDialogEvidenceCatalog.cs"));
        var inventoryBuilder = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "tools",
            "FreeW.DialogVisualHarness",
            "Program.cs"));

        catalog.Should().Contain("Pair(\"borders-and-shading\", \"BordersAndShadingDialog\")");
        inventoryBuilder.Should().Contain("var classText = text[match.Index..classEnd]");
    }

    [Fact]
    public void Uses_the_shared_Wpf_flush_tab_pane_compensation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "ParagraphCommandDialogs.cs"));

        source.Should().Contain("contentPaneMargin: new Thickness(");
        source.Should().Contain("Layout.AvaloniaTabPaneHorizontalCompensation");
        BordersAndShadingDialogPlanner.VisualMetrics.AvaloniaTabPaneHorizontalCompensation.Should().Be(-12);
        source.Should().Contain("dialog.ApplyParagraphSettingPlan();");
    }

    // AvaloniaDialogButtonContent wraps mnemonic-bearing text ("_OK") in an AccessText so Avalonia's
    // Fluent button template actually registers and renders the access key (WPF does this automatically
    // for a plain string; Avalonia does not). Read the user-facing text back out for content comparisons.
    private static string? UserFacingButtonText(Button button) => button.Content switch
    {
        string text => text,
        global::Avalonia.Controls.Primitives.AccessText accessText => accessText.Text,
        _ => button.Content?.ToString(),
    };
}
