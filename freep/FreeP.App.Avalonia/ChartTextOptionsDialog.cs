using Avalonia.Controls;
using Avalonia.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartTextOptionsDialog : Window
{
    private readonly ChartTextOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    internal ChartTextOptionsDialog(EditingSession editor, ChartTextTarget target = ChartTextTarget.Chart)
    {
        _session = new ChartTextOptionsDialogSession(editor, target);
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

    internal ChartTextOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    internal void SetOptionsForTests(
        string? fontFamily,
        double? fontSizePt,
        bool? bold,
        bool? italic,
        string? color)
    {
        _form.SetText(ChartOptionsDialogFieldId.FontFamily, fontFamily);
        _form.SetText(ChartOptionsDialogFieldId.FontSize, _session.FormatFontSize(fontSizePt));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.Bold, _session.FindBooleanIndex(bold));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.Italic, _session.FindBooleanIndex(italic));
        _form.SetText(ChartOptionsDialogFieldId.TextColor, color);
    }

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        if (result.ShouldClose)
            Close(true);
    }

    private ChartTextOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
