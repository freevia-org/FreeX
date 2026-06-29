using System.Windows.Controls;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Shared setup contract for WPF sister-app Backstage hosts.
/// </summary>
public sealed record SisterBackstageHostSpec(
    SisterBackstageTheme Theme,
    Func<SisterBackstageHostController, SisterBackstageEntrySpec> BuildEntries,
    Action OnClosed);

/// <summary>
/// Owns the repeated WPF Backstage host lifecycle, entry construction, and command callback adapters.
/// </summary>
public sealed class SisterBackstageHostController
{
    private BackstageViewShell? _shell;

    public SisterBackstageHostController(UserControl host, SisterBackstageHostSpec spec)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Theme);
        ArgumentNullException.ThrowIfNull(spec.BuildEntries);
        ArgumentNullException.ThrowIfNull(spec.OnClosed);

        var entries = SisterBackstageEntryBuilder.Build(spec.BuildEntries(this));
        _shell = new BackstageViewShell(host, spec.Theme.Accent, entries, spec.OnClosed);
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
        return action;
    }

    public Action HideThen(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return () =>
        {
            Hide();
            action();
        };
    }

    public Action<T> HideThen<T>(Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return value =>
        {
            Hide();
            action(value);
        };
    }

    public Action<T1, T2> HideThen<T1, T2>(Action<T1, T2> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return (first, second) =>
        {
            Hide();
            action(first, second);
        };
    }

    private BackstageViewShell Shell => _shell
        ?? throw new InvalidOperationException("Backstage host controller has not finished initializing.");
}
