using System.Windows.Controls;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// Shared setup contract for WPF sister-app Backstage hosts.
/// </summary>
public sealed record SisterBackstageHostSpec(
    SisterBackstageTheme Theme,
    Func<SisterBackstageHostController, SisterBackstageEntrySpec> BuildEntries,
    Action OnClosed)
{
    public BackstageFrameChrome? Chrome { get; init; }
}

/// <summary>
/// Owns the repeated WPF Backstage host lifecycle, entry construction, and command callback adapters.
/// </summary>
public sealed class SisterBackstageHostController
{
    private readonly BackstageActionBinder _dismissBeforeDispatch;
    private BackstageViewShell? _shell;

    public SisterBackstageHostController(UserControl host, SisterBackstageHostSpec spec)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Theme);
        ArgumentNullException.ThrowIfNull(spec.BuildEntries);
        ArgumentNullException.ThrowIfNull(spec.OnClosed);

        _dismissBeforeDispatch = BackstageActionBinder.DismissBefore(Hide);
        var entries = SisterBackstageEntryBuilder.Build(spec.BuildEntries(this));
        _shell = new BackstageViewShell(host, spec.Theme.Accent, entries, spec.OnClosed, spec.Chrome);
    }

    public BackstageFrame Frame => Shell.Frame;

    public void Show(string paneLabelOrAutomationId = "Info") => Shell.Show(paneLabelOrAutomationId);

    public void Hide() => Shell.Hide();

    public Action ShowPane(string paneLabelOrAutomationId)
    {
        ArgumentNullException.ThrowIfNull(paneLabelOrAutomationId);
        return () => Show(paneLabelOrAutomationId);
    }

    public Action FrameCommand(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return BackstageActionBinder.Identity.Bind(action);
    }

    public Action HideThen(Action action) => _dismissBeforeDispatch.Bind(action);

    public Action<T> HideThen<T>(Action<T> action) => _dismissBeforeDispatch.Bind(action);

    public Action<T1, T2> HideThen<T1, T2>(Action<T1, T2> action) =>
        _dismissBeforeDispatch.Bind(action);

    private BackstageViewShell Shell => _shell
        ?? throw new InvalidOperationException("Backstage host controller has not finished initializing.");
}
