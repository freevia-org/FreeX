using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class HeaderFooterDialog
{
    internal bool ApplyForTests(
        bool showDateTime,
        bool showFooter,
        bool showSlideNumber,
        string footerText,
        HeaderFooterApplyScope scope,
        bool suppressOnTitleSlide = false,
        HeaderFooterDateTimeMode dateTimeMode = HeaderFooterDateTimeMode.AutoUpdate,
        string dateTimeFieldType = "datetime1",
        string fixedDateTimeText = "")
    {
        SetInputForTests(showDateTime, showFooter, showSlideNumber, footerText,
            suppressOnTitleSlide, dateTimeMode, dateTimeFieldType, fixedDateTimeText);
        return Apply(scope);
    }

    internal void SetInputForTests(
        bool showDateTime,
        bool showFooter,
        bool showSlideNumber,
        string footerText,
        bool suppressOnTitleSlide = false,
        HeaderFooterDateTimeMode dateTimeMode = HeaderFooterDateTimeMode.AutoUpdate,
        string dateTimeFieldType = "datetime1",
        string fixedDateTimeText = "")
    {
        var state = _session.SetInput(showDateTime, showFooter, showSlideNumber, footerText,
            suppressOnTitleSlide, dateTimeMode, dateTimeFieldType, fixedDateTimeText);
        _formSession.ApplyState(state);
    }
}

internal sealed partial class SlideSizeDialog
{
    internal void SetInputForTests(string widthText, string heightText, SlideSizeDialogUnit unit)
    {
        _suppressSelectionRefresh = true;
        try
        {
            var state = _session.SetInputUnit(widthText, heightText, unit);
            _inchesRadio.IsChecked = unit == SlideSizeDialogUnit.Inches;
            _centimetersRadio.IsChecked = unit == SlideSizeDialogUnit.Centimeters;
            ApplyDisplay(state.Display);
        }
        finally
        {
            _suppressSelectionRefresh = false;
        }
    }

    internal bool ApplyForTests() => Apply(showValidation: false);
}

internal sealed partial class SlideShowSettingsDialog
{
    internal bool ApplyForTests(
        bool useSlideTimings,
        bool showWithAnimation,
        bool loopUntilStopped,
        PresentationShowType showType = PresentationShowType.PresentedBySpeaker,
        bool showBrowseScrollbar = true,
        uint? kioskRestartAfterMilliseconds = null,
        bool showWithNarration = true,
        bool showMediaControls = true,
        bool showMasterShapes = true)
    {
        _formSession.ApplyInput(SlideShowSettingsDialogSession.CreateInput(
            useSlideTimings, !showWithAnimation, loopUntilStopped,
            SlideShowSettingsDialogSession.ShowTypeIndex(showType), showBrowseScrollbar,
            SlideShowSettingsDialogSession.FormatRestartMilliseconds(kioskRestartAfterMilliseconds),
            showWithNarration, showMediaControls, showMasterShapes));
        return Apply();
    }
}

internal sealed partial class ChartDataDialog
{
    internal ChartDataDialogCommitPlan BuildCommitPlanForTests()
    {
        FlushTextBoxEdits();
        return _session.BuildCommitPlan();
    }

    internal void SwitchRowsAndColumnsForTests()
    {
        FlushTextBoxEdits();
        _session.SwitchRowsAndColumns();
        RebuildTable();
    }

    internal void SetChartTypeForTests(ChartType chartType)
    {
        _session.SetChartType(chartType);
        _chartTypeCombo.SelectedIndex = _session.SelectedChartTypeIndex;
    }

    internal void MoveSeriesForTests(int seriesIndex, bool down)
    {
        FlushTextBoxEdits();
        _session.SelectSeries(seriesIndex);
        MoveActiveSeries(down ? 1 : -1);
    }

    internal void RemoveSeriesForTests(int seriesIndex)
    {
        FlushTextBoxEdits();
        _session.SelectSeries(seriesIndex);
        OnRemoveSeries();
    }

    internal void RemoveCategoryForTests(int categoryIndex)
    {
        FlushTextBoxEdits();
        _session.SelectCategory(categoryIndex);
        OnRemoveCategory();
    }

    internal void MoveCategoryForTests(int categoryIndex, bool right)
    {
        FlushTextBoxEdits();
        _session.SelectCategory(categoryIndex);
        MoveActiveCategory(right ? 1 : -1);
    }

    internal bool PrepareInvalidValueForTests()
    {
        var first = _valueBoxes.FirstOrDefault();
        if (first is null)
            return false;
        first.TextBox.Text = "not-a-number";
        first.TextBox.Focus();
        return !TryFlushTextBoxEdits();
    }
}
