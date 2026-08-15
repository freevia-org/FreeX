using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Shell;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// WPF adapter for the shared page-number-format dialog state and validation policy. Keeping the
/// surface as a real shared-chrome dialog makes the production route directly capturable by the
/// paired visual harness instead of hiding it inside a renderer-local static prompt.
/// </summary>
internal sealed class PageNumberFormatDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ComboBox _formatBox;
    private readonly CheckBox _includeChapter;
    private readonly ComboBox _chapterStyleBox;
    private readonly ComboBox _chapterSeparatorBox;
    private readonly RadioButton _continueRadio;
    private readonly TextBox _startBox;
    private readonly TextBlock _status;

    public PageNumberFormatDialog(PageSettings page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var state = PageNumberFormatDialogPlanner.BuildInitialState(page);
        Title = PageNumberFormatDialogPlanner.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;

        _formatBox = new ComboBox
        {
            MinWidth = 180,
            ItemsSource = PageNumberFormatDialogPlanner.FormatItems.Select(item => item.Label).ToArray(),
            SelectedIndex = state.FormatIndex,
            Margin = new Thickness(0, 2, 0, 10),
        };
        _includeChapter = new CheckBox
        {
            Content = PageNumberFormatDialogPlanner.IncludeChapterNumberLabel,
            IsChecked = state.IncludeChapterNumber,
            Margin = new Thickness(0, 0, 0, 6),
        };
        _chapterStyleBox = new ComboBox
        {
            MinWidth = 160,
            ItemsSource = PageNumberFormatDialogPlanner.ChapterStyleItems.Select(item => item.Label).ToArray(),
            SelectedIndex = state.ChapterStyleIndex,
            Margin = new Thickness(0, 2, 0, 8),
        };
        _chapterSeparatorBox = new ComboBox
        {
            MinWidth = 140,
            ItemsSource = PageNumberFormatDialogPlanner.ChapterSeparatorItems.Select(item => item.Label).ToArray(),
            SelectedIndex = state.ChapterSeparatorIndex,
            Margin = new Thickness(0, 2, 0, 10),
        };
        _continueRadio = new RadioButton
        {
            Content = PageNumberFormatDialogPlanner.ContinueLabel,
            GroupName = "PageNumbering",
            IsChecked = state.ContinueFromPreviousSection,
            Margin = new Thickness(0, 2, 0, 4),
        };
        var startRadio = new RadioButton
        {
            Content = PageNumberFormatDialogPlanner.StartAtLabel,
            GroupName = "PageNumbering",
            IsChecked = !state.ContinueFromPreviousSection,
            Margin = new Thickness(0, 2, 8, 4),
        };
        _startBox = new TextBox
        {
            Text = state.StartAtText,
            Width = 72,
            Margin = new Thickness(0, 0, 0, 4),
        };
        _status = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };

        _includeChapter.Checked += (_, _) => UpdateChapterControlState();
        _includeChapter.Unchecked += (_, _) => UpdateChapterControlState();
        UpdateChapterControlState();

        var startRow = new StackPanel { Orientation = Orientation.Horizontal };
        startRow.Children.Add(startRadio);
        startRow.Children.Add(_startBox);

        var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 280 };
        panel.Children.Add(new TextBlock { Text = PageNumberFormatDialogPlanner.NumberFormatLabel });
        panel.Children.Add(_formatBox);
        panel.Children.Add(_includeChapter);
        panel.Children.Add(new TextBlock { Text = PageNumberFormatDialogPlanner.ChapterStartsWithStyleLabel });
        panel.Children.Add(_chapterStyleBox);
        panel.Children.Add(new TextBlock { Text = PageNumberFormatDialogPlanner.ChapterSeparatorLabel });
        panel.Children.Add(_chapterSeparatorBox);
        panel.Children.Add(new TextBlock
        {
            Text = PageNumberFormatDialogPlanner.PageNumberingLabel,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 2),
        });
        panel.Children.Add(_continueRadio);
        panel.Children.Add(startRow);
        panel.Children.Add(_status);
        panel.Children.Add(DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: 72,
            rowMargin: new Thickness(0, 12, 0, 0)));
        Content = panel;
    }

    public PageNumberFormatDialogResult? Result { get; private set; }

    public static PageNumberFormatDialogResult? Prompt(Window? owner, PageSettings page)
    {
        var dialog = new PageNumberFormatDialog(page) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void Accept()
    {
        if (!PageNumberFormatDialogPlanner.TryBuildResult(
                new PageNumberFormatDialogInput(
                    _formatBox.SelectedIndex,
                    _continueRadio.IsChecked == true,
                    _startBox.Text,
                    _includeChapter.IsChecked == true,
                    _chapterStyleBox.SelectedIndex,
                    _chapterSeparatorBox.SelectedIndex),
                out var result,
                out var error))
        {
            _status.Text = error ?? PageNumberFormatDialogPlanner.InvalidStartAtMessage;
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void UpdateChapterControlState()
    {
        var enabled = _includeChapter.IsChecked == true;
        _chapterStyleBox.IsEnabled = enabled;
        _chapterSeparatorBox.IsEnabled = enabled;
    }
}
