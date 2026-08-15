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
    public async Task Compact_combo_template_registers_required_parts_and_lays_out(bool isEditable)
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
            comboBox.Template.Should().NotBeNull(
                "compact dialog ComboBoxes own stable framework-required template parts");

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
                comboBox.ContainerFromIndex(0).Should().NotBeNull();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}
