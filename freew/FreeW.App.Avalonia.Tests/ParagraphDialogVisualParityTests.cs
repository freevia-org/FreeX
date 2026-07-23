using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using FreeW.App.Localization;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

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
            var buttons = dialog.GetLogicalDescendants()
                .OfType<Button>()
                .Where(button => button is not global::Avalonia.Controls.Primitives.ToggleButton)
                .ToArray();

            var tabItems = tabs.Items.Cast<TabItem>().ToArray();
            tabItems.Should().HaveCount(2);
            tabs.Height.Should().Be(234);
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
            AutomationProperties.GetAutomationId(left).Should().Be("paragraph-left-indent");

            buttons.Select(button => button.Content?.ToString()).Should().Equal(LocalizedUiText.Ok, LocalizedUiText.Cancel);
            buttons.Single(button => button.IsDefault).Content.Should().Be(LocalizedUiText.Ok);
            buttons.Single(button => button.IsCancel).Content.Should().Be(LocalizedUiText.Cancel);
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
            "scenario.RouteId is \"font\" or \"paragraph\" or \"multilevel-list\" or \"paste-special\" or \"style\" or \"manage-styles\"");
        wpfHarness.Should().Contain("scenario.RouteId is \"font\" or \"paragraph\"");
        wpfHarness.Should().Contain("Populate(dialog, scenario);");
    }

    private static T Field<T>(ParagraphDialog dialog, string name) where T : class =>
        (T)(typeof(ParagraphDialog)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)
            ?? throw new InvalidOperationException($"Missing ParagraphDialog field {name}."));
}
