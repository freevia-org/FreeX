using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// r604: neither host may choose a culture for a number the user typed.
///
/// <para>The WPF ribbon passed <c>CultureInfo.CurrentCulture</c> to the font-size parser and the
/// Avalonia ribbon passed <c>CultureInfo.InvariantCulture</c>, so the same box in the same app
/// accepted "10,5" on Windows and silently ignored it on Linux and macOS. A divergence like that is
/// invisible to a functional test that runs on one platform, and invisible to the parity capture
/// too, because both hosts LOOK identical -- they differ only in what they accept.</para>
///
/// <para>So the contract is on the source: a host ribbon may not name a CultureInfo next to a value
/// the user typed. It routes through the shared typed-entry parser instead, which is the single
/// place that decides.</para>
/// </summary>
public sealed class R604_BothHostsReadTypedNumbersTheSameWayTests(ITestOutputHelper output)
{
    private static readonly string[] HostRibbonFiles =
    [
        Path.Combine("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"),
        Path.Combine("freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"),
    ];

    private static string StripComments(string text)
    {
        var blockFree = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(blockFree, @"^[^\S\n]*//.*$", "", RegexOptions.Multiline);
    }

    [Fact]
    public void NoHostRibbonPicksACultureForTypedInput()
    {
        var root = TestWorkspaceFileLocator.FindContainingDirectory("FreeX.slnx");
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var relative in HostRibbonFiles)
        {
            var path = Path.Combine(root, relative);

            // Non-vacuity: both files must exist. A renamed host file would otherwise make this
            // pass while checking nothing.
            File.Exists(path).Should().BeTrue($"{relative} is one of the two host ribbons this contract compares");
            scanned++;

            var text = StripComments(File.ReadAllText(path));
            foreach (Match match in Regex.Matches(
                         text,
                         @"TryParse\w*\s*\((?<args>[^;]{0,300}?)\)",
                         RegexOptions.Singleline))
            {
                var args = match.Groups["args"].Value;
                if (!args.Contains("CultureInfo", StringComparison.Ordinal))
                    continue;

                var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetFileName(path)}:{line}: {Regex.Replace(args.Trim(), @"\s+", " ")}");
            }
        }

        scanned.Should().Be(2);

        foreach (var offender in offenders)
            output.WriteLine(offender);

        offenders.Should().BeEmpty(
            "a host ribbon naming a CultureInfo next to typed input is how the two platforms came to "
            + "disagree about what the font-size box accepts. Use the shared typed-entry parser:\n"
            + string.Join("\n", offenders));
    }
}
