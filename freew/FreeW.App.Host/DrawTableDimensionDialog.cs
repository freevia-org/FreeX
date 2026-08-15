using System.Windows;
using System.Windows.Controls;
using Free.Shared.Shell;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Host;

/// <summary>
/// Thin WPF projection of the shared draw-table dimension plan. Validation and normalization stay
/// in Presentation; this class owns only native fields and modal lifetime.
/// </summary>
internal sealed class DrawTableDimensionDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _rows;
    private readonly TextBox _columns;

    // Kept parameterless so the visual-evidence harness can construct the production surface.
    internal DrawTableDimensionDialog()
        : this(DrawTableCommandPlanner.BuildDialog(DrawTableDimensionDialogKind.DrawTable, UiText.Get))
    {
    }

    private DrawTableDimensionDialog(DrawTableDimensionDialogPlan plan)
    {
        Title = plan.Title;
        Width = 290;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;

        _rows = new TextBox
        {
            Text = plan.DefaultRows.ToString(),
            Width = 72,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _columns = new TextBox
        {
            Text = plan.DefaultColumns.ToString(),
            Width = 72,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = plan.RowsLabel, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(_rows);
        panel.Children.Add(new TextBlock { Text = plan.ColumnsLabel, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(_columns);
        panel.Children.Add(DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: 72,
            acceptContent: plan.OkLabel,
            cancelContent: plan.CancelLabel));
        Content = panel;

        Loaded += (_, _) => _rows.Focus();
    }

    public (int Rows, int Columns)? Result { get; private set; }

    public static (int Rows, int Columns)? Ask(Window? owner, DrawTableDimensionDialogKind kind)
    {
        var dialog = new DrawTableDimensionDialog(DrawTableCommandPlanner.BuildDialog(kind, UiText.Get))
        {
            Owner = owner,
        };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void Accept()
    {
        Result = DrawTableCommandPlanner.Normalize(_rows.Text, _columns.Text);
        DialogResult = true;
    }
}
