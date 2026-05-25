using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

using static Freexcel.App.Host.ChartDialogHelpers;

namespace Freexcel.App.Host;

public sealed record ChartBarFormatDialogResult(int BarGapWidth, int BarOverlap)
{
    public ChartLayoutOptions ToOptions() => new(BarGapWidth: BarGapWidth, BarOverlap: BarOverlap);

    public static ChartBarFormatDialogResult FromChart(ChartModel chart) =>
        CreateResult(chart.BarGapWidth ?? 150, chart.BarOverlap ?? 0);

    public static ChartBarFormatDialogResult CreateResult(int barGapWidth, int barOverlap) =>
        new(Math.Clamp(barGapWidth, 0, 500), Math.Clamp(barOverlap, -100, 100));
}

public sealed class ChartBarFormatDialog : Window
{
    private readonly TextBox _gapWidthBox = new();
    private readonly TextBox _overlapBox = new();

    public ChartBarFormatDialogResult Result { get; private set; }

    public ChartBarFormatDialog(ChartModel chart)
    {
        Result = ChartBarFormatDialogResult.FromChart(chart);
        Title = "Format Bar/Column";
        Width = 340;
        Height = 230;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        var stack = new StackPanel();
        stack.Children.Add(CreateInlineHelp("Gap Width: space between bar clusters as a percentage of bar width (0–500). Overlap: how much adjacent bars overlap (–100 to 100)."));
        ChartDialogHelpers.AddNumericText(stack, "_Gap width %", _gapWidthBox, "Enter a gap width from 0 to 500.");
        ChartDialogHelpers.AddNumericText(stack, "_Overlap %", _overlapBox, "Enter a bar overlap from −100 to 100.");
        root.Children.Add(CreateGroupBox("Bar Options", stack));
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartBarFormatDialogResult result)
    {
        _gapWidthBox.Text = result.BarGapWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _overlapBox.Text = result.BarOverlap.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void FocusInitialKeyboardTarget()
    {
        _gapWidthBox.Focus();
        _gapWidthBox.SelectAll();
        Keyboard.Focus(_gapWidthBox);
    }

    private void Accept()
    {
        if (!TryReadClampedInt(_gapWidthBox, 0, 500, out var gapWidth))
        {
            ShowInvalidInputWarning("Enter a gap width from 0 to 500.", _gapWidthBox);
            return;
        }

        if (!TryReadClampedInt(_overlapBox, -100, 100, out var overlap))
        {
            ShowInvalidInputWarning("Enter a bar overlap from −100 to 100.", _overlapBox);
            return;
        }

        Result = ChartBarFormatDialogResult.CreateResult(gapWidth, overlap);
        DialogResult = true;
    }

    private static bool TryReadClampedInt(TextBox box, int min, int max, out int value)
    {
        value = 0;
        return int.TryParse(box.Text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value)
            && value >= min && value <= max;
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
        return true;
    }
}

public sealed record ChartBubbleFormatDialogResult(int BubbleScale, bool ShowNegativeBubbles, ChartBubbleSizeRepresents BubbleSizeRepresents)
{
    public ChartLayoutOptions ToOptions() => new(
        BubbleScale: BubbleScale,
        ShowNegativeBubbles: ShowNegativeBubbles,
        BubbleSizeRepresents: BubbleSizeRepresents);

    public static ChartBubbleFormatDialogResult FromChart(ChartModel chart) =>
        CreateResult(chart.BubbleScale, chart.ShowNegativeBubbles, chart.BubbleSizeRepresents);

    public static ChartBubbleFormatDialogResult CreateResult(int bubbleScale, bool showNegativeBubbles, ChartBubbleSizeRepresents sizeRepresents) =>
        new(Math.Clamp(bubbleScale, 1, 300), showNegativeBubbles,
            Enum.IsDefined(sizeRepresents) ? sizeRepresents : ChartBubbleSizeRepresents.Area);
}

public sealed class ChartBubbleFormatDialog : Window
{
    private readonly TextBox _bubbleScaleBox = new();
    private readonly CheckBox _negBubblesBox = new() { Content = "Show _negative bubbles" };
    private readonly ComboBox _sizeRepresentsBox = new();

    public ChartBubbleFormatDialogResult Result { get; private set; }

    public ChartBubbleFormatDialog(ChartModel chart)
    {
        Result = ChartBubbleFormatDialogResult.FromChart(chart);
        Title = "Format Bubble Chart";
        Width = 360;
        Height = 270;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        var stack = new StackPanel();
        stack.Children.Add(CreateInlineHelp("Scale all bubbles relative to their default size (1–300). Choose whether bubble area or width represents the data value."));
        ChartDialogHelpers.AddNumericText(stack, "_Bubble scale %", _bubbleScaleBox, "Enter a scale from 1 to 300.");
        ChartDialogHelpers.AddCheck(stack, _negBubblesBox);
        ChartDialogHelpers.AddCombo(stack, "Size _represents", _sizeRepresentsBox, Enum.GetValues<ChartBubbleSizeRepresents>());
        root.Children.Add(CreateGroupBox("Bubble Options", stack));
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartBubbleFormatDialogResult result)
    {
        _bubbleScaleBox.Text = result.BubbleScale.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _negBubblesBox.IsChecked = result.ShowNegativeBubbles;
        _sizeRepresentsBox.SelectedItem = result.BubbleSizeRepresents;
    }

    private void FocusInitialKeyboardTarget()
    {
        _bubbleScaleBox.Focus();
        _bubbleScaleBox.SelectAll();
        Keyboard.Focus(_bubbleScaleBox);
    }

    private void Accept()
    {
        if (!int.TryParse(_bubbleScaleBox.Text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var scale)
            || scale < 1 || scale > 300)
        {
            MessageBox.Show(this, "Enter a bubble scale from 1 to 300.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            _bubbleScaleBox.Focus();
            _bubbleScaleBox.SelectAll();
            Keyboard.Focus(_bubbleScaleBox);
            return;
        }

        Result = ChartBubbleFormatDialogResult.CreateResult(
            scale,
            _negBubblesBox.IsChecked == true,
            ChartDialogHelpers.Selected(_sizeRepresentsBox, ChartBubbleSizeRepresents.Area));
        DialogResult = true;
    }
}
