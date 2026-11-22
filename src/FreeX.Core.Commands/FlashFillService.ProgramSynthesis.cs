using System.Globalization;
using System.Text;

namespace FreeX.Core.Commands;

/// <summary>
/// General Flash Fill program synthesis (the "FlashMeta"-style core Excel's Flash Fill is built on): learn a
/// program that is a concatenation of constant strings and substrings of the input column(s), where the
/// substring boundaries are token / delimiter / absolute position expressions, optionally case-converted.
/// All training examples must be reproduced; the cheapest consistent program is applied to the remaining rows.
/// Used by <see cref="FlashFillService"/> as the broad fallback behind the specialised single-purpose detectors
/// and as the multi-column engine (initials, reorderings, literal concatenation, delimiter slicing, ...).
/// </summary>
public static partial class FlashFillService
{
    private const int SynthMaxLength = 96;
    private const int SynthMaxNodes = 60_000;
    private const double SynthConstOverhead = 0.2;
    private const double SynthConstAlnumCharCost = 5.5;
    private const double SynthConstPunctCharCost = 0.4;
    private const double SynthSubBaseCost = 3.8;
    private const double SynthAllConstPenalty = 60.0;
    private const double SynthLoopCost = 3.5;

    private enum SynthCase { None, Upper, Lower, Proper }

    private enum SynthTokenClass { Alpha, Digit, Alnum }

    private abstract record SynthPos
    {
        public abstract int? Eval(string s);
        public abstract double Cost { get; }
    }

    private sealed record SynthAbsPos(int K, bool FromEnd) : SynthPos
    {
        public override int? Eval(string s)
        {
            var p = FromEnd ? s.Length - K : K;
            return p >= 0 && p <= s.Length ? p : null;
        }

        public override double Cost => FromEnd && K == 0 ? 0.3 : 0.9;
    }

    private sealed record SynthTokenPos(SynthTokenClass Class, int N, bool End, int Offset) : SynthPos
    {
        public override int? Eval(string s)
        {
            var tokens = SynthTokens(s, Class);
            var index = N > 0 ? N - 1 : tokens.Count + N;
            if (index < 0 || index >= tokens.Count)
                return null;

            var p = (End ? tokens[index].End : tokens[index].Start) + Offset;
            return p >= 0 && p <= s.Length ? p : null;
        }

        public override double Cost => Offset == 0 ? (N == 1 ? 0.28 : N == -1 ? 0.29 : 0.3) : Offset > 0 ? 0.7 : 0.9;
    }

