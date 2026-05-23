using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Freexcel.App.Host;

public sealed partial class TextToColumnsDialog
{
    private void FixedWidthRuler_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_fixedWidthButton.IsChecked != true)
            return;

        var positions = ParseFixedWidthBreakPositions(_fixedWidthBreaksBox.Text);
        var x = e.GetPosition(_fixedWidthRuler).X;
        var nearest = FindNearestBreakIndex(positions, x, tolerance: 8);
        _dragBreakIndex = nearest >= 0
            ? nearest
            : AddFixedWidthBreakAt(x);
        _fixedWidthRuler.CaptureMouse();
        e.Handled = true;
    }

    private void FixedWidthRuler_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragBreakIndex is not { } index || e.LeftButton != MouseButtonState.Pressed)
            return;

        var positions = ParseFixedWidthBreakPositions(_fixedWidthBreaksBox.Text);
        UpdateFixedWidthBreakPositions(MoveFixedWidthBreakPosition(
            positions,
            index,
            PositionFromRulerX(e.GetPosition(_fixedWidthRuler).X),
            FixedWidthMaxLength()));
        _dragBreakIndex = FindNearestBreakIndex(ParseFixedWidthBreakPositions(_fixedWidthBreaksBox.Text), e.GetPosition(_fixedWidthRuler).X, tolerance: double.MaxValue);
        e.Handled = true;
    }

    private void FixedWidthRuler_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragBreakIndex = null;
        _fixedWidthRuler.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void FixedWidthRuler_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_fixedWidthButton.IsChecked != true)
            return;

        var positions = ParseFixedWidthBreakPositions(_fixedWidthBreaksBox.Text);
        var nearest = FindNearestBreakIndex(positions, e.GetPosition(_fixedWidthRuler).X, tolerance: 10);
        if (nearest >= 0)
            UpdateFixedWidthBreakPositions(RemoveFixedWidthBreakPosition(positions, nearest));
        e.Handled = true;
    }

    private int AddFixedWidthBreakAt(double x)
    {
        var positions = ParseFixedWidthBreakPositions(_fixedWidthBreaksBox.Text);
        var position = Math.Clamp(PositionFromRulerX(x), 1, FixedWidthMaxLength() - 1);
        var updated = AddFixedWidthBreakPosition(positions, position, FixedWidthMaxLength());
        UpdateFixedWidthBreakPositions(updated);
        return updated.ToList().IndexOf(position);
    }

    private int FindNearestBreakIndex(IReadOnlyList<int> positions, double x, double tolerance)
    {
        var nearestIndex = -1;
        var nearestDistance = double.MaxValue;
        for (var index = 0; index < positions.Count; index++)
        {
            var distance = Math.Abs(RulerXFromPosition(positions[index]) - x);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = index;
            }
        }

        return nearestDistance <= tolerance ? nearestIndex : -1;
    }

    private void UpdateFixedWidthBreakPositions(IReadOnlyList<int> positions)
    {
        _suppressFixedWidthSync = true;
        try
        {
            _fixedWidthBreaksBox.Text = string.Join(",", positions);
        }
        finally
        {
            _suppressFixedWidthSync = false;
        }

        RefreshPreview();
    }

    private void RefreshFixedWidthRuler()
    {
        _fixedWidthRuler.Children.Clear();
        var sample = _previewRows.OrderByDescending(row => row.Length).FirstOrDefault() ?? string.Empty;
        var text = new TextBlock
        {
            Text = sample,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Margin = new Thickness(4, 28, 4, 0)
        };
        _fixedWidthRuler.Children.Add(text);

        for (var tick = 1; tick < FixedWidthMaxLength(); tick++)
        {
            var x = RulerXFromPosition(tick);
            var line = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 0,
                Y2 = tick % 5 == 0 ? 10 : 6,
                Stroke = Brushes.Gray,
                StrokeThickness = 1
            };
            _fixedWidthRuler.Children.Add(line);
        }

        foreach (var position in ParseFixedWidthBreakPositions(_fixedWidthBreaksBox.Text))
        {
            var x = RulerXFromPosition(position);
            var line = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 0,
                Y2 = 56,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };
            _fixedWidthRuler.Children.Add(line);
        }
    }

    private int FixedWidthMaxLength() =>
        Math.Max(2, _previewRows.Count == 0 ? 2 : _previewRows.Max(row => row.Length));

    private int PositionFromRulerX(double x)
    {
        var width = RulerWidth();
        return (int)Math.Round(Math.Clamp(x, 0, width) / width * FixedWidthMaxLength());
    }

    private double RulerXFromPosition(int position)
    {
        return Math.Clamp(position, 0, FixedWidthMaxLength()) / (double)FixedWidthMaxLength() * RulerWidth();
    }

    private double RulerWidth() => _fixedWidthRuler.ActualWidth > 1 ? _fixedWidthRuler.ActualWidth : 440;
}
