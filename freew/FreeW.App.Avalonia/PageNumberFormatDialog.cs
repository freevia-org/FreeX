using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed class PageNumberFormatDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

    private readonly ComboBox _formatBox = new() { MinWidth = 190 };
    private readonly RadioButton _continueRadio = new()
    {
        Content = PageNumberFormatDialogPlanner.ContinueLabel,
        GroupName = "PageNumbering",
        Margin = new Thickness(0, 4, 0, 2)
    };
    private readonly RadioButton _startRadio = new()
    {
        Content = PageNumberFormatDialogPlanner.StartAtLabel,
        GroupName = "PageNumbering",
        Margin = new Thickness(0, 4, 8, 2)
    };
    private readonly TextBox _startBox = new() { Width = 72 };
    private readonly TextBlock _status = new();

    public PageNumberFormatDialog(PageSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);

        Title = PageNumberFormatDialogPlanner.Title;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var state = PageNumberFormatDialogPlanner.BuildInitialState(current);
        _formatBox.ItemsSource = PageNumberFormatDialogPlanner.FormatItems.Select(item => item.Label).ToArray();
        _formatBox.SelectedIndex = state.FormatIndex;
        _continueRadio.IsChecked = state.ContinueFromPreviousSection;
        _startRadio.IsChecked = !state.ContinueFromPreviousSection;
        _startBox.Text = state.StartAtText;

        AvaloniaCompactDialogChrome.ApplyComboBox(_formatBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyRadioButton(_continueRadio, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyRadioButton(_startRadio, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_startBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, DialogChromeStyle, new Thickness(0, 8, 0, 0));

        var startRow = new StackPanel { Orientation = Orientation.Horizontal };
        startRow.Children.Add(_startRadio);
        startRow.Children.Add(_startBox);

        var content = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };
        content.Children.Add(new TextBlock { Text = PageNumberFormatDialogPlanner.NumberFormatLabel });
        content.Children.Add(_formatBox);
        content.Children.Add(new TextBlock
        {
            Text = PageNumberFormatDialogPlanner.PageNumberingLabel,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 12, 0, 2)
        });
        content.Children.Add(_continueRadio);
        content.Children.Add(startRow);
        content.Children.Add(new TextBlock
        {
            Text = PageNumberFormatDialogPlanner.ChapterNumberingDeferredLabel,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        });
        content.Children.Add(_status);

        var ok = new Button { Content = "OK", IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 84, isDefault: true);
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 84);
        ok.Click += (_, _) => OnOk();
        cancel.Click += (_, _) => Close(null);
        content.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 14, 0, 0)));

        Content = content;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
        };
    }

    private void OnOk()
    {
        if (!PageNumberFormatDialogPlanner.TryBuildResult(
                new PageNumberFormatDialogInput(
                    _formatBox.SelectedIndex,
                    _continueRadio.IsChecked == true,
                    _startBox.Text),
                out var result,
                out var error))
        {
            _status.Text = error ?? PageNumberFormatDialogPlanner.InvalidStartAtMessage;
            return;
        }

        Close(result);
    }

    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(editor);

        var dialog = new PageNumberFormatDialog(editor.Document.Page);
        var result = await dialog.ShowDialog<PageNumberFormatDialogResult?>(owner);
        if (result is null)
            return;

        editor.ApplyPageNumberFormat(result);
    }
}
