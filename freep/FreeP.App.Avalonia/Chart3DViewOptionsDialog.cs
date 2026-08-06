using Avalonia.Controls;
using Avalonia.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class Chart3DViewOptionsDialog : Window
{
    private readonly Chart3DViewOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    internal Chart3DViewOptionsDialog(EditingSession editor)
    {
        _session = new Chart3DViewOptionsDialogSession(editor);
        var plan = _session.BuildDialogPlan();
        _form = ChartOptionsDialogChrome.CreateForm(plan, OnOk, () => Close(false));

        Title = plan.Title;
        Width = plan.Width;
        Height = plan.Height + 36;
        MinWidth = plan.MinimumWidth;
        MinHeight = plan.MinimumHeight;
        CanResize = plan.IsResizable;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        Content = _form.Content;
    }

    internal Chart3DViewOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    internal void SetOptionsForTests(
        int? rotationX,
        int? rotationY,
        int? perspective,
        int? heightPercent,
        int? depthPercent,
        bool? rightAngleAxes,
        bool? wireframe,
        int? barGapDepthPercent = null)
    {
        _form.SetText(ChartOptionsDialogFieldId.RotationX, _session.Format(rotationX));
        _form.SetText(ChartOptionsDialogFieldId.RotationY, _session.Format(rotationY));
        _form.SetText(ChartOptionsDialogFieldId.Perspective, _session.Format(perspective));
        _form.SetText(ChartOptionsDialogFieldId.HeightPercent, _session.Format(heightPercent));
        _form.SetText(ChartOptionsDialogFieldId.DepthPercent, _session.Format(depthPercent));
        _form.SetText(ChartOptionsDialogFieldId.BarGapDepthPercent, _session.Format(barGapDepthPercent));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.RightAngleAxes, _session.FindBooleanIndex(rightAngleAxes));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.Wireframe, _session.FindBooleanIndex(wireframe));
    }

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        Close(result.ShouldClose);
    }

    private Chart3DViewOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
