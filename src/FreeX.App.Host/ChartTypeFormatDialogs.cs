using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using static FreeX.App.Host.ChartDialogHelpers;

namespace FreeX.App.Host;

public sealed record ChartBarFormatDialogResult(int BarGapWidth, int BarOverlap)
{
    public ChartLayoutOptions ToOptions() =>
        ChartBarFormatPlanner.Plan(ToInput());

    public static ChartBarFormatDialogResult FromChart(ChartModel chart) =>
        FromInput(ChartBarFormatPlanner.Read(chart));

    public static ChartBarFormatDialogResult CreateResult(int barGapWidth, int barOverlap) =>
        FromInput(new ChartBarFormatInput(barGapWidth, barOverlap));

    private ChartBarFormatInput ToInput() =>
        new(BarGapWidth, BarOverlap);

    private static ChartBarFormatDialogResult FromInput(ChartBarFormatInput input)
    {
        var normalized = ChartBarFormatPlanner.Normalize(input);
        return new(
            normalized.BarGapWidth,
            normalized.BarOverlap);
    }
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
        if (!ChartBarFormatPlanner.TryParseDialogInput(
                _gapWidthBox.Text,
                _overlapBox.Text,
                out var input,
                out var issue))
        {
            var presentation = ChartValidationPresentationPlanner.Describe(issue);
            ShowInvalidInputWarning(
                presentation.Message.Resolve(UiText.Get, UiText.Format),
                presentation.FocusTarget == ChartBarFormatDialogFieldId.Overlap ? _overlapBox : _gapWidthBox);
            return;
        }

        Result = ChartBarFormatDialogResult.CreateResult(input.BarGapWidth, input.BarOverlap);
        DialogResult = true;
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
        return true;
    }
}

public sealed record ChartPieFormatDialogResult(int FirstSliceAngle, int ExplodedSliceIndex, double ExplodedSliceDistance, double DoughnutHoleSize)
{
    public ChartLayoutOptions ToOptions() =>
        ChartPieFormatPlanner.Plan(ToInput());

    public static ChartPieFormatDialogResult FromChart(ChartModel chart) =>
        FromInput(ChartPieFormatPlanner.Read(chart));

    public static ChartPieFormatDialogResult CreateResult(int firstSliceAngle, int explodedSliceIndex, double explodedSliceDistance, double doughnutHoleSize) =>
        FromInput(new ChartPieFormatInput(firstSliceAngle, explodedSliceIndex, explodedSliceDistance, doughnutHoleSize));

    private ChartPieFormatInput ToInput() =>
        new(FirstSliceAngle, ExplodedSliceIndex, ExplodedSliceDistance, DoughnutHoleSize);

    private static ChartPieFormatDialogResult FromInput(ChartPieFormatInput input)
    {
        var normalized = ChartPieFormatPlanner.Normalize(input);
        return new(
            normalized.FirstSliceAngle,
            normalized.ExplodedSliceIndex,
            normalized.ExplodedSliceDistance,
            normalized.DoughnutHoleSize);
    }
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
        _isDoughnut = ChartPieFormatPlanner.SupportsHoleSize(chart);
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
        _explodedDistBox.Text = ChartPieFormatPlanner.ToDisplayPercent(result.ExplodedSliceDistance).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _holeBox.Text = ChartPieFormatPlanner.ToDisplayPercent(result.DoughnutHoleSize).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void FocusInitialKeyboardTarget()
    {
        _sliceAngleBox.Focus();
        _sliceAngleBox.SelectAll();
        Keyboard.Focus(_sliceAngleBox);
    }

    private void Accept()
    {
        if (!ChartPieFormatPlanner.TryParseDialogInput(
                _sliceAngleBox.Text,
                _explodedIndexBox.Text,
                _explodedDistBox.Text,
                _holeBox.Text,
                _isDoughnut,
                out var input,
                out var issue))
        {
            var presentation = ChartValidationPresentationPlanner.Describe(issue);
            var target = presentation.FocusTarget switch
            {
                ChartPieFormatDialogFieldId.ExplodedSliceIndex => _explodedIndexBox,
                ChartPieFormatDialogFieldId.ExplodedSliceDistance => _explodedDistBox,
                ChartPieFormatDialogFieldId.DoughnutHoleSize => _holeBox,
                _ => _sliceAngleBox
            };
            ShowInvalidInputWarning(presentation.Message.Resolve(UiText.Get, UiText.Format), target);
            return;
        }

        Result = ChartPieFormatDialogResult.CreateResult(
            input.FirstSliceAngle,
            input.ExplodedSliceIndex,
            input.ExplodedSliceDistance,
            input.DoughnutHoleSize);
        DialogResult = true;
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
        return true;
    }

}

public sealed record ChartBubbleFormatDialogResult(int BubbleScale, bool ShowNegativeBubbles, ChartBubbleSizeRepresents BubbleSizeRepresents)
{
    public ChartLayoutOptions ToOptions() =>
        ChartBubbleFormatPlanner.Plan(ToInput());

