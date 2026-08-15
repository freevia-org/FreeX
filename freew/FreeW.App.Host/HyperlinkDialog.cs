using System.Windows;
using System.Windows.Controls;
using Free.Shared.Shell;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>Thin WPF projection of the shared Insert/Edit Hyperlink contract.</summary>
internal sealed class HyperlinkDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _display = new() { MinWidth = 280 };
    private readonly TextBox _address = new() { MinWidth = 280 };

    // Kept parameterless so the visual-evidence harness can construct the production surface.
    internal HyperlinkDialog()
        : this(HyperlinkDialogMode.Insert, initialDisplayText: null, initialAddress: null)
    {
    }

    private HyperlinkDialog(
        HyperlinkDialogMode mode,
        string? initialDisplayText,
        string? initialAddress)
    {
        var presentation = HyperlinkDialogPlanner.Build(mode, initialDisplayText, initialAddress);
        Title = presentation.Title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;

        _display.Text = presentation.InitialDisplayText;
        _display.ToolTip = presentation.DisplayPlaceholder;
        _address.Text = presentation.InitialAddress;
        _address.ToolTip = presentation.AddressPlaceholder;

        var grid = new Grid { Margin = new Thickness(14, 12, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddLabeledRow(grid, 0, presentation.DisplayLabel, _display);
        AddLabeledRow(grid, 1, presentation.AddressLabel, _address);

        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: 78,
            rowMargin: new Thickness(14, 12, 14, 12),
            acceptContent: InsertDialogTextResources.OkButton,
            cancelContent: InsertDialogTextResources.CancelButton));
        Content = outer;

        Loaded += (_, _) => (string.IsNullOrEmpty(_display.Text) ? _display : _address).Focus();
    }

    public HyperlinkDialogAcceptance? Result { get; private set; }

    public static HyperlinkDialogAcceptance? Ask(
        Window? owner,
        HyperlinkDialogMode mode,
        string? initialDisplayText,
        string? initialAddress)
    {
        var dialog = new HyperlinkDialog(mode, initialDisplayText, initialAddress) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void Accept()
    {
        var acceptance = HyperlinkDialogPlanner.PlanAcceptance(_display.Text, _address.Text);
        if (!acceptance.IsAccepted)
        {
            _address.Focus();
            return;
        }

        Result = acceptance;
        DialogResult = true;
    }

    private static void AddLabeledRow(Grid grid, int row, string label, Control editor)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, row == 0 ? 8 : 0),
        };
        editor.Margin = new Thickness(0, 0, 0, row == 0 ? 8 : 0);
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(text);
        grid.Children.Add(editor);
    }
}
