using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Guards the headless-capture memory contract: every test that closes a <c>MainWindow</c> must first
/// call <see cref="MainWindow.AllowCloseWithoutDirtyPromptForParityCapture"/>.
///
/// <para>
/// <c>MainWindow_Closing</c> cancels the close and fires an unawaited confirmation dialog when
/// <c>_session.IsDirty</c> is true. No headless test ever answers that dialog, so <c>Close()</c>
/// silently becomes a no-op and the whole window graph -- ribbon, grid, session, and the Avalonia
/// text-layout objects the headless backend never disposes -- stays rooted for the rest of the test
/// process. That is the exact mechanism behind the CaptureTests projects' observed 22 GB+ working-set
/// blowups (r130), and it fails as a slow leak rather than as a failing assertion, which is why it
/// needs a source contract rather than a runtime one.
/// </para>
/// </summary>
public sealed class HeadlessMainWindowCloseGuardSourceTests
{
    private const string GuardCall = "AllowCloseWithoutDirtyPromptForParityCapture";

    private static readonly Regex MainWindowConstruction =
        new(@"(?:var|MainWindow)\s+(\w+)\s*=\s*new\s+MainWindow\s*\(", RegexOptions.Compiled);

    private static readonly Regex MainWindowParameter =
        new(@"MainWindow\s+(\w+)\s*[,)]", RegexOptions.Compiled);

    [Fact]
    public void EveryMainWindowCloseSite_SuppressesTheDirtyWorkbookPromptFirst()
    {
        var violations = new List<string>();

        foreach (var path in Directory.EnumerateFiles(TestSourceDirectory(), "*.cs"))
        {
            var lines = File.ReadAllLines(path);
            var fileName = Path.GetFileName(path);

            foreach (var (start, end) in MemberChunks(lines))
            {
                var chunk = string.Join("\n", lines[start..(end + 1)]);

                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match match in MainWindowConstruction.Matches(chunk))
                    names.Add(match.Groups[1].Value);
                foreach (Match match in MainWindowParameter.Matches(chunk))
                    names.Add(match.Groups[1].Value);

                foreach (var name in names)
                {
                    var escaped = Regex.Escape(name);
                    if (Regex.IsMatch(chunk, $@"\b{escaped}\.{GuardCall}\b"))
                        continue;

                    for (var i = start; i <= end; i++)
                    {
                        if (Regex.IsMatch(lines[i], $@"(^|[^\w.]){escaped}\.Close\(\)"))
                            violations.Add($"{fileName}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        violations.Should().BeEmpty(
            "a MainWindow closed without " + GuardCall + "() leaks its entire window graph for the " +
            "rest of the test process when the session is dirty. Add the call immediately before the " +
            "close, as the surrounding tests already do:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Splits a test file into top-level member bodies so that a variable named <c>window</c> in one
    /// test is never confused with a plain <see cref="Avalonia.Controls.Window"/> of the same name in
    /// another. A member body ends at a line that is exactly four spaces and a closing brace.
    /// </summary>
    private static IEnumerable<(int Start, int End)> MemberChunks(string[] lines)
    {
        var start = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i] != "    }")
                continue;

            yield return (start, i);
            start = i + 1;
        }

        if (start < lines.Length)
            yield return (start, lines.Length - 1);
    }

    private static string TestSourceDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
                return Path.Combine(directory.FullName, "tests", "FreeX.App.Avalonia.Tests");
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
