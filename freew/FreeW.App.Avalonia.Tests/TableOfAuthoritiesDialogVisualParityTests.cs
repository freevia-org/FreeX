using Avalonia;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using FreeW.App.Avalonia;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Avalonia.Tests;

public sealed class TableOfAuthoritiesDialogVisualParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Dialog_uses_Wpf_authority_geometry_and_action_chrome()
    {
        await Session.Dispatch(() =>
        {
            var dialog = CreateDialog();
            var category = Field<ComboBox>(dialog, "_category");
            var passim = Field<CheckBox>(dialog, "_passim");
            var keepFormatting = Field<CheckBox>(dialog, "_keepFormatting");
            var leader = Field<ComboBox>(dialog, "_leader");
            var buttons = dialog.GetLogicalDescendants().OfType<Button>()
                .Where(button => button is not ToggleButton)
                .ToArray();

            dialog.Width.Should().Be(380);
            category.Height.Should().Be(22);
            category.Margin.Should().Be(new Thickness(0, 0, 0, 8));
            passim.Height.Should().Be(18);
            passim.Margin.Should().Be(new Thickness(0, 0, 0, 6));
            keepFormatting.Height.Should().Be(18);
            keepFormatting.Margin.Should().Be(new Thickness(0, 0, 0, 8));
            leader.Height.Should().Be(22);
            leader.Margin.Should().Be(new Thickness(0, 0, 0, 8));
            buttons.Select(button => button.Content?.ToString()).Should().Equal("OK", "Cancel");
            buttons.Should().OnlyContain(button => button.MinWidth == 80 && button.Height == 26);
            buttons.Should().OnlyContain(button => button.CornerRadius == new CornerRadius(3));
            buttons.Should().OnlyContain(button => ((ISolidColorBrush)button.Background!).Color == Colors.White);
            buttons.Should().OnlyContain(button => ((ISolidColorBrush)button.BorderBrush!).Color == Color.FromRgb(200, 200, 200));
            buttons.Single(button => button.IsDefault).IsCancel.Should().BeFalse();
            buttons.Single(button => button.IsCancel).IsDefault.Should().BeFalse();
            category.Items.Cast<object>().Should().OnlyContain(item => item is TableOfAuthoritiesCategoryChoice);
            leader.Items.Cast<object>().Should().OnlyContain(item => item is TableOfAuthoritiesTabLeaderChoice);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Dialog_keeps_shared_planner_state_and_accessible_actions()
    {
        await Session.Dispatch(() =>
        {
            var dialog = CreateDialog();
            var passim = Field<CheckBox>(dialog, "_passim");
            var keepFormatting = Field<CheckBox>(dialog, "_keepFormatting");
            var category = Field<ComboBox>(dialog, "_category");
            var leader = Field<ComboBox>(dialog, "_leader");
            var buttons = dialog.GetLogicalDescendants().OfType<Button>()
                .Where(button => button is not ToggleButton)
                .ToArray();

            passim.IsChecked = true;
            keepFormatting.IsChecked = true;
            category.SelectedIndex = 2;
            leader.SelectedIndex = 1;

            var result = (FreeW.Core.Model.ToaOptions)Invoke(dialog, "BuildResultForTest")!;
            result.UsePassim.Should().BeTrue();
            result.KeepOriginalFormatting.Should().BeTrue();
            result.CategoryFilter.Should().Be(FreeW.Core.Model.CitationCategory.Statutes);
            result.TabLeader.Should().Be(FreeW.Core.Model.ToaTabLeader.Dashes);
            buttons.Single(button => button.IsDefault).IsDefault.Should().BeTrue();
            buttons.Single(button => button.IsCancel).IsCancel.Should().BeTrue();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("initial")]
    [InlineData("populated")]
    [InlineData("validation-error")]
    public async Task Dialog_uses_Wpf_action_row_vertical_geometry_for_each_evidence_state(string state)
    {
        await Session.Dispatch(() =>
        {
            var options = TableOfAuthoritiesDialogPlanner.BuildEvidenceOptions(state);
            var dialog = (TableOfAuthoritiesDialog)Activator.CreateInstance(
                typeof(TableOfAuthoritiesDialog),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: [options],
                culture: null)!;
            var root = (StackPanel)dialog.Content!;
            var actionRow = root.Children.OfType<StackPanel>().Single();

            root.Margin.Should().Be(new Thickness(16, 16, 17, 16));
            actionRow.Margin.Should().Be(new Thickness(0, 12, 0, 0));
        }, CancellationToken.None);
    }

    private static TableOfAuthoritiesDialog CreateDialog() =>
        (TableOfAuthoritiesDialog)Activator.CreateInstance(
            typeof(TableOfAuthoritiesDialog),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [FreeW.Core.Model.ToaOptions.Default],
            culture: null)!;

    private static T Field<T>(TableOfAuthoritiesDialog dialog, string name) where T : class =>
        (T)(typeof(TableOfAuthoritiesDialog)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)
            ?? throw new InvalidOperationException($"Missing TableOfAuthoritiesDialog field {name}."));

    private static object? Invoke(TableOfAuthoritiesDialog dialog, string name) =>
        typeof(TableOfAuthoritiesDialog)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dialog, null);
}
