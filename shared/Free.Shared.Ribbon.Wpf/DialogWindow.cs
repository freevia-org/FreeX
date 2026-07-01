namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Compatibility wrapper for callers that still import the historical ribbon namespace.
/// Dialog chrome and resources live in <see cref="Free.Shared.Shell.Wpf.DialogWindow"/>.
/// </summary>
public abstract class DialogWindow : Free.Shared.Shell.Wpf.DialogWindow
{
}
