using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class DialogComboBoxChromeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ShellHeadlessApp).Assembly);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Compact_combo_keeps_native_template_behavior_and_lays_out(bool isEditable)
    {
        await Session.Dispatch(() =>
        {
            var comboBox = new ComboBox
            {
                IsEditable = isEditable,
                ItemsSource = new[] { "One", "Two" },
                SelectedIndex = 0,
            };
            AvaloniaCompactDialogChrome.ApplyComboBox(
                comboBox,
                AvaloniaCompactDialogChrome.WindowsStyle);
            comboBox.Template.Should().BeNull(
                "the compact chrome must not replace Avalonia's native ComboBox template");

            var window = new Window { Content = comboBox };
            try
            {
                window.Show();
                comboBox.ApplyTemplate();
                comboBox.Template.Should().NotBeNull();
                comboBox.IsDropDownOpen = true;
                comboBox.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                comboBox.IsDropDownOpen.Should().BeTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}
