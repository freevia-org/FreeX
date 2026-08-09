using System.Globalization;
using FreeX.ParityCompare.Core;

namespace FreeX.ParityCompare;

/// <summary>Parsed command-line options for the parity runner.</summary>
public sealed class CliOptions
{
    public string? OutDir { get; private set; }
    public string? WinDir { get; private set; }
    public string? LinDir { get; private set; }
    public bool WinOnly { get; private set; }
    public bool LinuxOnly { get; private set; }
    public bool SkipCapture { get; private set; }
    public bool ShowHelp { get; private set; }
    public double Threshold { get; private set; } = SurfaceComparer.DefaultHardThreshold;
    public string DockerImage { get; private set; } = "ubuntu:24.04";

    public const string HelpText =
        """
        FreeX.ParityCompare — cross-platform visual parity runner.

        Usage:
          dotnet run --project tools/FreeX.ParityCompare -- [flags]

        Flags:
          --out <dir>         Report output dir (default: artifacts/parity-report)
          --win-only          Capture + compare Windows only (skip Linux/Docker)
          --linux-only        Capture Linux only (skip Windows)
          --skip-capture      Compare existing capture dirs; run neither shell
          --win-dir <dir>     Windows capture dir (default: <out>/capture-win)
          --lin-dir <dir>     Linux capture dir   (default: <out>/capture-linux/out)
          --threshold <pct>   Hard grid-fidelity fail threshold % (default: 5.0)
          --docker-image <i>  Docker image for the Linux run (default: ubuntu:24.04)
          -h | --help         Show this help
        """;

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help": o.ShowHelp = true; break;
                case "--win-only": o.WinOnly = true; break;
                case "--linux-only": o.LinuxOnly = true; break;
                case "--skip-capture": o.SkipCapture = true; break;
                case "--out": o.OutDir = Next(args, ref i); break;
                case "--win-dir": o.WinDir = Next(args, ref i); break;
                case "--lin-dir": o.LinDir = Next(args, ref i); break;
                case "--docker-image": o.DockerImage = Next(args, ref i) ?? o.DockerImage; break;
                case "--threshold":
                    var t = Next(args, ref i);
                    if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        o.Threshold = v;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    break;
            }
        }
        if (o.WinOnly && o.LinuxOnly)
        {
            Console.Error.WriteLine("--win-only and --linux-only are mutually exclusive; ignoring both.");
            o.WinOnly = o.LinuxOnly = false;
        }
        return o;
    }

    private static string? Next(string[] args, ref int i) =>
        i + 1 < args.Length ? args[++i] : null;
}