    private sealed record SynthDelimPos(char Delimiter, int N, bool After) : SynthPos
    {
        public override int? Eval(string s)
        {
            var occurrences = new List<int>();
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] == Delimiter)
                    occurrences.Add(i);
            }

            var index = N > 0 ? N - 1 : occurrences.Count + N;
            if (index < 0 || index >= occurrences.Count)
                return null;

            return occurrences[index] + (After ? 1 : 0);
        }

        public override double Cost => 0.5;
    }

    private abstract record SynthAtom
    {
        public abstract string? Eval(IReadOnlyList<string> inputs);
        public abstract double Cost { get; }
    }

    private sealed record SynthSubAtom(int Column, SynthPos Start, SynthPos End, SynthCase Case) : SynthAtom
    {
        public int SampleLength { get; init; }

        public override string? Eval(IReadOnlyList<string> inputs)
        {
            if (Column >= inputs.Count)
                return null;

            var s = inputs[Column];
            if (Start.Eval(s) is not { } a || End.Eval(s) is not { } b || b <= a)
                return null;

            return SynthApplyCase(s.Substring(a, b - a), Case);
        }

        public override double Cost
        {
            get
            {
                var cost = SynthSubBaseCost + Start.Cost + End.Cost + (Case == SynthCase.None ? 0 : 0.3);
                // Pure character-offset slices are the weakest evidence (short ones are usually coincidence).
                if (Start is SynthAbsPos && End is SynthAbsPos)
                    cost += 0.8 + 12.0 / (1 + SampleLength);
                return cost;
            }
        }
    }

    private sealed record SynthLoopInitialsAtom(int Column, string Between, string After, SynthCase Case) : SynthAtom
    {
        public override string? Eval(IReadOnlyList<string> inputs)
        {
            if (Column >= inputs.Count)
                return null;

            var tokens = SynthTokens(inputs[Column], SynthTokenClass.Alpha);
            if (tokens.Count == 0)
                return null;

            var builder = new StringBuilder();
            for (var i = 0; i < tokens.Count; i++)
            {
                if (i > 0)
                    builder.Append(Between);
                builder.Append(SynthApplyCase(inputs[Column].Substring(tokens[i].Start, 1), Case));
            }

            return builder.Append(After).ToString();
        }

        public override double Cost => SynthLoopCost;
    }

    private sealed record SynthConstAtom(string Text) : SynthAtom
    {
        public override string? Eval(IReadOnlyList<string> inputs) => Text;
        public override double Cost => SynthConstCost(Text);
    }

    private static double SynthConstCost(string text)
    {
        var cost = SynthConstOverhead;
        foreach (var ch in text)
            cost += char.IsLetterOrDigit(ch) ? SynthConstAlnumCharCost : SynthConstPunctCharCost;
        return cost;
    }

    private readonly record struct SynthToken(int Start, int End);

    private static List<SynthToken> SynthTokens(string s, SynthTokenClass tokenClass)
    {
        var tokens = new List<SynthToken>();
        var i = 0;
        while (i < s.Length)
        {
            if (!SynthInClass(s[i], tokenClass))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < s.Length && SynthInClass(s[i], tokenClass))
                i++;
            tokens.Add(new SynthToken(start, i));
        }

        return tokens;
    }

    private static bool SynthInClass(char ch, SynthTokenClass tokenClass) => tokenClass switch
    {
        SynthTokenClass.Alpha => char.IsLetter(ch),
        SynthTokenClass.Digit => char.IsDigit(ch),
        _ => char.IsLetterOrDigit(ch),
    };

    private static string SynthApplyCase(string s, SynthCase transform)
    {
        switch (transform)
        {
            case SynthCase.Upper:
                return s.ToUpperInvariant();
            case SynthCase.Lower:
                return s.ToLowerInvariant();
            case SynthCase.Proper:
                var builder = new StringBuilder(s.Length);
                var startOfWord = true;
                foreach (var ch in s)
                {
                    builder.Append(startOfWord ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch));
                    startOfWord = !char.IsLetter(ch);
                }

                return builder.ToString();
            default:
                return s;
        }
    }

    /// <summary>A learned program plus the columns it reads.</summary>
    private sealed record SynthProgram(IReadOnlyList<SynthAtom> Atoms)
    {
        public string? Run(IReadOnlyList<string> inputs)
        {
            var builder = new StringBuilder();
            foreach (var atom in Atoms)
            {
                var piece = atom.Eval(inputs);
                if (piece is null)
                    return null;
                builder.Append(piece);
            }

            return builder.ToString();
        }

        public int DistinctColumnCount => Atoms
            .Select(a => a switch { SynthSubAtom s => s.Column, SynthLoopInitialsAtom l => l.Column, _ => -1 })
            .Where(c => c >= 0)
            .Distinct()
            .Count();
    }

    /// <summary>
    /// Learns a program from the examples and applies it to <paramref name="remaining"/>. Returns null when no program
    /// reproduces every example, or the best program cannot be evaluated on every remaining row.
    /// </summary>
    /// <param name="minimumColumns">Reject programs that read fewer distinct input columns than this.</param>
    internal static IReadOnlyList<string>? TrySynthesizeFill(
        IReadOnlyList<(IReadOnlyList<string> Inputs, string Output)> examples,
        IReadOnlyList<IReadOnlyList<string>> remaining,
        int minimumColumns = 0)
    {
        if (examples.Count == 0)
            return null;

        var variableTokenCounts = examples.Select(e => e.Inputs).Concat(remaining)
            .Any(row => row.Select(v => SynthTokens(v, SynthTokenClass.Alpha).Count).Distinct().Count() > 0 &&
                        row.Select(v => SynthTokens(v, SynthTokenClass.Alpha).Count).First() != SynthTokens(examples[0].Inputs[0], SynthTokenClass.Alpha).Count);
        foreach (var allowLoops in variableTokenCounts ? new[] { true, false } : new[] { false, true })
        {
            var program = LearnSynthProgram(examples, allowLoops);
            if (program is null)
                continue;

            if (program.DistinctColumnCount < minimumColumns)
                continue;

            var results = new List<string>(remaining.Count);
            var ok = true;
            foreach (var row in remaining)
            {
                var value = program.Run(row);
                if (value is null)
                {
                    ok = false;
                    break;
                }

                results.Add(value);
            }

            if (ok)
                return results;
        }

        return null;
    }

    /// <summary>
    /// Multi-column Flash Fill: learns a program over all of the adjacent source columns and applies it, but only when the
    /// program really combines at least two of them (otherwise the single-column engine is the right tool).
    /// </summary>
    public static IReadOnlyList<string>? FillFromColumnsSynthesized(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs,
        IReadOnlyList<IReadOnlyList<string>> remainingSources)
    {
        if (exampleSources.Count == 0 || exampleSources.Count != exampleOutputs.Count)
            return null;

        var examples = exampleSources.Select((s, i) => (s, exampleOutputs[i])).ToList();
        return TrySynthesizeFill(examples, remainingSources, minimumColumns: examples.Count == 1 ? 1 : 2);
    }

    private static SynthProgram? LearnSynthProgram(
        IReadOnlyList<(IReadOnlyList<string> Inputs, string Output)> examples,
        bool allowLoops)
    {
        var count = examples.Count;
        foreach (var (inputs, output) in examples)
        {
            if (output.Length == 0 || output.Length > SynthMaxLength || inputs.Any(i => i.Length > SynthMaxLength))
                return null;
            if (inputs.Count != examples[0].Inputs.Count)
                return null;
        }

        var atoms = BuildSynthAtoms(examples, allowLoops);
        var outputs = examples.Select(e => e.Output).ToArray();

        // Per-atom evaluation on every example (atoms that cannot be evaluated or are not a substring of the matching output are dropped).
        var evaluated = new List<(SynthAtom Atom, string[] Values)>();
        foreach (var atom in atoms)
        {
            var values = new string[count];
            var ok = true;
            for (var e = 0; e < count; e++)
            {
                var value = atom.Eval(examples[e].Inputs);
                if (string.IsNullOrEmpty(value) || !outputs[e].Contains(value, StringComparison.Ordinal))
                {
                    ok = false;
                    break;
                }

                values[e] = value;
            }

            if (ok)
                evaluated.Add((atom, values));
        }

        // Dijkstra over (offset per example, uses-input flag).
        var startOffsets = new int[count];
        var queue = new PriorityQueue<(int[] Offsets, bool UsesInput), double>();
        var best = new Dictionary<string, double>();
        var parent = new Dictionary<string, (string Key, SynthAtom Atom)>();
        string Key(int[] offsets, bool uses) => string.Join(',', offsets) + (uses ? "u" : "c");

        var startKey = Key(startOffsets, false);
        best[startKey] = 0;
        queue.Enqueue((startOffsets, false), 0);
        var visited = 0;
        string? goalKey = null;
        var goalCost = double.MaxValue;

        while (queue.TryDequeue(out var state, out var cost))
        {
            var key = Key(state.Offsets, state.UsesInput);
            if (best.TryGetValue(key, out var known) && cost > known + 1e-9)
                continue;

            if (++visited > SynthMaxNodes)
                return null;

            var atEnd = true;
            for (var e = 0; e < count; e++)
            {
                if (state.Offsets[e] != outputs[e].Length)
                {
                    atEnd = false;
                    break;
                }
            }

            if (atEnd)
            {
                var total = cost + (state.UsesInput ? 0 : SynthAllConstPenalty);
                if (total < goalCost)
                {
                    goalCost = total;
                    goalKey = key;
                }

                continue;
            }

            for (var e = 0; e < count; e++)
            {
                if (state.Offsets[e] >= outputs[e].Length && !atEnd)
                {
                    // One example is exhausted before the others: no atom can continue consistently.
                    goto nextState;
                }
            }

            foreach (var (atom, values) in evaluated)
            {
                var fits = true;
                for (var e = 0; e < count; e++)
                {
                    if (!outputs[e].AsSpan(state.Offsets[e]).StartsWith(values[e], StringComparison.Ordinal))
                    {
                        fits = false;
                        break;
                    }
                }

                if (fits)
                    Relax(state, key, cost, atom, values.Select(v => v.Length).ToArray(), true);
            }

            // Constant atoms: the same literal text in every example at the current offsets.
            var maxConst = outputs.Select((o, e) => o.Length - state.Offsets[e]).Min();
            for (var length = 1; length <= maxConst; length++)
            {
                var text = outputs[0].Substring(state.Offsets[0], length);
                var same = true;
                for (var e = 1; e < count; e++)
                {
                    if (string.CompareOrdinal(outputs[e], state.Offsets[e], text, 0, length) != 0)
                    {
                        same = false;
                        break;
                    }
                }

                if (!same)
                    break;

                Relax(state, key, cost, new SynthConstAtom(text), Enumerable.Repeat(length, count).ToArray(), false);
            }

        nextState:;
        }

        if (goalKey is null)
            return null;

        var path = new List<SynthAtom>();
        var cursor = goalKey;
        while (parent.TryGetValue(cursor, out var link))
        {
            path.Add(link.Atom);
            cursor = link.Key;
        }

        path.Reverse();
        return new SynthProgram(PreferConstantsForAdjacentInitials(path, examples));

        void Relax((int[] Offsets, bool UsesInput) state, string fromKey, double fromCost, SynthAtom atom, int[] lengths, bool usesInput)
        {
            var next = new int[count];
            for (var e = 0; e < count; e++)
                next[e] = state.Offsets[e] + lengths[e];
            var uses = state.UsesInput || usesInput;
            var nextKey = Key(next, uses);
            var nextCost = fromCost + atom.Cost;
            if (best.TryGetValue(nextKey, out var existing) && existing <= nextCost + 1e-9)
                return;

            best[nextKey] = nextCost;
            parent[nextKey] = (fromKey, atom);
            queue.Enqueue((next, uses), nextCost);
        }
    }

    /// <summary>
    /// Excel treats a lone letter glued onto a longer extracted piece (e.g. "JSmith" from "John Smith") as a literal
    /// rather than an initial; swap such single-character substrings for constants when every example agrees.
    /// </summary>
    private static IReadOnlyList<SynthAtom> PreferConstantsForAdjacentInitials(
        List<SynthAtom> path,
        IReadOnlyList<(IReadOnlyList<string> Inputs, string Output)> examples)
    {
        var result = new List<SynthAtom>(path);
        var changed = true;
        while (changed)
        {
        changed = false;
        for (var i = 0; i < result.Count; i++)
        {
            if (result[i] is not SynthSubAtom single)
                continue;

            var values = examples.Select(e => single.Eval(e.Inputs)).ToArray();
            if (values.Any(v => v is null || v.Length != 1 || !char.IsLetter(v[0])) || values.Distinct().Count() != 1)
                continue;

            var neighbours = new[] { i - 1, i + 1 }.Where(n => n >= 0 && n < result.Count).Select(n => result[n]).ToList();
            var gluedToLiteral =
                (i > 0 && result[i - 1] is SynthConstAtom before && char.IsLetterOrDigit(before.Text[^1])) ||
                (i + 1 < result.Count && result[i + 1] is SynthConstAtom after && char.IsLetterOrDigit(after.Text[0]));
            var gluedToLongerPiece = neighbours.Any(n => n is SynthSubAtom other &&
                                                         examples.All(e => other.Eval(e.Inputs) is { Length: > 1 } text && char.IsLetter(text[0])));
            if (gluedToLiteral || gluedToLongerPiece)
            {
                result[i] = new SynthConstAtom(values[0]!);
                changed = true;
            }
        }
        }

        return result;
    }

    private static List<SynthAtom> BuildSynthAtoms(
        IReadOnlyList<(IReadOnlyList<string> Inputs, string Output)> examples,
        bool allowLoops)
    {
        var atoms = new HashSet<SynthAtom>();
        var first = examples[0];
        var columns = first.Inputs.Count;

        // Every substring of the first output, to prune candidate substrings cheaply.
        var outputSubstrings = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < first.Output.Length; i++)
        {
            for (var j = 1; j <= first.Output.Length - i; j++)
                outputSubstrings.Add(first.Output.Substring(i, j));
        }

        var cases = new[] { SynthCase.None, SynthCase.Upper, SynthCase.Lower, SynthCase.Proper };
        for (var column = 0; column < columns; column++)
        {
            var source = first.Inputs[column];
            for (var a = 0; a < source.Length; a++)
            {
                for (var b = a + 1; b <= source.Length; b++)
                {
                    var piece = source.Substring(a, b - a);
                    var seenText = new HashSet<string>();
                    foreach (var transform in cases)
                    {
                        var text = SynthApplyCase(piece, transform);
                        if (!seenText.Add(text) || !outputSubstrings.Contains(text))
                            continue;

                        // A slice that begins or ends in whitespace is a sign of a position that will not generalize.
                        if (b - a > 1 && (char.IsWhiteSpace(piece[0]) || char.IsWhiteSpace(piece[^1])))
                            continue;

                        var starts = SynthPositionsFor(source, a, examples, column);
                        if (starts.Count == 0)
                            continue;

                        var ends = SynthPositionsFor(source, b, examples, column);
                        foreach (var start in starts)
                        {
                            // Very short slices are only believable as the start of a token (initials, 2-letter codes).
                            if (b - a <= 2 && start is not SynthTokenPos { End: false, Offset: 0 })
                                continue;

                            foreach (var end in ends)
                                atoms.Add(new SynthSubAtom(column, start, end, transform) { SampleLength = b - a });
                        }
                    }
                }
            }

            if (allowLoops)
            {
                foreach (var transform in cases)
                {
                    foreach (var between in new[] { "", ".", ". ", " ", "-", "_", ", ", "/" })
                    {
                        foreach (var after in new[] { "", ".", " " })
                            atoms.Add(new SynthLoopInitialsAtom(column, between, after, transform));
                    }
                }
            }
        }

        return atoms.ToList();
    }

    /// <summary>All position expressions that denote offset <paramref name="p"/> in the first example and evaluate on every example.</summary>
    private static List<SynthPos> SynthPositionsFor(
        string s,
        int p,
        IReadOnlyList<(IReadOnlyList<string> Inputs, string Output)> examples,
        int column)
    {
        var candidates = new List<SynthPos> { new SynthAbsPos(p, false), new SynthAbsPos(s.Length - p, true) };

        foreach (var tokenClass in new[] { SynthTokenClass.Alpha, SynthTokenClass.Digit, SynthTokenClass.Alnum })
        {
            var tokens = SynthTokens(s, tokenClass);
            for (var i = 0; i < tokens.Count; i++)
            {
                for (var offset = 0; offset <= 4 && offset <= tokens[i].End - tokens[i].Start; offset++)
                {
                    if (tokens[i].Start + offset == p)
                    {
                        candidates.Add(new SynthTokenPos(tokenClass, i + 1, false, offset));
                        candidates.Add(new SynthTokenPos(tokenClass, i - tokens.Count, false, offset));
                    }

                    if (offset > 0 && tokens[i].End - offset == p)
                    {
                        candidates.Add(new SynthTokenPos(tokenClass, i + 1, true, -offset));
                        candidates.Add(new SynthTokenPos(tokenClass, i - tokens.Count, true, -offset));
                    }
                }

                if (tokens[i].End == p)
                {
                    candidates.Add(new SynthTokenPos(tokenClass, i + 1, true, 0));
                    candidates.Add(new SynthTokenPos(tokenClass, i - tokens.Count, true, 0));
                }
            }
        }

        var delimiters = s.Where(ch => !char.IsLetterOrDigit(ch)).Distinct().ToList();
        foreach (var delimiter in delimiters)
        {
            var total = s.Count(ch => ch == delimiter);
            var seen = 0;
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] != delimiter)
                    continue;

                seen++;
                if (i + 1 == p)
                {
                    candidates.Add(new SynthDelimPos(delimiter, seen, true));
                    candidates.Add(new SynthDelimPos(delimiter, seen - total - 1, true));
                }

                if (i == p)
                {
                    candidates.Add(new SynthDelimPos(delimiter, seen, false));
                    candidates.Add(new SynthDelimPos(delimiter, seen - total - 1, false));
                }
            }
        }

        // Keep only expressions that are valid on every example (and mean the same thing structurally).
        var valid = new List<SynthPos>();
        foreach (var candidate in candidates)
        {
            var ok = true;
            for (var e = 1; e < examples.Count; e++)
            {
                if (candidate.Eval(examples[e].Inputs[column]) is null)
                {
                    ok = false;
                    break;
                }
            }

            if (ok && !valid.Contains(candidate))
                valid.Add(candidate);
        }

        return valid;
    }
}

public static partial class FlashFillService
{
    /// <summary>Diagnostic: the learned program as text (null when none).</summary>
    internal static string? DescribeSynthesizedProgram(IReadOnlyList<(IReadOnlyList<string> Inputs, string Output)> examples, bool allowLoops = false)
    {
        var program = LearnSynthProgram(examples, allowLoops);
        return program is null ? null : string.Join(" + ", program.Atoms.Select(a => a.ToString()));
    }
}
