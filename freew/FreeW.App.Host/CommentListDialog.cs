using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Shell;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Host;

/// <summary>
/// Thin WPF projection of the shared comment-list presentation. The row content and empty state are
/// Presentation-owned; this class only materializes the native controls and modal lifetime.
/// </summary>
internal sealed class CommentListDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    public CommentListDialog(IReadOnlyList<CommentListItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var presentation = CommentDialogPresentationPlanner.BuildList(items);
        Title = presentation.Title;
        Width = 460;
        Height = 360;
        ResizeMode = ResizeMode.CanResize;

        var body = new StackPanel { Margin = new Thickness(16) };
        if (presentation.Rows.Count == 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = presentation.EmptyMessage,
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            foreach (var row in presentation.Rows)
                body.Children.Add(BuildRow(row));
        }

        var scroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var buttons = DialogButtonRowFactory.CreateOkOnly(
            Close,
            buttonWidth: 78,
            rowMargin: new Thickness(16, 10, 16, 14),
            acceptContent: CommentDialogPresentationPlanner.Text.CloseActionLabel);
        DockPanel.SetDock(buttons, Dock.Bottom);

        var root = new DockPanel { LastChildFill = true };
        root.Children.Add(buttons);
        root.Children.Add(scroll);
        Content = root;
    }

    public static void Show(Window? owner, IReadOnlyList<CommentListItem> items) =>
        new CommentListDialog(items) { Owner = owner }.ShowDialog();

    private static Border BuildRow(CommentListRowPresentation row)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = row.HeadingText,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = row.Body,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
        });

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 7, 0, 8),
            Child = panel,
        };
    }
}
