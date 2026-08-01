using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Automation;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Options;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class OptionsDialogVisualParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Options_uses_Wpf_table_geometry_and_shared_action_contract()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new OptionsDialog(new FreeWOptions
            {
                AutoCorrect = new AutoCorrectOptions
                {
                    Replacements = [new AutoCorrectReplacement("teh", "the")],
                },
            });
            try
            {
                dialog.Width.Should().Be(460);
                dialog.FontFamily.Should().Be(AvaloniaCompactDialogChrome.WindowsUiFontFamily);

                var table = GetField<Border>(dialog, "_replacements");
                table.Height.Should().Be(180);
                var grid = GetField<Grid>(dialog, "_replacementGrid");
                grid.ColumnDefinitions.Count.Should().Be(2);
                grid.RowDefinitions.Count.Should().Be(3, "the WPF DataGrid has one populated row plus its blank add row");
                dialog.ReplacementEditorsForTest.Should().HaveCount(2);

                var buttons = dialog.GetLogicalDescendants()
                    .OfType<Button>()
                    .Where(button => button is not ToggleButton)
                    .ToArray();
                buttons.Select(button => button.Content?.ToString())
                    .Should().Equal(ShellStrings.Current.Ok, ShellStrings.Current.Cancel);
                buttons[0].MinWidth.Should().Be(84);
                buttons[0].IsDefault.Should().BeTrue();
                buttons[0].IsCancel.Should().BeFalse();
                buttons[1].IsCancel.Should().BeTrue();
                buttons[1].IsDefault.Should().BeFalse();
                buttons.Select(AutomationProperties.GetName)
                    .Should().OnlyContain(name => !string.IsNullOrWhiteSpace(name));
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Options_selects_the_recent_files_field_on_open_and_commits_grid_rows()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new OptionsDialog(new FreeWOptions
            {
                RecentFilesCap = 12,
                AutoCorrect = new AutoCorrectOptions
                {
                    ReplaceText = true,
                    Replacements = [new AutoCorrectReplacement("teh", "the")],
                },
            });
            try
            {
                dialog.Show();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                dialog.RecentFilesCapForTest.IsFocused.Should().BeTrue();
                dialog.RecentFilesCapForTest.SelectionStart.Should().Be(0);
                dialog.RecentFilesCapForTest.SelectionEnd.Should().Be(2);

                var row = dialog.ReplacementEditorsForTest[0];
                row.Replace.Text = " adn ";
                row.With.Text = "and";
                dialog.AcceptForTest();

                dialog.Result.Should().NotBeNull();
                dialog.Result!.RecentFilesCap.Should().Be(12);
                dialog.Result.AutoCorrect.ReplaceText.Should().BeTrue();
                dialog.Result.AutoCorrect.Replacements
                    .Should().ContainSingle(replacement => replacement.Replace == "adn" && replacement.With == "and");
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    private static T GetField<T>(OptionsDialog dialog, string name) where T : class =>
        (T)(typeof(OptionsDialog)
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(dialog)
            ?? throw new InvalidOperationException($"Missing OptionsDialog field {name}."));
}
