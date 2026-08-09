using System.Globalization;
using System.Windows;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style plot-area and legend manual-layout dialog.</summary>
public sealed class ChartLayoutOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartLayoutOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    public ChartLayoutOptionsDialog(EditingSession editor)
    {
        _session = new ChartLayoutOptionsDialogSession(editor);
        var plan = _session.BuildDialogPlan(CultureInfo.CurrentCulture);
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

    internal ChartLayoutOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlanForTests(_form.CaptureValues(), CultureInfo.CurrentCulture);

    internal void SetOptionsForTests(ChartLayoutOptionsDialogTestSettings settings) =>
        _form.ApplyValues(_session.BuildTestValues(settings, CultureInfo.CurrentCulture));

    private void OnValueChanged(ChartOptionsDialogFieldId fieldId)
    {
        if (fieldId != ChartOptionsDialogFieldId.LayoutTargetObject)
            return;

        _session.SelectTarget(_form.SelectedIndex(fieldId));
        _form.ApplyPlan(_session.BuildDialogPlan(CultureInfo.CurrentCulture));
    }

    private void OnOk()
    {
        var result = _session.TryCommit(ReadInput(), CultureInfo.CurrentCulture);
        if (result.Succeeded)
        {
            DialogResult = true;
            return;
        }

        MessageBox.Show(this, result.Error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private ChartLayoutOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
