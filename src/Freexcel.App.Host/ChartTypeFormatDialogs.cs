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

public sealed record ChartPieFormatDialogResult(int FirstSliceAngle, int ExplodedSliceIndex, double ExplodedSliceDistance, double DoughnutHoleSize)
{
    public ChartLayoutOptions ToOptions() => new(
        FirstSliceAngle: FirstSliceAngle,
        ExplodedSliceIndex: ExplodedSliceIndex,
        ExplodedSliceDistance: ExplodedSliceDistance,
        DoughnutHoleSize: DoughnutHoleSize);

    public static ChartPieFormatDialogResult FromChart(ChartModel chart) =>
        CreateResult(
            (int)chart.FirstSliceAngle,
            chart.ExplodedSliceIndex,
            chart.ExplodedSliceDistance,
            chart.DoughnutHoleSize);

    public static ChartPieFormatDialogResult CreateResult(int firstSliceAngle, int explodedSliceIndex, double explodedSliceDistance, double doughnutHoleSize) =>
        new(Math.Clamp(firstSliceAngle, 0, 359),
            explodedSliceIndex,
            Math.Clamp(explodedSliceDistance, 0, 0.5),
            Math.Clamp(doughnutHoleSize, 0.1, 0.9));
}

public sealed class ChartPieFormatDialog : Window
{
    private readonly TextBox _sliceAngleBox = new();
    private readonly TextBox _explodedIndexBox = new();
    private readonly TextBox _explodedDistBox = new();
    private readonly TextBox _holeBox = new();
    private readonly bool _isDoughnut;

    public ChartPieFormatDialogResult Result { get; private set; }

