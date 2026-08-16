using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using FluentAssertions;

using FreeX.App.Avalonia.Tests.Parity;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Source contract for every <c>HeadlessUnitTestSession.Dispatch(async ...)</c> call in the repo.
///
/// <para><see cref="global::Avalonia.Headless.HeadlessUnitTestSession"/> exposes only three Dispatch
/// overloads -- <c>Dispatch(Action, CancellationToken)</c>, <c>Dispatch&lt;T&gt;(Func&lt;T&gt;,
/// CancellationToken)</c> and <c>Dispatch&lt;T&gt;(Func&lt;Task&lt;T&gt;&gt;, CancellationToken)</c>.
/// There is NO <c>Dispatch(Func&lt;Task&gt;, CancellationToken)</c>. A valueless async lambda
/// (<c>async () =&gt; { ... }</c>) therefore cannot infer <c>T</c> and binds to the <c>Action</c>
/// overload as an <b>async void</b> lambda: Dispatch returns before the body finishes and every
/// exception -- including every failed assertion -- is posted to the synchronization context and
/// dropped. Such a test reports PASSED no matter what it asserts.</para>
///
/// <para>Returning a value from the lambda (the established <c>return true;</c> convention) routes
/// the call through <c>Dispatch&lt;T&gt;(Func&lt;Task&lt;T&gt;&gt;, ...)</c>, which is genuinely
/// awaited and propagates failures. This test fails the build if a valueless async Dispatch lambda
/// is ever reintroduced.</para>
/// </summary>
public sealed class HeadlessDispatchOverloadContractTests
{
    private const string CallMarker = "Dispatch(async";

    [Fact]
    public void EveryAsyncDispatchLambda_ReturnsAValue_SoFailuresPropagate()
    {
        var offenders = new List<string>();
        var root = FunctionalParityMatrix.RepoRoot();
        var scanned = 0;

        foreach (var file in EnumerateSourceFiles(root))
        {
            string text;
            try { text = File.ReadAllText(file); }
            catch (IOException) { continue; }
            if (!text.Contains(CallMarker, StringComparison.Ordinal)) continue;

            var index = 0;
            while ((index = text.IndexOf(CallMarker, index, StringComparison.Ordinal)) >= 0)
            {
                scanned++;
                if (!LambdaReturnsAValue(text, index))
                {
                    var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    offenders.Add($"{relative}:{LineNumberOf(text, index)}");
                }

                index += CallMarker.Length;
            }
        }

        scanned.Should().BeGreaterThan(0, "the scanner must actually find Dispatch(async ...) call sites");
        offenders.Should().BeEmpty(
            "a valueless `async () => {{ ... }}` binds to Dispatch(Action, CancellationToken) as an " +
            "async void lambda, so the test silently passes no matter what it asserts -- end the " +
            "lambda with `return true;` instead. Offending call sites:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        // This file documents the banned shape in prose, so it would match itself.
                        && !Path.GetFileName(path).Equals($"{nameof(HeadlessDispatchOverloadContractTests)}.cs", StringComparison.Ordinal));

    /// <summary>
    /// Returns true when the lambda body opened after the <c>Dispatch(async</c> at
    /// <paramref name="callIndex"/> contains a value-returning <c>return</c> at its own statement
    /// level -- the only shape that binds to the awaited <c>Func&lt;Task&lt;T&gt;&gt;</c> overload.
    /// </summary>
    private static bool LambdaReturnsAValue(string text, int callIndex)
    {
        var arrow = text.IndexOf("=>", callIndex, StringComparison.Ordinal);
        if (arrow < 0) return false;

        var cursor = arrow + 2;
        while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;

        // An expression-bodied `async () => Foo()` is void-returning, hence async void.
        if (cursor >= text.Length || text[cursor] != '{') return false;

        var depth = 0;
        for (var i = cursor; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (i < 0) return false;
                i++;
                continue;
            }

            if (c == '"') { i = SkipString(text, i); continue; }
            if (c == '\'') { i = SkipChar(text, i); continue; }

            if (c == '{') { depth++; continue; }
            if (c == '}')
            {
                depth--;
                if (depth == 0) return false;
                continue;
            }

            if (depth == 1 && IsValueReturnAt(text, i)) return true;
        }

        return false;
    }

    private static bool IsValueReturnAt(string text, int i)
    {
        const string Keyword = "return";
        if (i + Keyword.Length > text.Length) return false;
        if (string.CompareOrdinal(text, i, Keyword, 0, Keyword.Length) != 0) return false;

        if (i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] is '_' or '.')) return false;

        var after = i + Keyword.Length;
        if (after < text.Length && (char.IsLetterOrDigit(text[after]) || text[after] == '_')) return false;

        while (after < text.Length && char.IsWhiteSpace(text[after])) after++;

        // `return;` is void-returning -> still async void.
        return after < text.Length && text[after] != ';';
    }

    private static int SkipString(string text, int quote)
    {
        var verbatim = quote > 0 && text[quote - 1] == '@';
        var i = quote + 1;
        while (i < text.Length)
        {
            if (verbatim)
            {
                if (text[i] == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') i++;
                    else return i;
                }
            }
            else if (text[i] == '\\') i++;
            else if (text[i] == '"') return i;

            i++;
        }

        return text.Length;
    }

    private static int SkipChar(string text, int quote)
    {
        var i = quote + 1;
        while (i < text.Length && text[i] != '\'')
        {
            if (text[i] == '\\') i++;
            i++;
        }

        return i;
    }

    private static int LineNumberOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index; i++)
            if (text[i] == '\n') line++;

        return line;
    }
}
