using Avalonia.Controls;
using Avalonia.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartBubbleOptionsDialog : Window
{
    private readonly ChartBubbleOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    internal ChartBubbleOptionsDialog(EditingSession editor)
    {
        _session = new ChartBubbleOptionsDialogSession(editor);
        var plan = _session.BuildDialogPlan();
        _form = ChartOptionsDialogChrome.CreateForm(plan, OnOk, () => Close(false));

        Title = plan.Title;
        Width = plan.Width;
        Height = plan.Height;
        MinWidth = plan.MinimumWidth;
        MinHeight = plan.MinimumHeight;
        CanResize = plan.IsResizable;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        Content = _form.Content;
    }

    internal ChartBubbleOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    internal void SetOptionsForTests(
        int bubbleScalePercent,
        BubbleSizeRepresentation sizeRepresents,
        bool showNegativeBubbles)
    {
        _form.SetText(ChartOptionsDialogFieldId.BubbleScale, _session.Format(bubbleScalePercent));
        _form.SetSelectedIndex(
            ChartOptionsDialogFieldId.BubbleSizeRepresents,
            _session.FindSizeRepresentsIndex(sizeRepresents));
        _form.SetChecked(ChartOptionsDialogFieldId.ShowNegativeBubbles, showNegativeBubbles);
    }

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        if (result.ShouldClose)
            Close(true);
    }

    private ChartBubbleOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
