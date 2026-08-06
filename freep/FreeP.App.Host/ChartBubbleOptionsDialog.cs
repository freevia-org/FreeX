using System.Windows;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style bubble chart sizing options dialog.</summary>
public sealed class ChartBubbleOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartBubbleOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    public ChartBubbleOptionsDialog(EditingSession editor)
    {
        _session = new ChartBubbleOptionsDialogSession(editor);
        var plan = _session.BuildDialogPlan();
        _form = ChartOptionsDialogChrome.CreateForm(plan, OnOk, Close);

        Title = plan.Title;
        Width = plan.Width;
        Height = plan.Height;
        MinWidth = plan.MinimumWidth;
        MinHeight = plan.MinimumHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
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
        {
            DialogResult = true;
            return;
        }

        MessageBox.Show(this, result.ValidationMessage, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private ChartBubbleOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
