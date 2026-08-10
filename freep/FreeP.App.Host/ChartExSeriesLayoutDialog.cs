using System.Windows;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

public sealed class ChartExSeriesLayoutDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartExSeriesLayoutDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    public ChartExSeriesLayoutDialog(EditingSession editor)
    {
        _session = new ChartExSeriesLayoutDialogSession(editor);
        var plan = _session.BuildDialogPlan();
        _form = ChartOptionsDialogChrome.CreateForm(plan, OnOk, Close, OnValueChanged);

        Title = plan.Title;
        Width = plan.Width;
        Height = plan.Height;
        MinWidth = plan.MinimumWidth;
        MinHeight = plan.MinimumHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Content = _form.Content;
    }

    internal int SelectedSeriesIndexForTests => _session.SelectedSeriesIndex;
    internal string? SelectedLayoutIdForTests => _session.LayoutIdAt(SelectedLayoutIndex);

    internal void ApplyForTests()
    {
        if (!_session.TryApply(SelectedLayoutIndex, out var error))
            throw new ArgumentException(error);
    }

    private int SelectedLayoutIndex =>
        _form.SelectedIndex(ChartOptionsDialogFieldId.ChartExLayout);

    private void OnValueChanged(ChartOptionsDialogFieldId fieldId)
    {
        if (fieldId != ChartOptionsDialogFieldId.ChartExSeries)
            return;

        _session.SelectSeries(_form.SelectedIndex(fieldId));
        _form.ApplyPlan(_session.BuildDialogPlan());
    }

    private void OnOk()
    {
        if (!_session.TryApply(SelectedLayoutIndex, out var error))
        {
            DialogMessageHelper.ShowWarning(this, error, Title);
            return;
        }

        DialogResult = true;
    }
}
