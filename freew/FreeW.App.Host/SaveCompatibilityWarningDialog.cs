using System.Windows;
using System.Windows.Controls;
using Free.Shared.Shell;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Host;

/// <summary>
/// WPF adapter for the shared save-compatibility plan. Layout and control chrome intentionally
/// match the Avalonia adapter while the warning decision and copy remain Presentation-owned.
/// </summary>
internal sealed class SaveCompatibilityWarningDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private SaveCompatibilityWarningDialog(DocumentSaveCompatibilityPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        Title = plan.Title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;

        var message = new TextBlock
        {
            Text = plan.Message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 14, 16, 10),
        };

        var buttons = DialogButtonRowFactory.Create(
            () => DialogResult = true,
            buttonWidth: 90,
            rowMargin: new Thickness(16, 0, 16, 16),
            acceptContent: plan.ContinueButtonText,
            cancelContent: plan.CancelButtonText);

        Content = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                buttons,
                message,
            },
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
    }

    public static bool Show(Window owner, DocumentSaveCompatibilityPlan plan)
    {
        var dialog = new SaveCompatibilityWarningDialog(plan)
        {
            Owner = owner,
        };
        return dialog.ShowDialog() == true;
    }
}
