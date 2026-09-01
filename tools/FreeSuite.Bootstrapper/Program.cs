using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace FreeSuite.Bootstrapper;

internal static class Program
{
    private static readonly string[] InstallerNames =
    [
        "FreeX-Setup.exe",
        "FreeW-Setup.exe",
        "FreeP-Setup.exe"
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        var silent = args.Any(argument => string.Equals(argument, "--silent", StringComparison.OrdinalIgnoreCase));
        var installRoot = GetOptionValue(args, "--installto");
        var extractionRoot = Path.Combine(Path.GetTempPath(), $"FreeSuite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractionRoot);

        try
        {
            foreach (var installerName in InstallerNames)
            {
                var installerPath = ExtractInstaller(extractionRoot, installerName);
                var appName = Path.GetFileNameWithoutExtension(installerName).Replace("-Setup", string.Empty, StringComparison.Ordinal);
                var installerArguments = "--silent";
                if (!string.IsNullOrWhiteSpace(installRoot))
                {
                    installerArguments += $" --installto \"{Path.Combine(installRoot, appName)}\"";
                }
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = installerArguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process is null)
                {
                    throw new InvalidOperationException($"Could not start {installerName}.");
                }

                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"{installerName} failed with exit code {process.ExitCode}. " +
                        "The applications installed before it remain installed and can be updated independently.");
                }
            }

            if (!silent)
            {
                MessageBox.Show(
                    "FreeX, FreeW, and FreeP were installed successfully.",
                    "Free Suite",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            return 0;
        }
        catch (Exception exception)
        {
            if (!silent)
            {
                MessageBox.Show(exception.Message, "Free Suite installation failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return 1;
        }
        finally
        {
            try
            {
                Directory.Delete(extractionRoot, recursive: true);
            }
            catch
            {
                // Velopack setup processes are awaited, but antivirus software can retain a short-lived handle.
            }
        }
    }

    private static string? GetOptionValue(string[] args, string option)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static string ExtractInstaller(string extractionRoot, string installerName)
    {
        var resourceName = $"FreeSuite.Payload.{installerName}";
        using var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The suite payload is missing {installerName}.");
        var outputPath = Path.Combine(extractionRoot, installerName);
        using var output = File.Create(outputPath);
        input.CopyTo(output);
        return outputPath;
    }
}