    public ChartPieFormatDialog(ChartModel chart)
    {
        _isDoughnut = ChartTypeSupport.SupportsDoughnutHoleSize(chart.Type);
        Result = ChartPieFormatDialogResult.FromChart(chart);
        Title = "Format Pie/Doughnut";
        Width = 360;
        Height = _isDoughnut ? 310 : 270;
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
        stack.Children.Add(CreateInlineHelp("First slice angle: rotation of the first slice in degrees (0–359). Explode: index of a single exploded slice (−1 = none) and its distance (0–50%)."));
        ChartDialogHelpers.AddNumericText(stack, "_First slice angle °", _sliceAngleBox, "Enter an angle from 0 to 359.");
        ChartDialogHelpers.AddNumericText(stack, "E_xploded slice index (−1 = none)", _explodedIndexBox, "Enter a slice index or −1 for none.");
        ChartDialogHelpers.AddNumericText(stack, "_Exploded distance %", _explodedDistBox, "Enter a distance from 0 to 50.");
        if (_isDoughnut)
            ChartDialogHelpers.AddNumericText(stack, "_Hole size %", _holeBox, "Enter a hole size from 10 to 90.");
        root.Children.Add(CreateGroupBox("Pie / Doughnut Options", stack));
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartPieFormatDialogResult result)
    {
        _sliceAngleBox.Text = result.FirstSliceAngle.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _explodedIndexBox.Text = result.ExplodedSliceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _explodedDistBox.Text = ((int)Math.Round(result.ExplodedSliceDistance * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _holeBox.Text = ((int)Math.Round(result.DoughnutHoleSize * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void FocusInitialKeyboardTarget()
    {
        _sliceAngleBox.Focus();
        _sliceAngleBox.SelectAll();
        Keyboard.Focus(_sliceAngleBox);
    }

    private void Accept()
    {
        if (!int.TryParse(_sliceAngleBox.Text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var angle)
            || angle < 0 || angle > 359)
        {
            ShowInvalidInputWarning("Enter a first slice angle from 0 to 359.", _sliceAngleBox);
            return;
        }

        if (!int.TryParse(_explodedIndexBox.Text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var explodedIndex))
        {
            ShowInvalidInputWarning("Enter a slice index or −1 for none.", _explodedIndexBox);
            return;
        }

        if (!int.TryParse(_explodedDistBox.Text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var explodedDistPct)
            || explodedDistPct < 0 || explodedDistPct > 50)
        {
            ShowInvalidInputWarning("Enter an exploded distance from 0 to 50.", _explodedDistBox);
            return;
        }

        var holePct = 55;
        if (_isDoughnut)
        {
            if (!int.TryParse(_holeBox.Text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out holePct)
                || holePct < 10 || holePct > 90)
            {
                ShowInvalidInputWarning("Enter a hole size from 10 to 90.", _holeBox);
                return;
            }
        }

        Result = ChartPieFormatDialogResult.CreateResult(angle, explodedIndex, explodedDistPct / 100.0, holePct / 100.0);
        DialogResult = true;
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

public sealed record ChartStockFormatDialogResult(
    int UpDownBarGapWidth,
    CellColor? UpBarFillColor,
    CellColor? UpBarBorderColor,
    CellColor? DownBarFillColor,
    CellColor? DownBarBorderColor,
    CellColor? HighLowLineColor,
    double HighLowLineThickness)
{
    public ChartLayoutOptions ToOptions() => new(
        UpDownBarGapWidth: UpDownBarGapWidth,
        UpBarFillColor: UpBarFillColor,
        UpBarBorderColor: UpBarBorderColor,
        DownBarFillColor: DownBarFillColor,
        DownBarBorderColor: DownBarBorderColor,
        HighLowLineColor: HighLowLineColor,
        HighLowLineThickness: HighLowLineThickness);

    public static ChartStockFormatDialogResult FromChart(ChartModel chart) =>
        CreateResult(
            chart.UpDownBarGapWidth ?? 150,
            chart.UpBarFillColor,
            chart.UpBarBorderColor,
            chart.DownBarFillColor,
            chart.DownBarBorderColor,
            chart.HighLowLineColor,
            chart.HighLowLineThickness);

    public static ChartStockFormatDialogResult CreateResult(
        int upDownBarGapWidth,
        CellColor? upBarFillColor,
        CellColor? upBarBorderColor,
        CellColor? downBarFillColor,
        CellColor? downBarBorderColor,
        CellColor? highLowLineColor,
        double highLowLineThickness) =>
        new(Math.Clamp(upDownBarGapWidth, 0, 500),
            upBarFillColor,
            upBarBorderColor,
            downBarFillColor,
            downBarBorderColor,
            highLowLineColor,
            Math.Clamp(highLowLineThickness, 0.5, 10.0));
}

public sealed class ChartStockFormatDialog : Window
{
    private readonly TextBox _gapWidthBox = new();
    private readonly TextBox _upFillBox = new();
    private readonly TextBox _upBorderBox = new();
    private readonly TextBox _downFillBox = new();
    private readonly TextBox _downBorderBox = new();
    private readonly TextBox _highLowColorBox = new();
    private readonly TextBox _thicknessBox = new();

    public ChartStockFormatDialogResult Result { get; private set; }

    public ChartStockFormatDialog(ChartModel chart)
    {
        Result = ChartStockFormatDialogResult.FromChart(chart);
        Title = "Format Stock Chart";
        Width = 380;
        Height = 490;
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
        stack.Children.Add(CreateInlineHelp("Up/down bar gap width (0–500%). Colors accept #RRGGBB hex or \"none\". High-low line thickness (0.5–10 pt)."));
        ChartDialogHelpers.AddNumericText(stack, "_Gap width %", _gapWidthBox, "Enter a gap width from 0 to 500.");
        ChartDialogHelpers.AddColorText(stack, "_Up bar fill", _upFillBox);
        ChartDialogHelpers.AddColorText(stack, "U_p bar border", _upBorderBox);
        ChartDialogHelpers.AddColorText(stack, "_Down bar fill", _downFillBox);
        ChartDialogHelpers.AddColorText(stack, "D_own bar border", _downBorderBox);
        ChartDialogHelpers.AddColorText(stack, "_High-low line color", _highLowColorBox);
        ChartDialogHelpers.AddNumericText(stack, "_Line thickness pt", _thicknessBox, "Enter a thickness from 0.5 to 10.");
        root.Children.Add(CreateGroupBox("Stock Options", stack));
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartStockFormatDialogResult result)
    {
        _gapWidthBox.Text = result.UpDownBarGapWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _upFillBox.Text = ChartDialogHelpers.FormatColor(result.UpBarFillColor);
        _upBorderBox.Text = ChartDialogHelpers.FormatColor(result.UpBarBorderColor);
        _downFillBox.Text = ChartDialogHelpers.FormatColor(result.DownBarFillColor);
        _downBorderBox.Text = ChartDialogHelpers.FormatColor(result.DownBarBorderColor);
        _highLowColorBox.Text = ChartDialogHelpers.FormatColor(result.HighLowLineColor);
        _thicknessBox.Text = result.HighLowLineThickness.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void FocusInitialKeyboardTarget()
    {
        _gapWidthBox.Focus();
        _gapWidthBox.SelectAll();
        Keyboard.Focus(_gapWidthBox);
    }

    private void Accept()
    {
        if (!int.TryParse(_gapWidthBox.Text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var gapWidth)
            || gapWidth < 0 || gapWidth > 500)
        {
            ShowInvalidInputWarning("Enter a gap width from 0 to 500.", _gapWidthBox);
            return;
        }

        if (!double.TryParse(_thicknessBox.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var thickness)
            || thickness < 0.5 || thickness > 10.0)
        {
            ShowInvalidInputWarning("Enter a line thickness from 0.5 to 10.", _thicknessBox);
            return;
        }

        Result = ChartStockFormatDialogResult.CreateResult(
            gapWidth,
            ChartDialogHelpers.ParseColor(_upFillBox.Text),
            ChartDialogHelpers.ParseColor(_upBorderBox.Text),
            ChartDialogHelpers.ParseColor(_downFillBox.Text),
            ChartDialogHelpers.ParseColor(_downBorderBox.Text),
            ChartDialogHelpers.ParseColor(_highLowColorBox.Text),
            thickness);
        DialogResult = true;
    }

    private void ShowInvalidInputWarning(string message, TextBox target)
    {
        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }
}
