using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Dialog for setting floating image position offsets and anchors. Returns null if the user cancels.
/// </summary>
internal sealed class ImagePositionDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _hBox, _vBox;
    private readonly ComboBox _hAnchorBox, _vAnchorBox;
    private (double HOffset, double VOffset, HorizontalAnchor HAnchor, VerticalAnchor VAnchor)? _result;

    private ImagePositionDialog(
        Window? owner,
        double hOffPt,
        double vOffPt,
        HorizontalAnchor hAnchor,
        VerticalAnchor vAnchor,
        string title,
        bool isGroupLocal)
    {
        var surface = ImagePositionDialogPlanner.Surface;
        Owner = owner;
        Title = title;
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ImageChartDialogSurfaceSemantics.Apply(this, surface with { AutomationName = title });

        var state = ImagePositionDialogPlanner.BuildInitialState(
            hOffPt,
            vOffPt,
            hAnchor,
            vAnchor,
            CultureInfo.CurrentCulture);

        _hBox = new TextBox { Text = state.HorizontalOffsetText, MinWidth = 80 };
        _vBox = new TextBox { Text = state.VerticalOffsetText, MinWidth = 80 };

        _hAnchorBox = Combo(ImagePositionDialogPlanner.HorizontalAnchorItems, state.HorizontalAnchorIndex);
        _vAnchorBox = Combo(ImagePositionDialogPlanner.VerticalAnchorItems, state.VerticalAnchorIndex);
        _hAnchorBox.IsEnabled = !isGroupLocal;
        _vAnchorBox.IsEnabled = !isGroupLocal;
        ImageChartDialogSurfaceSemantics.Apply(_hBox, surface.Field(ImagePositionDialogField.HorizontalOffset));
        ImageChartDialogSurfaceSemantics.Apply(_hAnchorBox, surface.Field(ImagePositionDialogField.HorizontalAnchor));
        ImageChartDialogSurfaceSemantics.Apply(_vBox, surface.Field(ImagePositionDialogField.VerticalOffset));
        ImageChartDialogSurfaceSemantics.Apply(_vAnchorBox, surface.Field(ImagePositionDialogField.VerticalAnchor));

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        static void Place(Grid g, UIElement el, int row, int col)
        {
            Grid.SetRow(el, row); Grid.SetColumn(el, col); g.Children.Add(el);
        }

        Place(grid, Label(surface.Field(ImagePositionDialogField.HorizontalOffset).Label), 0, 0); Place(grid, _hBox, 0, 1);
        Place(grid, Label(surface.Field(ImagePositionDialogField.HorizontalAnchor).Label), 1, 0); Place(grid, _hAnchorBox, 1, 1);
        Place(grid, Label(surface.Field(ImagePositionDialogField.VerticalOffset).Label),   2, 0); Place(grid, _vBox, 2, 1);
        Place(grid, Label(surface.Field(ImagePositionDialogField.VerticalAnchor).Label),   3, 0); Place(grid, _vAnchorBox, 3, 1);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Place(grid, buttons, 4, 1);

        Content = grid;
        DialogFocus.FocusAndSelect(_hBox);
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 0) };

    private static ComboBox Combo<TValue>(IReadOnlyList<ImageDialogChoice<TValue>> items, int selectedIndex)
    {
        var cb = new ComboBox { MinWidth = 100 };
        foreach (var item in items)
            cb.Items.Add(item.Label);
        cb.SelectedIndex = selectedIndex;
        return cb;
    }

    private void Accept()
    {
        if (!ImagePositionDialogPlanner.TryBuildResult(
                new ImagePositionDialogInput(
                    _hBox.Text,
                    _vBox.Text,
                    _hAnchorBox.SelectedIndex,
                    _vAnchorBox.SelectedIndex),
                CultureInfo.CurrentCulture,
                out var result,
                out var validation))
        {
            DialogMessageHelper.ShowWarning(
                this,
                validation?.Message ?? ImagePositionDialogPlanner.OffsetValidationMessage);
            return;
        }

        _result = (result!.HorizontalOffset, result.VerticalOffset, result.HorizontalAnchor, result.VerticalAnchor);
        Close();
    }

    /// <summary>Show the position dialog. Returns offsets + anchors, or null if cancelled.</summary>
    public static (double HOffset, double VOffset, HorizontalAnchor HAnchor, VerticalAnchor VAnchor)? Prompt(
        Window? owner,
        double hOffPt,
        double vOffPt,
        HorizontalAnchor hAnchor,
        VerticalAnchor vAnchor,
        string title = ImagePositionDialogPlanner.DefaultTitle,
        bool isGroupLocal = false)
    {
        var dialog = new ImagePositionDialog(
            owner, hOffPt, vOffPt, hAnchor, vAnchor, title, isGroupLocal);
        dialog.ShowDialog();
        return dialog._result;
    }
}
