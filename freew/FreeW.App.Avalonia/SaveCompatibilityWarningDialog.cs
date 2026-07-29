using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Avalonia;

internal sealed class SaveCompatibilityWarningDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;

    private SaveCompatibilityWarningDialog(DocumentSaveCompatibilityPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        Title = plan.Title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var message = new TextBlock
        {
            Text = plan.Message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 14, 16, 10),
        };

        var continueButton = new Button
        {
            Content = plan.ContinueButtonText,
            IsDefault = true,
        };
        AvaloniaCompactDialogChrome.ApplyButton(continueButton, DialogChromeStyle, minWidth: 90, isDefault: true);
        continueButton.Click += (_, _) => Close(true);

        var cancelButton = new Button
        {
            Content = plan.CancelButtonText,
            IsCancel = true,
        };
        AvaloniaCompactDialogChrome.ApplyButton(cancelButton, DialogChromeStyle, minWidth: 90);
        cancelButton.Click += (_, _) => Close(false);

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(
            [continueButton, cancelButton],
            new Thickness(16, 0, 16, 16));

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

    public static Task<bool> ShowAsync(Window owner, DocumentSaveCompatibilityPlan plan) =>
        new SaveCompatibilityWarningDialog(plan).ShowDialog<bool>(owner);
}