    public static ChartBubbleFormatDialogResult FromChart(ChartModel chart) =>
        FromInput(ChartBubbleFormatPlanner.Read(chart));

    public static ChartBubbleFormatDialogResult CreateResult(int bubbleScale, bool showNegativeBubbles, ChartBubbleSizeRepresents sizeRepresents) =>
        FromInput(new ChartBubbleFormatInput(bubbleScale, showNegativeBubbles, sizeRepresents));

    private ChartBubbleFormatInput ToInput() =>
        new(BubbleScale, ShowNegativeBubbles, BubbleSizeRepresents);

    private static ChartBubbleFormatDialogResult FromInput(ChartBubbleFormatInput input)
    {
        var normalized = ChartBubbleFormatPlanner.Normalize(input);
        return new(
            normalized.BubbleScale,
            normalized.ShowNegativeBubbles,
            normalized.BubbleSizeRepresents);
    }
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
        ChartDialogHelpers.AddCombo(stack, UiText.Get("ChartBubbleFormat_SizeRepresentsLabel"), _sizeRepresentsBox, ChartBubbleFormatPlanner.GetSizeRepresentsChoices());
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
        if (!ChartBubbleFormatPlanner.TryParseDialogInput(
                _bubbleScaleBox.Text,
                _negBubblesBox.IsChecked == true,
                SelectedSizeRepresents(),
                out var input,
                out var issue))
        {
            var presentation = ChartValidationPresentationPlanner.Describe(issue);
            DialogFocus.ShowWarningAndFocus(
                this,
                presentation.Message.Resolve(UiText.Get, UiText.Format),
                Title,
                _bubbleScaleBox);
            return;
        }

        Result = ChartBubbleFormatDialogResult.CreateResult(
            input.BubbleScale,
            input.ShowNegativeBubbles,
            input.BubbleSizeRepresents);
        DialogResult = true;
    }

    private ChartBubbleSizeRepresents? SelectedSizeRepresents() =>
        _sizeRepresentsBox.SelectedItem is ChartBubbleSizeRepresents value ? value : null;
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
    public ChartLayoutOptions ToOptions() =>
        ChartStockFormatPlanner.Plan(ToInput());

    public static ChartStockFormatDialogResult FromChart(ChartModel chart) =>
        FromInput(ChartStockFormatPlanner.Read(chart));

    public static ChartStockFormatDialogResult CreateResult(
        int upDownBarGapWidth,
        CellColor? upBarFillColor,
        CellColor? upBarBorderColor,
        CellColor? downBarFillColor,
        CellColor? downBarBorderColor,
        CellColor? highLowLineColor,
        double highLowLineThickness) =>
        FromInput(new ChartStockFormatInput(
            upDownBarGapWidth,
            upBarFillColor,
            upBarBorderColor,
            downBarFillColor,
            downBarBorderColor,
            highLowLineColor,
            highLowLineThickness));

    private ChartStockFormatInput ToInput() =>
        new(
            UpDownBarGapWidth,
            UpBarFillColor,
            UpBarBorderColor,
            DownBarFillColor,
            DownBarBorderColor,
            HighLowLineColor,
            HighLowLineThickness);

    private static ChartStockFormatDialogResult FromInput(ChartStockFormatInput input)
    {
        var normalized = ChartStockFormatPlanner.Normalize(input);
        return new(
            normalized.UpDownBarGapWidth,
            normalized.UpBarFillColor,
            normalized.UpBarBorderColor,
            normalized.DownBarFillColor,
            normalized.DownBarBorderColor,
            normalized.HighLowLineColor,
            normalized.HighLowLineThickness);
    }
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
        if (!ChartStockFormatPlanner.TryParseDialogInput(
                _gapWidthBox.Text,
                ChartDialogHelpers.ParseColor(_upFillBox.Text),
                ChartDialogHelpers.ParseColor(_upBorderBox.Text),
                ChartDialogHelpers.ParseColor(_downFillBox.Text),
                ChartDialogHelpers.ParseColor(_downBorderBox.Text),
                ChartDialogHelpers.ParseColor(_highLowColorBox.Text),
                _thicknessBox.Text,
                out var input,
                out var issue))
        {
            var presentation = ChartValidationPresentationPlanner.Describe(issue);
            ShowInvalidInputWarning(
                presentation.Message.Resolve(UiText.Get, UiText.Format),
                presentation.FocusTarget == ChartStockFormatDialogFieldId.HighLowLineThickness
                    ? _thicknessBox
                    : _gapWidthBox);
            return;
        }

        Result = ChartStockFormatDialogResult.CreateResult(
            input.UpDownBarGapWidth,
            input.UpBarFillColor,
            input.UpBarBorderColor,
            input.DownBarFillColor,
            input.DownBarBorderColor,
            input.HighLowLineColor,
            input.HighLowLineThickness);
        DialogResult = true;
    }

    private void ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
    }
}
