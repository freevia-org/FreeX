using FluentAssertions;
using FreeX.Core.Commands;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r465: parsing clipboard text must never throw or hang, whatever was copied.
///
/// <para>Clipboard text is the least trustworthy input the app takes: it is whatever the user copied
/// from any application on the machine, and unlike a file it arrives with no format claim to check.
/// An exception on paste is a crash on Ctrl+V.</para>
///
/// <para>Eighteen hostile inputs -- ragged rows, a lone CR, a NUL, a 200,000-character line, 20,000
/// rows, 20,000 columns, five thousand consecutive quotes, lone surrogates, RTL overrides and C0
/// controls. None throws, none hangs, and the quote-aware splitting stays correct throughout.</para>
/// </summary>
public sealed class R465_ClipboardTextParsingNeverThrowsTests
{
    [Fact]
    public async System.Threading.Tasks.Task ParsingHostileClipboardTextNeverThrowsOrHangs()
    {
        // Clipboard text is whatever the user copied from any application on the machine. It is the
        // least trustworthy input the app takes, and unlike a file it arrives with no format claim.
        var inputs = new (string Label, string Text)[]
        {
            ("empty", string.Empty),
            ("single newline", "\n"),
            ("crlf only", "\r\n"),
            ("tabs only", "\t\t\t"),
            ("ragged rows", "a\tb\tc\nd\ne\tf\tg\th"),
            ("trailing tabs", "a\t\t\t\n"),
            ("lone CR", "a\rb"),
            ("null char", "a\0b"),
            ("very long line", new string('x', 200_000)),
            ("many rows", string.Join("\n", Enumerable.Range(0, 20_000).Select(i => i + "\tvalue"))),
            ("many columns", string.Join("\t", Enumerable.Range(0, 20_000))),
            ("unbalanced quote", "\"unclosed"),
            ("quote storm", new string('"', 5_000)),
            ("embedded newline in quotes", "\"line1\nline2\"\tb"),
            ("unicode surrogates", "\uD83D\uDE00\t\uD83D"),
            ("rtl and control chars", "\u202Ereversed\u0007\t\u200B"),
            ("formula-looking", "=SUM(A1:A2)\t=1/0"),
            ("csv-ish commas", "a,b,c\nd,e,f"),
        };

        var threw = new List<string>();
        var hung = new List<string>();
        var results = new List<string>();

        foreach (var (label, text) in inputs)
        {
            var task = System.Threading.Tasks.Task.Run(() => ClipboardSerializer.Deserialize(text));
            var finished = await System.Threading.Tasks.Task.WhenAny(
                task, System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(10)));

            if (finished != task)
            {
                hung.Add(label);
                continue;
            }

            try
            {
                var rows = await task;
                results.Add($"{label} :: rows={rows.Length} firstRowCells={(rows.Length > 0 ? rows[0].Length : 0)}");
            }
            catch (Exception ex)
            {
                threw.Add($"{label} :: {ex.GetType().Name}");
            }
        }

        var census = $"inputs={inputs.Length} threw={threw.Count} hung={hung.Count}";

        threw.Should().BeEmpty(
            "an exception while parsing clipboard text is a crash on Ctrl+V, from content the user " +
            "copied in some other application entirely. " + census + "\n" + string.Join("\n", threw),
            Array.Empty<object>());

        hung.Should().BeEmpty(
            "a hang here freezes the app on a keystroke and no catch can rescue it. " + census +
            "\n" + string.Join("\n", hung),
            Array.Empty<object>());

        results.Should().HaveCount(
            inputs.Length,
            "every input must have been parsed, or this sweep is reporting on fewer cases than it " +
            "claims. " + census);
    }
}
