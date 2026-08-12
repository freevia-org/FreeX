using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

public sealed partial class HeaderFooterDialog
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
        Apply(scope);
        return LastApplyPlan?.ShouldApply == true;
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

public sealed partial class SlideSizeDialog
{
    internal void SetInputForTests(string widthText, string heightText, SlideSizeDialogUnit unit)
    {
        _suppressPresetRefresh = true;
        try
        {
            var state = _session.SetInputUnit(widthText, heightText, unit);
            _inchesRadio.IsChecked = unit == SlideSizeDialogUnit.Inches;
            _cmRadio.IsChecked = unit == SlideSizeDialogUnit.Centimeters;
            ApplyDisplay(state.Display);
        }
        finally
        {
            _suppressPresetRefresh = false;
        }
    }

    internal bool ApplyForTests() => Apply(showValidationDialog: false);
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

public sealed partial class ChartDataDialog
{
    internal ChartDataDialogCommitPlan BuildCommitPlanForTests()
    {
        if (!TryFlushPendingEdits())
            throw new InvalidOperationException("The chart data grid contains an invalid value.");
        return _session.BuildCommitPlan();
    }

    internal bool PrepareInvalidValueForTests()
    {
        if (_grid.Items.Count == 0 || _grid.Columns.Count < 2)
            return false;
        _grid.CurrentCell = new DataGridCellInfo(_grid.Items[0], _grid.Columns[1]);
        _grid.ScrollIntoView(_grid.Items[0], _grid.Columns[1]);
        _grid.Focus();
        if (!_grid.BeginEdit())
            return false;
        UpdateLayout();
        var editor = FindVisualDescendants<TextBox>(_grid).FirstOrDefault(box => box.IsKeyboardFocusWithin)
            ?? FindVisualDescendants<TextBox>(_grid).FirstOrDefault();
        if (editor is null)
            return false;
        editor.Text = "not-a-number";
        editor.Focus();
        var committed = TryFlushPendingEdits();
        return !committed && !string.IsNullOrWhiteSpace(_validationText.Text);
    }
}
