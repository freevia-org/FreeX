using Avalonia.Controls;

namespace Free.Shared.Shell.Avalonia;

public static class AvaloniaWindowIconLoader
{
    public static bool TryApply(Window window, string resourceFileName)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", resourceFileName);
            if (!File.Exists(iconPath))
                return false;

            using var stream = File.OpenRead(iconPath);
            window.Icon = new WindowIcon(stream);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
