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
        var frame = new DispatcherFrame();
        DispatcherTimer? completionTimer = null;
        EventHandler? completionTickHandler = null;
        EventHandler closedHandler = (_, _) => frame.Continue = false;
        try
        {
            dialog.Closed += closedHandler;
            dialog.Show(owner);
            if (!isCompleted() && dialog.IsVisible)
            {
                completionTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(25),
                };
                completionTickHandler = (_, _) =>
                {
                    if (isCompleted() || !dialog.IsVisible)
                    {
                        completionTimer.Stop();
                        frame.Continue = false;
                    }
                };
                completionTimer.Tick += completionTickHandler;
                completionTimer.Start();

                // PushFrame runs Avalonia's real platform loop, including pending OS input.
                // RunJobs must not be used here because it explicitly ignores those events.
                Dispatcher.UIThread.PushFrame(frame);
            }
        }
        finally
        {
            if (completionTimer is not null)
            {
                completionTimer.Stop();
                if (completionTickHandler is not null)
                    completionTimer.Tick -= completionTickHandler;
                completionTimer = null;
            }
            if (closedHandler is not null)
                dialog.Closed -= closedHandler;
            if (dialog.IsVisible)
                dialog.Close();
            owner.IsEnabled = wasEnabled;
        }
    }
}
