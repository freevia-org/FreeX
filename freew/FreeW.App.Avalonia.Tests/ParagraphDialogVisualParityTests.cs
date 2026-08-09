using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeW.App.Localization;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using Free.Shared.Shell.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class ParagraphDialogVisualParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Initial_state_tabs_and_actions_match_the_Wpf_contract()
    {
        await Session.Dispatch(() =>
        {
            var current = ParagraphFormatting.Default;
            var expected = ParagraphBreaksDialogPlanner.BuildInitialState(current, System.Globalization.CultureInfo.CurrentCulture);
            var dialog = new ParagraphDialog(current);

            var tabs = Field<TabControl>(dialog, "_tabs");
            var left = Field<TextBox>(dialog, "_left");
            var right = Field<TextBox>(dialog, "_right");
            var before = Field<TextBox>(dialog, "_before");
            var after = Field<TextBox>(dialog, "_after");
            var lineSpacing = Field<TextBox>(dialog, "_lineSpacing");
            var special = Field<ComboBox>(dialog, "_special");
            var suppressLineNumbers = Field<CheckBox>(dialog, "_suppressLineNumbers");
            var contextualSpacing = Field<CheckBox>(dialog, "_contextualSpacing");
            var specialAmount = Field<TextBox>(dialog, "_specialAmount");
            var buttons = dialog.GetLogicalDescendants()
                .OfType<Button>()
                .Where(button => button is not global::Avalonia.Controls.Primitives.ToggleButton)
                .ToArray();

            var tabItems = tabs.Items.Cast<TabItem>().ToArray();
            tabItems.Should().HaveCount(2);
            dialog.Width.Should().Be(380);
            tabs.Height.Should().Be(253);
            tabItems[0].Header.Should().Be("Indents and Spacing");
            tabItems[1].Header.Should().Be("Line and Page Breaks");
            tabItems[0].Width.Should().Be(123);
            tabItems[1].Width.Should().Be(122);

            left.Text.Should().Be(expected.LeftText);
            right.Text.Should().Be(expected.RightText);
            before.Text.Should().Be(expected.SpaceBeforeText);
            after.Text.Should().Be(expected.SpaceAfterText);
            lineSpacing.Text.Should().Be(expected.LineSpacingText);
            special.SelectedIndex.Should().Be(expected.SpecialIndex);
            special.HorizontalAlignment.Should().Be(global::Avalonia.Layout.HorizontalAlignment.Stretch);
            specialAmount.IsEnabled.Should().BeFalse();
            suppressLineNumbers.IsChecked.Should().Be(expected.SuppressLineNumbers);
            contextualSpacing.IsChecked.Should().Be(expected.ContextualSpacing);
            contextualSpacing.IsVisible.Should().BeTrue();
            AutomationProperties.GetAutomationId(left).Should().Be("paragraph-left-indent");

            buttons.Select(UserFacingButtonText).Should().Equal(LocalizedUiText.Ok, LocalizedUiText.Cancel);
            UserFacingButtonText(buttons.Single(button => button.IsDefault)).Should().Be(LocalizedUiText.Ok);
            UserFacingButtonText(buttons.Single(button => button.IsCancel)).Should().Be(LocalizedUiText.Cancel);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Line_and_page_breaks_tab_uses_the_Wpf_authority_height()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new ParagraphDialog(ParagraphFormatting.Default);
            var tabs = Field<TabControl>(dialog, "_tabs");

            tabs.SelectedIndex = 1;

            tabs.Height.Should().Be(235);
            tabs.SelectedItem.Should().BeOfType<TabItem>();
            ((TabItem)tabs.SelectedItem!).Header.Should().Be("Line and Page Breaks");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Line_and_page_breaks_tab_uses_Wpf_section_spacing()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new ParagraphDialog(ParagraphFormatting.Default);
            var tabs = Field<TabControl>(dialog, "_tabs");
            var panel = (StackPanel)((TabItem)tabs.Items[1]!).Content!;

            panel.Children.OfType<TextBlock>().Select(text => text.Margin.Bottom)
                .Should().Equal(8, 8);
            panel.Children.OfType<CheckBox>().Select(check => check.Margin.Bottom)
                .Should().Equal(6, 6, 6, 6, 6, 6);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Paragraph_uses_Wpf_authority_control_chrome_without_changing_shared_defaults()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new ParagraphDialog(ParagraphFormatting.Default);
            var left = Field<TextBox>(dialog, "_left");
            var special = Field<ComboBox>(dialog, "_special");
            var specialAmount = Field<TextBox>(dialog, "_specialAmount");

            ((ISolidColorBrush)left.BorderBrush!).Color.Should().Be(Color.FromRgb(0xAB, 0xAD, 0xB3));
            ((ISolidColorBrush)left.SelectionBrush!).Color.Should().Be(Color.FromRgb(0x56, 0x9D, 0xE5));
            ((ISolidColorBrush)special.Background!).Color.Should().Be(Color.FromRgb(0xF0, 0xF0, 0xF0));
            left.Height.Should().Be(18);
            left.FocusAdorner.Should().BeNull();
            special.Height.Should().Be(22);
            ((ISolidColorBrush)specialAmount.BorderBrush!).Color
                .Should().Be(Color.FromRgb(0xD0, 0xD1, 0xD4));

            var sharedTextBox = new TextBox();
            AvaloniaCompactDialogChrome.ApplyTextBox(
                sharedTextBox,
                AvaloniaCompactDialogChrome.WindowsStyle);
            ((ISolidColorBrush)sharedTextBox.BorderBrush!).Color.Should().Be(Color.FromRgb(0xAB, 0xAD, 0xB3));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Paragraph_dialog_materializes_Wpf_textbox_and_checkbox_geometry()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new ParagraphDialog(ParagraphFormatting.Default);
            try
            {
                dialog.Width = 380;
                dialog.Height = 345;
                dialog.Show();
                dialog.Measure(new Size(380, 345));
                dialog.Arrange(new Rect(0, 0, 380, 345));
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                var fields = new[] { "_left", "_right", "_specialAmount", "_before", "_after", "_lineSpacing" }
                    .Select(name => Field<TextBox>(dialog, name))
                    .ToArray();
                fields.SelectMany(box => box.GetVisualDescendants().OfType<Border>())
                    .Where(border => border.Name == "PART_BorderElement")
                    .Should().HaveCount(6)
                    .And.OnlyContain(border => border.Bounds.Height == 18);

                var indicators = dialog.GetVisualDescendants()
                    .OfType<CheckBox>()
                    .SelectMany(check => check.GetVisualDescendants().OfType<Border>())
                    .Where(border => border.Bounds.Width == 14 && border.Bounds.Height == 13)
                    .ToArray();
                indicators.Should().HaveCount(1);
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Paragraph_dialog_preserves_Wpf_client_surface_inset()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new ParagraphDialog(ParagraphFormatting.Default)
            {
                Width = 366,
                Height = 308,
                SizeToContent = SizeToContent.Manual,
            };
            try
            {
                dialog.Show();
                dialog.Measure(new Size(366, 308));
                dialog.Arrange(new Rect(0, 0, 366, 308));
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                var tabs = Field<TabControl>(dialog, "_tabs");
                tabs.Bounds.X.Should().Be(12);
                tabs.Bounds.Y.Should().Be(12);
                tabs.Bounds.Width.Should().Be(343);
                tabs.Bounds.Height.Should().Be(253);
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Validation_routes_to_the_Wpf_first_field_and_keeps_the_dialog_open()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new ParagraphDialog(ParagraphFormatting.Default);
            var tabs = Field<TabControl>(dialog, "_tabs");
            var left = Field<TextBox>(dialog, "_left");
            var status = Field<TextBlock>(dialog, "_status");
            var accept = typeof(ParagraphDialog).GetMethod(
                "Accept",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(ParagraphDialog), "Accept");

            tabs.SelectedIndex = 1;
            left.Text = "not-a-number";
            accept.Invoke(dialog, null);

            dialog.IsVisible.Should().BeFalse("headless construction must not close the dialog on validation");
            tabs.SelectedIndex.Should().Be(0);
            status.IsVisible.Should().BeTrue();
            status.Text.Should().Be(ParagraphBreaksDialogPlanner.ValidationMessage);
        }, CancellationToken.None);
    }

    [Fact]
    public void Evidence_harness_uses_Wpf_size_and_applies_Paragraph_static_prompt_states()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var avaloniaHarness = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "tools",
            "FreeW.DialogVisualHarness.Avalonia",
            "Program.cs"));
        var wpfHarness = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "tools",
            "FreeW.DialogVisualHarness.Wpf",
            "Program.cs"));

        avaloniaHarness.Should().Contain(
            "scenario.RouteId is \"accessibility-report\" or \"font\" or \"paragraph\" or \"multilevel-list\" or \"paste-special\" or \"style\" or \"manage-styles\"");
        wpfHarness.Should().Contain("scenario.RouteId is \"font\" or \"paragraph\"");
        wpfHarness.Should().Contain("Populate(dialog, scenario);");
        avaloniaHarness.Should().Contain("button is ToggleButton or RepeatButton");
    }

    private static T Field<T>(ParagraphDialog dialog, string name) where T : class =>
        (T)(typeof(ParagraphDialog)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)
            ?? throw new InvalidOperationException($"Missing ParagraphDialog field {name}."));

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
