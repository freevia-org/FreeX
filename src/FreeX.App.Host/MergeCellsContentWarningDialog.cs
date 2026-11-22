using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.App.Presentation.Editing;
using FreeX.App.Services;

namespace FreeX.App.Host;

/// <summary>
/// Modal "merging cells will keep only the upper-left value" warning. Extracted verbatim from the
/// inline window MainWindow built for every merge entry point; the caller sets <see cref="Window.Owner"/>
/// before <see cref="Window.ShowDialog"/> and reads <see cref="Choice"/> afterwards (Cancel when the
/// window is dismissed any other way).
/// </summary>
public sealed class MergeCellsContentWarningDialog : Window
{
    public MergeCellContentChoice Choice { get; private set; } = MergeCellContentChoice.Cancel;

    public MergeCellsContentWarningDialog(IReadOnlyList<string?> entryDisplayTexts)
    {
        var presentation = MergeCellsContentWarningPlanner.Create(entryDisplayTexts);
        var keepFirstAction = presentation.Action(MergeCellsContentWarningAction.KeepFirstCell);
        var concatenateAction = presentation.Action(MergeCellsContentWarningAction.ConcatenateAllCells);
        var cancelAction = presentation.Action(MergeCellsContentWarningAction.Cancel);

        Title = presentation.Title;
        Width = 460;
        Height = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        AutomationProperties.SetAutomationId(
            this,
            presentation.DialogAutomationId);

        var root = new StackPanel
        {
            Margin = new Thickness(18),
            Orientation = Orientation.Vertical
        };

        root.Children.Add(new TextBlock
        {
            Text = presentation.PrimaryMessage,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        root.Children.Add(new TextBlock
        {
            Text = presentation.CompactGuidanceMessage,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        if (!string.IsNullOrWhiteSpace(presentation.PreviewText))
        {
            root.Children.Add(new TextBlock
            {
                Text = presentation.PreviewText,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 14)
            });
        }

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };

        var keepFirstButton = new Button
        {
            Content = keepFirstAction.Label,
            MinWidth = 136,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = keepFirstAction.IsDefault
        };
        AutomationProperties.SetAutomationId(
            keepFirstButton,
            keepFirstAction.AutomationId);
        keepFirstButton.Click += (_, _) =>
        {
            Choice = MergeCellContentChoice.KeepFirstCell;
            DialogResult = true;
        };

        var concatenateButton = new Button
        {
            Content = concatenateAction.Label,
            MinWidth = 136,
            Margin = new Thickness(0, 0, 8, 0)
        };
        AutomationProperties.SetAutomationId(
            concatenateButton,
            concatenateAction.AutomationId);
        concatenateButton.Click += (_, _) =>
        {
            Choice = MergeCellContentChoice.ConcatenateAllCells;
            DialogResult = true;
        };

        var cancelButton = new Button
        {
            Content = cancelAction.Label,
            MinWidth = 82,
            IsCancel = cancelAction.IsCancel
        };
        AutomationProperties.SetAutomationId(
            cancelButton,
            cancelAction.AutomationId);
        cancelButton.Click += (_, _) =>
        {
            Choice = MergeCellContentChoice.Cancel;
            DialogResult = false;
        };

        buttonRow.Children.Add(keepFirstButton);
        buttonRow.Children.Add(concatenateButton);
        buttonRow.Children.Add(cancelButton);
        root.Children.Add(buttonRow);

        Content = root;
    }
}
