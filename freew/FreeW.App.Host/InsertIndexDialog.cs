using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Host;

/// <summary>Thin WPF host for Word's References &gt; Insert Index dialog.</summary>
internal sealed class InsertIndexDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _identifier;
    private readonly string _actionLabel;
    private InsertIndexDialogResult? _result;

    private InsertIndexDialog(Window? owner, InsertIndexDialogState initialState, bool isUpdate)
    {
        Owner = owner;
        Title = isUpdate ? InsertIndexDialogPlanner.UpdateTitle : InsertIndexDialogPlanner.Title;
        _actionLabel = isUpdate
            ? InsertIndexDialogPlanner.UpdateButtonLabel
            : InsertIndexDialogPlanner.InsertButtonLabel;
        Width = InsertIndexDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _identifier = new TextBox
        {
            MinWidth = 320,
            Text = initialState.Identifier,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var panel = new StackPanel { Margin = new Thickness(16, 14, 16, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = InsertIndexDialogPlanner.IdentifierLabel,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(_identifier);
        panel.Children.Add(new TextBlock
        {
            Text = InsertIndexDialogPlanner.IdentifierHint,
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: 80,
            acceptContent: _actionLabel,
            rowMargin: new Thickness(0, 8, 0, 12)));
        Content = panel;

        Loaded += (_, _) => DialogFocus.FocusAndSelect(_identifier);
    }

    private void Accept(bool closeOnSuccess = true)
    {
        _result = InsertIndexDialogPlanner.BuildResult(new InsertIndexDialogState(_identifier.Text ?? string.Empty));
        if (closeOnSuccess)
            Close();
    }

    private void Accept() =>
        Accept(closeOnSuccess: true);

    internal static InsertIndexDialog CreateForTest(string? identifier = null) =>
        new(null, InsertIndexDialogPlanner.BuildInitialState(identifier), isUpdate: false);

    internal static InsertIndexDialog CreateForUpdateTest(string? identifier = null) =>
        new(null, InsertIndexDialogPlanner.BuildInitialState(identifier), isUpdate: true);

    internal void SetIdentifierForTest(string? identifier) =>
        _identifier.Text = identifier;

    internal void AcceptForTest() =>
        Accept(closeOnSuccess: false);

    internal InsertIndexDialogResult? ResultForTest => _result;

    internal string ActionLabelForTest => _actionLabel;

    public static InsertIndexDialogResult? Prompt(Window? owner, InsertIndexDialogState initialState)
    {
        var dialog = new InsertIndexDialog(owner, initialState, isUpdate: false);
        dialog.ShowDialog();
        return dialog._result;
    }

    public static InsertIndexDialogResult? PromptForUpdate(Window? owner, InsertIndexDialogState initialState)
    {
        var dialog = new InsertIndexDialog(owner, initialState, isUpdate: true);
        dialog.ShowDialog();
        return dialog._result;
    }
}
