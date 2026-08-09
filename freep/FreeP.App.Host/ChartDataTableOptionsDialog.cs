using System.Globalization;
using System.Windows;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart data-table options dialog.</summary>
public sealed class ChartDataTableOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartDataTableOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    public ChartDataTableOptionsDialog(EditingSession editor)
    {
        _session = new ChartDataTableOptionsDialogSession(editor);
        var plan = _session.BuildDialogPlan(CultureInfo.CurrentCulture);
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

    internal ChartDataTableOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlanForTests(_form.CaptureValues(), CultureInfo.CurrentCulture);

    internal void SetOptionsForTests(ChartDataTableOptionsDialogTestSettings settings) =>
        _form.ApplyValues(_session.BuildTestValues(settings, CultureInfo.CurrentCulture));

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

    private ChartDataTableOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
