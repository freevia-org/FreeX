using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Shell;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// Thin WPF projection of the shared comment-entry presentation and acceptance policy.
/// </summary>
internal sealed class CommentReplyDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly CommentTextEntryKind _kind;
    private readonly TextBox _text = new()
    {
        AcceptsReturn = true,
        MinHeight = 90,
        Width = 340,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    private readonly TextBlock _status = new()
    {
        Foreground = Brushes.Red,
        Visibility = Visibility.Collapsed,
    };

    // Kept parameterless so the visual-evidence harness can construct the production reply surface.
    internal CommentReplyDialog()
        : this(CommentTextEntryKind.Reply)
    {
    }

    private CommentReplyDialog(CommentTextEntryKind kind)
    {
        _kind = kind;
        var presentation = CommentDialogPresentationPlanner.BuildTextEntry(kind);
        Title = presentation.Title;
        Width = 390;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        _status.Text = presentation.RequiredMessage;

        var body = new StackPanel { Margin = new Thickness(16) };
        body.Children.Add(new TextBlock
        {
            Text = presentation.FieldLabel,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        });
        body.Children.Add(_text);
        body.Children.Add(_status);
        body.Children.Add(DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: 78,
            rowMargin: new Thickness(0, 10, 0, 0),
            acceptContent: presentation.ActionLabel,
            cancelContent: CommentDialogPresentationPlanner.Text.CancelActionLabel));
        Content = body;

        Loaded += (_, _) => _text.Focus();
    }

    public string? Result { get; private set; }

    public static string? Ask(Window? owner, CommentTextEntryKind kind)
    {
        var dialog = new CommentReplyDialog(kind) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void Accept()
    {
        var acceptance = CommentDialogPresentationPlanner.PlanTextAcceptance(_kind, _text.Text);
        if (!acceptance.IsAccepted)
        {
            _status.Text = acceptance.ValidationMessage;
            _status.Visibility = Visibility.Visible;
            return;
        }

        Result = acceptance.Text;
        DialogResult = true;
    }
}
