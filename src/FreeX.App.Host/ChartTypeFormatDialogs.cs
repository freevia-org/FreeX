using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using static FreeX.App.Host.ChartDialogHelpers;

namespace FreeX.App.Host;

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
        Title = UiText.Get("ChartBarFormat_Title");
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
        stack.Children.Add(CreateInlineHelp(UiText.Get("ChartBarFormat_HelpText")));
        ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartBarFormat_GapWidthLabel"), _gapWidthBox, UiText.Get("ChartBarFormat_GapWidthHelpText"));
        ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartBarFormat_OverlapLabel"), _overlapBox, UiText.Get("ChartBarFormat_OverlapHelpText"));
        root.Children.Add(CreateGroupBox(UiText.Get("ChartBarFormat_OptionsGroup"), stack));
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
            ShowInvalidInputWarning(UiText.Get("ChartBarFormat_InvalidGapWidthMessage"), _gapWidthBox);
            return;
        }

        if (!TryReadClampedInt(_overlapBox, -100, 100, out var overlap))
        {
            ShowInvalidInputWarning(UiText.Get("ChartBarFormat_InvalidOverlapMessage"), _overlapBox);
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
        DialogMessageHelper.ShowWarning(this, message, Title);
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
        Title = UiText.Get("ChartPieFormat_Title");
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
        stack.Children.Add(CreateInlineHelp(UiText.Get("ChartPieFormat_HelpText")));
        ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartPieFormat_FirstSliceAngleLabel"), _sliceAngleBox, UiText.Get("ChartPieFormat_FirstSliceAngleHelpText"));
        ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartPieFormat_ExplodedSliceIndexLabel"), _explodedIndexBox, UiText.Get("ChartPieFormat_ExplodedSliceIndexHelpText"));
        ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartPieFormat_ExplodedDistanceLabel"), _explodedDistBox, UiText.Get("ChartPieFormat_ExplodedDistanceHelpText"));
        if (_isDoughnut)
            ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartPieFormat_HoleSizeLabel"), _holeBox, UiText.Get("ChartPieFormat_HoleSizeHelpText"));
        root.Children.Add(CreateGroupBox(UiText.Get("ChartPieFormat_OptionsGroup"), stack));
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
            ShowInvalidInputWarning(UiText.Get("ChartPieFormat_InvalidFirstSliceAngleMessage"), _sliceAngleBox);
            return;
        }

        if (!int.TryParse(_explodedIndexBox.Text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var explodedIndex))
        {
            ShowInvalidInputWarning(UiText.Get("ChartPieFormat_InvalidExplodedSliceIndexMessage"), _explodedIndexBox);
            return;
        }

        if (!int.TryParse(_explodedDistBox.Text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var explodedDistPct)
            || explodedDistPct < 0 || explodedDistPct > 50)
        {
            ShowInvalidInputWarning(UiText.Get("ChartPieFormat_InvalidExplodedDistanceMessage"), _explodedDistBox);
            return;
        }

        var holePct = 55;
        if (_isDoughnut)
        {
            if (!int.TryParse(_holeBox.Text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out holePct)
                || holePct < 10 || holePct > 90)
            {
                ShowInvalidInputWarning(UiText.Get("ChartPieFormat_InvalidHoleSizeMessage"), _holeBox);
                return;
            }
        }

        Result = ChartPieFormatDialogResult.CreateResult(angle, explodedIndex, explodedDistPct / 100.0, holePct / 100.0);
        DialogResult = true;
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogMessageHelper.ShowWarning(this, message, Title);
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
    private readonly CheckBox _negBubblesBox = new() { Content = UiText.Get("ChartBubbleFormat_ShowNegativeBubbles") };
    private readonly ComboBox _sizeRepresentsBox = new();

    public ChartBubbleFormatDialogResult Result { get; private set; }

    public ChartBubbleFormatDialog(ChartModel chart)
    {
        Result = ChartBubbleFormatDialogResult.FromChart(chart);
        Title = UiText.Get("ChartBubbleFormat_Title");
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
        stack.Children.Add(CreateInlineHelp(UiText.Get("ChartBubbleFormat_HelpText")));
        ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartBubbleFormat_BubbleScaleLabel"), _bubbleScaleBox, UiText.Get("ChartBubbleFormat_BubbleScaleHelpText"));
        ChartDialogHelpers.AddCheck(stack, _negBubblesBox);
        ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartBubbleFormat_SizeRepresentsLabel"), _sizeRepresentsBox, Enum.GetValues<ChartBubbleSizeRepresents>());
        root.Children.Add(CreateGroupBox(UiText.Get("ChartBubbleFormat_OptionsGroup"), stack));
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
            DialogMessageHelper.ShowWarning(this, UiText.Get("ChartBubbleFormat_InvalidBubbleScaleMessage"), Title);
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
        Title = UiText.Get("ChartStockFormat_Title");
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
        stack.Children.Add(CreateInlineHelp(UiText.Get("ChartStockFormat_HelpText")));
        ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartStockFormat_GapWidthLabel"), _gapWidthBox, UiText.Get("ChartStockFormat_GapWidthHelpText"));
        ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartStockFormat_UpBarFillLabel"), _upFillBox);
        ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartStockFormat_UpBarBorderLabel"), _upBorderBox);
        ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartStockFormat_DownBarFillLabel"), _downFillBox);
        ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartStockFormat_DownBarBorderLabel"), _downBorderBox);
        ChartDialogHelpers.AddColorText(stack, UiText.Get("ChartStockFormat_HighLowLineColorLabel"), _highLowColorBox);
        ChartDialogHelpers.AddNumericText(stack, UiText.Get("ChartStockFormat_LineThicknessLabel"), _thicknessBox, UiText.Get("ChartStockFormat_LineThicknessHelpText"));
        root.Children.Add(CreateGroupBox(UiText.Get("ChartStockFormat_OptionsGroup"), stack));
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
            ShowInvalidInputWarning(UiText.Get("ChartStockFormat_InvalidGapWidthMessage"), _gapWidthBox);
            return;
        }

        if (!double.TryParse(_thicknessBox.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var thickness)
            || thickness < 0.5 || thickness > 10.0)
        {
            ShowInvalidInputWarning(UiText.Get("ChartStockFormat_InvalidLineThicknessMessage"), _thicknessBox);
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
        DialogMessageHelper.ShowWarning(this, message, Title);
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }
}
