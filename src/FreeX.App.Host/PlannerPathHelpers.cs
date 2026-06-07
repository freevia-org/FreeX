using System.IO;

namespace FreeX.App.Host;

internal static class PlannerPathHelpers
{
    public static bool TryGetExtension(string? path, out string extension)
    {
        if (string.IsNullOrWhiteSpace(path) || HasInvalidPathChars(path))
        {
            extension = "";
            return false;
        }

        try
        {
            extension = Path.GetExtension(path) ?? "";
            return !string.IsNullOrWhiteSpace(extension);
        }
        catch (ArgumentException)
        {
            extension = "";
            return false;
        }
        catch (NotSupportedException)
        {
            extension = "";
            return false;
        }
        catch (PathTooLongException)
        {
            extension = "";
            return false;
        }
    }

    public static bool HasInvalidPathChars(string path) =>
        path.IndexOf('\0') >= 0;
}
