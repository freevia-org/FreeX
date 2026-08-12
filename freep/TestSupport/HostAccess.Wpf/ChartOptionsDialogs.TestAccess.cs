using System.Globalization;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

public sealed partial class Chart3DViewOptionsDialog
{
    internal Chart3DViewOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(_session.BuildInput(_form.CaptureValues()));

    internal void SetOptionsForTests(Func<Chart3DViewOptionsDialogSession, ChartOptionsDialogValues> buildValues) =>
        _form.ApplyValues(buildValues(_session));
}

public sealed partial class ChartAreaOptionsDialog
{
    internal ChartAreaOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(_session.BuildInput(_form.CaptureValues()), CultureInfo.CurrentCulture);

    internal void SetOptionsForTests(Func<ChartAreaOptionsDialogSession, ChartOptionsDialogValues> buildValues) =>
        _form.ApplyValues(buildValues(_session));
}

public sealed partial class ChartAxisOptionsDialog
{
    internal ChartAxisOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(_session.BuildInput(_form.CaptureValues()));

    internal void SetOptionsForTests(Func<ChartAxisOptionsDialogSession, ChartOptionsDialogValues> buildValues) =>
        _form.ApplyValues(buildValues(_session));
}

public sealed partial class ChartBubbleOptionsDialog
{
    internal ChartBubbleOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(_session.BuildInput(_form.CaptureValues()));

    internal void SetOptionsForTests(Func<ChartBubbleOptionsDialogSession, ChartOptionsDialogValues> buildValues) =>
        _form.ApplyValues(buildValues(_session));
}

public sealed partial class ChartDataTableOptionsDialog
{
    internal ChartDataTableOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(_session.BuildInput(_form.CaptureValues()), CultureInfo.CurrentCulture);

    internal void SetOptionsForTests(Func<ChartDataTableOptionsDialogSession, ChartOptionsDialogValues> buildValues) =>
        _form.ApplyValues(buildValues(_session));
}

public sealed partial class ChartDisplayOptionsDialog
{
    internal ChartDisplayOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(_session.BuildInput(_form.CaptureValues()));

    internal void SetOptionsForTests(Func<ChartDisplayOptionsDialogSession, ChartOptionsDialogValues> buildValues) =>
        _form.ApplyValues(buildValues(_session));
}

public sealed partial class ChartLayoutOptionsDialog
{
    internal ChartLayoutOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(_session.BuildInput(_form.CaptureValues()), CultureInfo.CurrentCulture);

    internal void SetOptionsForTests(Func<ChartLayoutOptionsDialogSession, ChartOptionsDialogValues> buildValues) =>
        _form.ApplyValues(buildValues(_session));
}

public sealed partial class ChartPieOptionsDialog
{
    internal ChartPieOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(_session.BuildInput(_form.CaptureValues()), CultureInfo.CurrentCulture);

    internal void SetOptionsForTests(Func<ChartPieOptionsDialogSession, ChartOptionsDialogValues> buildValues) =>
        _form.ApplyValues(buildValues(_session));

    internal void SetOfPieOptionsForTests(Func<ChartPieOptionsDialogSession, ChartOptionsDialogValues> buildValues) =>
        _form.ApplyValues(buildValues(_session));
}

public sealed partial class ChartPlotStyleOptionsDialog
{
    internal ChartPlotStyleOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(_session.BuildInput(_form.CaptureValues()));

    internal void SetOptionsForTests(Func<ChartPlotStyleOptionsDialogSession, ChartOptionsDialogValues> buildValues) =>
        _form.ApplyValues(buildValues(_session));
}

public sealed partial class ChartPointOptionsDialog
{
    internal ChartPointOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(_session.BuildInput(_form.CaptureValues()));

    internal void SetOptionsForTests(Func<ChartPointOptionsDialogSession, ChartOptionsDialogValues> buildValues) =>
        _form.ApplyValues(buildValues(_session));
}

public sealed partial class ChartProtectionOptionsDialog
{
    internal ChartProtectionOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(_session.BuildInput(_form.CaptureValues()));

    internal void SetOptionsForTests(Func<ChartProtectionOptionsDialogSession, ChartOptionsDialogValues> buildValues) =>
        _form.ApplyValues(buildValues(_session));
}

public sealed partial class ChartSeriesOptionsDialog
{
    internal ChartSeriesOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(_session.BuildInput(_form.CaptureValues()));

    internal void SetOptionsForTests(Func<ChartSeriesOptionsDialogSession, ChartOptionsDialogValues> buildValues) =>
        _form.ApplyValues(buildValues(_session));
}

public sealed partial class ChartTextOptionsDialog
{
    internal ChartTextOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(_session.BuildInput(_form.CaptureValues()));

    internal void SetOptionsForTests(Func<ChartTextOptionsDialogSession, ChartOptionsDialogValues> buildValues) =>
        _form.ApplyValues(buildValues(_session));
}
