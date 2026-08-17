using System.Windows;
using System.Windows.Controls;
using Free.Shared.Shell;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>Thin WPF projection of the shared hyperlink ScreenTip contract.</summary>
internal sealed class ScreenTipDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _screenTip = new() { MinWidth = 280 };

    internal ScreenTipDialog()
        : this(initialScreenTip: null)
    {
    }

    private ScreenTipDialog(string? initialScreenTip)
    {
        var presentation = ScreenTipDialogPlanner.Build(initialScreenTip);
        Title = presentation.Title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;

        _screenTip.Text = presentation.InitialScreenTip;
        _screenTip.ToolTip = presentation.Placeholder;

        var grid = new Grid { Margin = new Thickness(14, 12, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = presentation.Label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        Grid.SetColumn(_screenTip, 1);
        grid.Children.Add(_screenTip);

        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: 78,
            rowMargin: new Thickness(14, 12, 14, 12),
            acceptContent: InsertDialogTextResources.OkButton,
            cancelContent: InsertDialogTextResources.CancelButton));
        Content = outer;

        Loaded += (_, _) => DialogFocus.FocusAndSelect(_screenTip);
    }

    public string? Result { get; private set; }

    public static string? Ask(Window? owner, string? initialScreenTip)
    {
        var dialog = new ScreenTipDialog(initialScreenTip) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void Accept()
    {
        Result = ScreenTipDialogPlanner.PlanAcceptance(_screenTip.Text);
        DialogResult = true;
    }
}
