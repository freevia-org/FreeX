using Avalonia.Controls;
using Avalonia.Threading;

namespace Free.Shared.Shell.Avalonia;

/// <summary>
/// Owns the nested dispatcher pump required when a renderer-neutral synchronous contract must be
/// realized by an Avalonia window.
/// </summary>
public static class AvaloniaSynchronousDialogHost
{
    public static void Show(Window owner, Window dialog, Func<bool> isCompleted)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(isCompleted);

        var wasEnabled = owner.IsEnabled;
        owner.IsEnabled = false;
        try
        {
            dialog.Show(owner);
            while (!isCompleted())
            {
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                if (!isCompleted())
                    Thread.Sleep(1);
            }
        }
        finally
        {
            if (dialog.IsVisible)
                dialog.Close();
            owner.IsEnabled = wasEnabled;
        }
    }
}
