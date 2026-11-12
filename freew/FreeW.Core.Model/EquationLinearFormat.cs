using System.Globalization;
using System.Text;
using Symbols = FreeW.Core.Model.EquationLinearSymbols;

namespace FreeW.Core.Model;

/// <summary>
/// The linear source of an equation: its text plus the fragments that text cannot express. Each such
/// fragment appears in <see cref="Text"/> as an opaque token <c>⟦n⟧</c> whose number indexes
/// <see cref="OpaqueParts"/>; parsing the text with the same list puts the fragment back untouched.
/// </summary>
/// <param name="Text">The linear text.</param>
/// <param name="OpaqueParts">The fragments opaque tokens in <paramref name="Text"/> refer to, by index.</param>
/// <param name="HasOpaqueParts">True when part of the equation is carried as an opaque token rather than editable text.</param>
public sealed record EquationLinearText(string Text, IReadOnlyList<MathRun> OpaqueParts, bool HasOpaqueParts);

/// <summary>
/// Converts between the equation model and FreeW's linear equation format — the subset of Word's
/// "UnicodeMath" linear format that covers the structures the model holds.
/// <para>
/// Grammar, loosely (every <c>\name</c> in <see cref="EquationLinearSymbols.Names"/> is replaced by its
/// character before parsing, swallowing one following space, exactly as Word's math autocorrect does):
/// </para>
/// <list type="bullet">
/// <item>fractions <c>a/b</c>, <c>(a+b)/(c+d)</c>; <c>¦</c> (\atop) no bar, <c>⁄</c> (\sdiv) skewed, <c>∕</c> (\ldiv) linear;</item>
/// <item>scripts <c>x^2</c>, <c>x_i</c>, <c>x_i^2</c>; limits <c>lim_(x-&gt;0)</c>, <c>a┬b</c> (\below), <c>a┴b</c> (\above);</item>
/// <item>radicals <c>√x</c>, <c>√(a+b)</c>, <c>√(n&amp;x)</c>, <c>∛x</c>, <c>∜x</c>;</item>
/// <item>delimiters <c>(…)</c> <c>[…]</c> <c>{…}</c> <c>|…|</c> <c>⟨…⟩</c> <c>⌊…⌋</c> <c>⌈…⌉</c>, any open with any close,
/// <c>├c…┤c</c> for arbitrary glyphs, <c>〖…〗</c> for invisible grouping and <c>│</c> between arguments;</item>
/// <item>n-ary operators <c>∑_(i=1)^n▒a_i</c> (the operand follows <c>▒</c> or a space);</item>
/// <item>functions <c>sin x</c>, <c>sin⁡x</c> (U+2061), <c>lim_(x-&gt;0) f</c>;</item>
/// <item>accents after their base (<c>x\hat</c>, <c>(a+b)\vec</c>), bars <c>¯(x)</c> <c>▁(x)</c>, braces <c>⏞(x)</c> <c>⏟(x)</c>;</item>
/// <item>matrices <c>■(a&amp;b@c&amp;d)</c> and equation arrays <c>█(a@b)</c>;</item>
/// <item><c>\c</c> takes the character c literally, <c>"…"</c> takes a whole run literally.</item>
/// </list>
/// <para>
/// Round trip: <c>Parse(ToLinear(e))</c> is <see cref="EquationStructure.AreEquivalent(Equation?, Equation?, bool)"/>
/// to <c>e</c> for every equation. The serializer proves that for each fragment as it writes it and
/// falls back to an opaque token for anything the text form cannot carry, so nothing is ever dropped.
/// </para>
/// </summary>
public static class EquationLinearFormat
{
    private const int MaxParseDepth = 64;
    private const int MaxChainLength = 32;

    /// <summary>Parses linear text into an equation. Never throws: text that forms no structure stays text.</summary>
    public static Equation Parse(string? text, IReadOnlyList<MathRun>? opaqueParts = null) =>
        new(ParseRuns(text ?? string.Empty, opaqueParts ?? []));

    /// <summary>Writes an equation as linear text (see the round-trip guarantee on the type).</summary>
    public static EquationLinearText ToLinear(Equation equation)
    {
        ArgumentNullException.ThrowIfNull(equation);

        var runs = EquationStructure.CanonicalRuns(equation.Runs);
        var serializer = new Serializer();
        var text = serializer.Sequence(runs);
        if (!serializer.Verify(text, runs))
        {
            // Unreachable by construction; kept so a serializer defect can never lose content.
            serializer = new Serializer();
            text = string.Concat(runs.Select(serializer.OpaqueToken));
        }

        var opaqueParts = serializer.OpaqueParts;
        return new EquationLinearText(
            text,
            opaqueParts,
            opaqueParts.Count > 0 && Tokenize(text, opaqueParts.Count).Any(token => token.Kind == TokenKind.Opaque));
    }

    /// <summary>
    /// Replaces every recognised <c>\name</c> in <paramref name="text"/> by its character (the text the
    /// editor shows after Word-style math autocorrect) without otherwise changing it.
    /// </summary>
    public static string ExpandNames(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] == '\\')
            {
                var end = index + 1;
                while (end < text.Length && char.IsAsciiLetter(text[end]))
                    end++;

                if (end > index + 1 && Symbols.Names.TryGetValue(text[(index + 1)..end], out var value))
                {
                    builder.Append(value);
                    index = end;
                    if (index < text.Length && text[index] == ' ')
                        index++;
                    continue;
                }

                // An escape (\c) or an unknown name: copy the backslash and the next element verbatim.
                builder.Append('\\');
                index++;
                if (index < text.Length)
                {
                    var element = ElementAt(text, index);
                    builder.Append(element);
                    index += element.Length;
                }

                continue;
            }

            builder.Append(text[index]);
            index++;
        }

        return builder.ToString();
    }

    private static List<MathRun> ParseRuns(string text, IReadOnlyList<MathRun> opaqueParts)
    {
        var parser = new Parser(Tokenize(text, opaqueParts.Count), opaqueParts);
        return parser.ParseAll();
    }

    private static string ElementAt(string text, int index) =>
        char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
            ? text.Substring(index, 2)
            : text.Substring(index, 1);

    private static IEnumerable<string> Elements(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            var element = ElementAt(text, index);
            yield return element;
            index += element.Length;
        }
    }

    private static bool IsAsciiDigit(string element) => element.Length == 1 && char.IsAsciiDigit(element[0]);

    private static bool IsAsciiLetter(string element) => element.Length == 1 && char.IsAsciiLetter(element[0]);

    private static bool IsOperandCharacter(string element) =>
        Symbols.OperandSymbols.Contains(element)
        || (element.Length == 1
            ? char.IsLetterOrDigit(element[0])
            : element.Length == 2 && char.IsLetterOrDigit(element, 0));

    private static bool IsNumber(string text)
    {
        if (text.Length == 0 || !char.IsAsciiDigit(text[0]) || !char.IsAsciiDigit(text[^1]))
            return false;

        var seenPoint = false;
        foreach (var ch in text)
        {
            if (char.IsAsciiDigit(ch))
                continue;
            if (ch != '.' || seenPoint)
                return false;
            seenPoint = true;
        }

        return true;
    }

    /// <summary>A single operand the grammar reads back as exactly this text without any bracketing.</summary>
    private static bool IsSimpleAtom(string text)
    {
        if (text.Length == 0)
            return false;

        if (IsNumber(text))
            return true;

        var element = ElementAt(text, 0);
        return element.Length == text.Length && IsOperandCharacter(element) && !Symbols.IsStructural(element);
    }

    private static bool TryFractionType(string element, out MathFractionType type)
    {
        switch (element)
        {
            case "/":
                type = MathFractionType.Bar;
                return true;
            case Symbols.NoBarFraction:
                type = MathFractionType.NoBar;
                return true;
            case Symbols.SkewedFraction:
                type = MathFractionType.Skewed;
                return true;
            case Symbols.LinearFraction:
                type = MathFractionType.Linear;
                return true;
            default:
                type = MathFractionType.Bar;
                return false;
        }
    }

    private static string FractionOperator(MathFractionType type) => type switch
    {
        MathFractionType.NoBar => Symbols.NoBarFraction,
        MathFractionType.Skewed => Symbols.SkewedFraction,
        MathFractionType.Linear => Symbols.LinearFraction,
        _ => "/"
    };

    // ── Tokens ───────────────────────────────────────────────────────────────────────────────────────

    private enum TokenKind
    {
        /// <summary>One character with its grammatical meaning.</summary>
        Char,

        /// <summary>Escaped or quoted text: never structural, never an operand.</summary>
        Literal,

        /// <summary>A reference to an opaque fragment.</summary>
        Opaque
    }

    private readonly record struct Token(TokenKind Kind, string Text, int OpaqueIndex = -1);

    private static readonly (string Source, string Replacement)[] Digraphs =
    [
        ("->", "→"),
        ("<=", "≤"),
        (">=", "≥"),
        ("+-", "±"),
        ("-+", "∓"),
        ("~=", "≅"),
    ];

    private static List<Token> Tokenize(string text, int opaqueCount)
    {
        var tokens = new List<Token>(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            var ch = text[index];
            if (ch == '\\')
            {
                var end = index + 1;
                while (end < text.Length && char.IsAsciiLetter(text[end]))
                    end++;

                if (end > index + 1)
                {
                    if (Symbols.Names.TryGetValue(text[(index + 1)..end], out var value))
                    {
                        foreach (var element in Elements(value))
                            tokens.Add(new Token(TokenKind.Char, element));
                        index = end;
                        if (index < text.Length && text[index] == ' ')
                            index++;
                        continue;
                    }

                    // An unknown name stays as typed: a literal backslash followed by ordinary letters.
                    tokens.Add(new Token(TokenKind.Literal, "\\"));
                    index++;
                    continue;
                }

                if (index + 1 < text.Length)
                {
                    var escaped = ElementAt(text, index + 1);
                    tokens.Add(new Token(TokenKind.Literal, escaped));
                    index += 1 + escaped.Length;
                    continue;
                }

                tokens.Add(new Token(TokenKind.Literal, "\\"));
                index++;
                continue;
            }

            if (ch == '"' && TryReadQuoted(text, index, out var quoted, out var afterQuote))
            {
                if (quoted.Length > 0)
                    tokens.Add(new Token(TokenKind.Literal, quoted));
                index = afterQuote;
                continue;
            }

            if (ch == Symbols.OpaqueOpen[0] && TryReadOpaque(text, index, opaqueCount, out var opaqueIndex, out var afterOpaque))
            {
                tokens.Add(new Token(TokenKind.Opaque, string.Empty, opaqueIndex));
                index = afterOpaque;
                continue;
            }

            if (index + 1 < text.Length && TryDigraph(ch, text[index + 1], out var replacement))
            {
                tokens.Add(new Token(TokenKind.Char, replacement));
                index += 2;
                continue;
            }

            var current = ElementAt(text, index);
            tokens.Add(ch == '"'
                ? new Token(TokenKind.Literal, current)
                : new Token(TokenKind.Char, current));
            index += current.Length;
        }

        return tokens;
    }

    private static bool TryReadQuoted(string text, int start, out string value, out int after)
    {
        var builder = new StringBuilder();
        var index = start + 1;
        while (index < text.Length)
        {
            var ch = text[index];
            if (ch == '\\' && index + 1 < text.Length)
            {
                builder.Append(text[index + 1]);
                index += 2;
                continue;
            }

            if (ch == '"')
            {
                value = builder.ToString();
                after = index + 1;
                return true;
            }

            builder.Append(ch);
            index++;
        }

        value = string.Empty;
        after = start;
        return false;
    }

    private static bool TryReadOpaque(string text, int start, int opaqueCount, out int opaqueIndex, out int after)
    {
        opaqueIndex = -1;
        after = start;
        var index = start + 1;
        var value = 0;
        var digits = 0;
        while (index < text.Length && char.IsAsciiDigit(text[index]) && digits < 6)
        {
            value = (value * 10) + (text[index] - '0');
            digits++;
            index++;
        }

        if (digits == 0 || index >= text.Length || text[index] != Symbols.OpaqueClose[0] || value >= opaqueCount)
            return false;

        opaqueIndex = value;
        after = index + 1;
        return true;
    }

    private static bool TryDigraph(char first, char second, out string replacement)
    {
        foreach (var (source, value) in Digraphs)
        {
            if (source[0] == first && source[1] == second)
            {
                replacement = value;
                return true;
            }
        }

        replacement = string.Empty;
        return false;
    }

    // ── Parser ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One parsed operand: how it reads standing alone and, for a bracket group, without its brackets.</summary>
    private sealed class Operand(List<MathRun> runs)
    {
        /// <summary>The operand as it appears on its own.</summary>
        public List<MathRun> Runs { get; } = runs;

        /// <summary>The content of a <c>(…)</c> or <c>〖…〗</c> group, used where the grammar drops the brackets.</summary>
        public List<MathRun>? Stripped { get; init; }

        /// <summary>The function name this operand is (still) headed by, so it applies to what follows.</summary>
        public string? FunctionName { get; init; }

        /// <summary>True while the operand is nothing but the bare function name.</summary>
        public bool IsBareFunctionName { get; init; }

        public List<MathRun> Content => Stripped ?? Runs;
    }

    private sealed class Parser
    {
        private readonly List<Token> _tokens;
        private readonly IReadOnlyList<MathRun> _opaqueParts;
        private readonly int[] _match;
        private readonly int[] _markerLength;

        public Parser(List<Token> tokens, IReadOnlyList<MathRun> opaqueParts)
        {
            _tokens = tokens;
            _opaqueParts = opaqueParts;
            _match = new int[tokens.Count];
            _markerLength = new int[tokens.Count];
            Array.Fill(_match, -1);
            Array.Fill(_markerLength, 1);
            MatchBrackets();
        }

        public List<MathRun> ParseAll() => ParseRange(0, _tokens.Count, depth: 0);

        private bool IsChar(int index, int end, string text) =>
            index < end && _tokens[index].Kind == TokenKind.Char && _tokens[index].Text == text;

        private bool IsCharToken(int index, int end) => index < end && _tokens[index].Kind == TokenKind.Char;

        /// <summary>
        /// Pairs brackets. Any opening bracket pairs with the next unpaired closing bracket at its level (so
        /// <c>(a,b]</c> is one delimiter, as in Word); a bar closes the nearest open bar of the same glyph
        /// and otherwise opens. Brackets left unpaired are ordinary text.
        /// </summary>
        private void MatchBrackets()
        {
            var stack = new Stack<(int Index, bool IsBar)>();
            for (var index = 0; index < _tokens.Count; index++)
            {
                var token = _tokens[index];
                if (token.Kind != TokenKind.Char)
                    continue;

                if (token.Text == Symbols.ExplicitOpen)
                {
                    if (index + 1 < _tokens.Count && _tokens[index + 1].Kind != TokenKind.Opaque)
                    {
                        _markerLength[index] = 2;
                        stack.Push((index, false));
                        index++;
                    }
                }
                else if (token.Text == Symbols.ExplicitClose)
                {
                    if (TryClose(stack, index) && index + 1 < _tokens.Count && _tokens[index + 1].Kind != TokenKind.Opaque)
                    {
                        _markerLength[index] = 2;
                        index++;
                    }
                }
                else if (Symbols.OpenBrackets.Contains(token.Text))
                {
                    stack.Push((index, false));
                }
                else if (Symbols.CloseBrackets.Contains(token.Text))
                {
                    TryClose(stack, index);
                }
                else if (Symbols.BarBrackets.Contains(token.Text))
                {
                    if (stack.Count > 0 && stack.Peek().IsBar && _tokens[stack.Peek().Index].Text == token.Text)
                    {
                        var open = stack.Pop().Index;
                        _match[open] = index;
                        _match[index] = open;
                    }
                    else
                    {
                        stack.Push((index, true));
                    }
                }
            }
        }

        private bool TryClose(Stack<(int Index, bool IsBar)> stack, int closeIndex)
        {
            // Bars still open inside this bracket never found their partner: they are plain text.
            while (stack.Count > 0 && stack.Peek().IsBar)
                stack.Pop();

            if (stack.Count == 0)
                return false;

            var open = stack.Pop().Index;
            _match[open] = closeIndex;
            _match[closeIndex] = open;
            return true;
        }

        private bool IsGroupOpen(int index, int end) => index < end && _match[index] > index;

        private int InnerStart(int open) => open + _markerLength[open];

        private int AfterGroup(int open) => _match[open] + _markerLength[_match[open]];

        private string OpenGlyph(int open) =>
            _markerLength[open] == 2
                ? _tokens[open + 1].Text
                : _tokens[open].Text == Symbols.InvisibleOpen ? string.Empty : _tokens[open].Text;

        private string CloseGlyph(int open)
        {
            var close = _match[open];
            return _markerLength[close] == 2
                ? _tokens[close + 1].Text
                : _tokens[close].Text == Symbols.InvisibleClose ? string.Empty : _tokens[close].Text;
        }

        private bool IsPlainParenGroup(int index, int end) =>
            IsGroupOpen(index, end)
            && _markerLength[index] == 1
            && _tokens[index].Text == "("
            && _markerLength[_match[index]] == 1
            && _tokens[_match[index]].Text == ")";

        private List<(int Start, int End)> SplitTopLevel(int start, int end, string separator, int maxParts = int.MaxValue)
        {
            var parts = new List<(int Start, int End)>();
            var partStart = start;
            var index = start;
            while (index < end)
            {
                if (_match[index] > index && _match[index] < end)
                {
                    index = AfterGroup(index);
                    continue;
                }

                if (parts.Count + 1 < maxParts && IsChar(index, end, separator))
                {
                    parts.Add((partStart, index));
                    partStart = index + 1;
                }

                index++;
            }

            parts.Add((partStart, end));
            return parts;
        }

        private List<MathRun> ParseRange(int start, int end, int depth)
        {
            var output = new List<MathRun>();
            var text = new StringBuilder();
            var index = start;
            while (index < end)
            {
                var next = index;
                if (TryFraction(ref next, end, depth, out var operand))
                {
                    Flush();
                    output.AddRange(operand.Runs);
                    index = next;
                    continue;
                }

                var token = _tokens[index];
                if (token.Kind == TokenKind.Opaque)
                {
                    // Only reachable past the nesting limit, where operands are no longer parsed.
                    Flush();
                    output.Add(_opaqueParts[token.OpaqueIndex]);
                }
                else
                {
                    text.Append(token.Text);
                }

                index++;
            }

            Flush();
            return EquationStructure.CanonicalRuns(output);

            void Flush()
            {
                if (text.Length == 0)
                    return;
                output.Add(MathRun.PlainText(text.ToString()));
                text.Clear();
            }
        }

        // fraction := factor ( fractionOperator factor )*
        private bool TryFraction(ref int index, int end, int depth, out Operand operand)
        {
            if (!TryFactor(ref index, end, depth, out operand))
                return false;

            var chain = 0;
            while (chain++ < MaxChainLength
                && IsCharToken(index, end)
                && TryFractionType(_tokens[index].Text, out var type))
            {
                var next = index + 1;
                if (!TryFactor(ref next, end, depth + 1, out var denominator))
                    break;

                operand = new Operand([BuildFraction(type, operand.Content, denominator.Content)]);
                index = next;
            }

            return true;
        }

        // factor := script ( U+2061 factor? | implicit application of a function name )?
        private bool TryFactor(ref int index, int end, int depth, out Operand operand)
        {
            if (!TryScript(ref index, end, depth, out operand))
                return false;

            if (IsChar(index, end, Symbols.FunctionApply))
            {
                var next = index + 1;
                var argument = TryFactor(ref next, end, depth + 1, out var explicitArgument)
                    ? explicitArgument.Runs
                    : [];
                operand = new Operand([BuildFunction(operand.Runs, argument)]);
                index = next;
            }
            else if (operand.FunctionName is not null)
            {
                var next = index;
                if (IsChar(next, end, " "))
                    next++;
                if (TryFactor(ref next, end, depth + 1, out var implicitArgument))
                {
                    operand = new Operand([BuildFunction(operand.Runs, implicitArgument.Runs)]);
                    index = next;
                }
            }

            return true;
        }

        // script := primary ( (_ arg)? (^ arg)? | ┬ arg | ┴ arg )*
        private bool TryScript(ref int index, int end, int depth, out Operand operand)
        {
            if (!TryPrimary(ref index, end, depth, out operand))
                return false;

            for (var chain = 0; chain < MaxChainLength; chain++)
            {
                if (IsChar(index, end, Symbols.Below) && TryScriptArgument(ref index, end, depth, out var below))
                {
                    operand = new Operand([BuildScript(MathRunKind.LowerLimit, operand.Runs, below, null)])
                    {
                        FunctionName = operand.FunctionName
                    };
                    continue;
                }

                if (IsChar(index, end, Symbols.Above) && TryScriptArgument(ref index, end, depth, out var above))
                {
                    operand = new Operand([BuildScript(MathRunKind.UpperLimit, operand.Runs, null, above)])
                    {
                        FunctionName = operand.FunctionName
                    };
                    continue;
                }

                List<MathRun>? sub = null;
                List<MathRun>? sup = null;
                while (true)
                {
                    if (sub is null && IsChar(index, end, "_") && TryScriptArgument(ref index, end, depth, out var subArgument))
                    {
                        sub = subArgument;
                        continue;
                    }

                    if (sup is null && IsChar(index, end, "^") && TryScriptArgument(ref index, end, depth, out var supArgument))
                    {
                        sup = supArgument;
                        continue;
                    }

                    break;
                }

                if (sub is null && sup is null)
                    break;

                MathRunKind kind;
                if (sub is not null && sup is not null)
                    kind = MathRunKind.SubSuperscript;
                else if (sub is null)
                    kind = MathRunKind.Superscript;
                else if (operand.IsBareFunctionName && Symbols.LimitFunctionNames.Contains(operand.FunctionName!))
                    kind = MathRunKind.LowerLimit;
                else
                    kind = MathRunKind.Subscript;

                operand = new Operand([BuildScript(kind, operand.Runs, sub, sup)])
                {
                    FunctionName = operand.FunctionName
                };
            }

            return true;
        }

        private bool TryScriptArgument(ref int index, int end, int depth, out List<MathRun> runs)
        {
            runs = [];
            var next = index + 1;
            string? sign = null;
            if (IsCharToken(next, end) && Symbols.ScriptSigns.Contains(_tokens[next].Text))
            {
                sign = _tokens[next].Text;
                next++;
            }

            if (!TryPrimary(ref next, end, depth + 1, out var operand))
                return false;

            if (sign is not null)
                runs.Add(MathRun.PlainText(sign));
            runs.AddRange(operand.Content);
            runs = EquationStructure.CanonicalRuns(runs);
            index = next;
            return true;
        }

        private bool TryPrimary(ref int index, int end, int depth, out Operand operand)
        {
            operand = null!;
            if (index >= end || depth > MaxParseDepth)
                return false;

            var token = _tokens[index];
            if (token.Kind == TokenKind.Literal)
                return false;

            var next = index;
            if (token.Kind == TokenKind.Opaque)
            {
                operand = new Operand([_opaqueParts[token.OpaqueIndex]]);
                next++;
            }
            else if (!TryStructuralPrimary(ref next, end, depth, out operand))
            {
                return false;
            }

            for (var chain = 0;
                 chain < MaxChainLength && IsCharToken(next, end) && Symbols.Accents.Contains(_tokens[next].Text);
                 chain++)
            {
                operand = new Operand([BuildDecorated(MathRunKind.Accent, operand.Content, accent: _tokens[next].Text)]);
                next++;
            }

            index = next;
            return true;
        }

        private bool TryStructuralPrimary(ref int index, int end, int depth, out Operand operand)
        {
            operand = null!;
            var text = _tokens[index].Text;

            if (IsAsciiDigit(text))
            {
                var next = index;
                var number = new StringBuilder();
                while (IsCharToken(next, end) && IsAsciiDigit(_tokens[next].Text))
                    number.Append(_tokens[next++].Text);
                if (IsChar(next, end, ".") && IsCharToken(next + 1, end) && IsAsciiDigit(_tokens[next + 1].Text))
                {
                    number.Append('.');
                    next++;
                    while (IsCharToken(next, end) && IsAsciiDigit(_tokens[next].Text))
                        number.Append(_tokens[next++].Text);
                }

                operand = new Operand([MathRun.PlainText(number.ToString())]);
                index = next;
                return true;
            }

            if (IsGroupOpen(index, end))
            {
                operand = ParseGroup(index, depth);
                index = AfterGroup(index);
                return true;
            }

            if (text is Symbols.Radical or Symbols.CubeRoot or Symbols.FourthRoot)
                return TryRadical(ref index, end, depth, out operand);

            if (text is Symbols.Matrix or Symbols.EquationArray)
                return TryMatrix(ref index, end, depth, out operand);

            if (text is Symbols.Overbar or Symbols.Underbar)
            {
                if (!TryBracketedOperand(index + 1, end, depth, out var barBase, out var afterBar))
                    return false;
                operand = new Operand([BuildDecorated(MathRunKind.Bar, barBase, barTop: text == Symbols.Overbar)]);
                index = afterBar;
                return true;
            }

            if (Symbols.GroupCharacters.TryGetValue(text, out var position))
            {
                if (!TryBracketedOperand(index + 1, end, depth, out var groupBase, out var afterGroup))
                    return false;
                operand = new Operand([BuildDecorated(MathRunKind.GroupChar, groupBase, groupChr: text, groupChrPos: position)]);
                index = afterGroup;
                return true;
            }

            if (Symbols.NAryOperators.Contains(text))
                return TryNAry(ref index, end, depth, out operand);

            if (Symbols.Accents.Contains(text))
            {
                // \hat(x): an accent with nothing before it applies to the bracket group after it.
                if (!IsPlainParenGroup(index + 1, end))
                    return false;
                var accentBase = ParseRange(InnerStart(index + 1), _match[index + 1], depth + 1);
                operand = new Operand([BuildDecorated(MathRunKind.Accent, accentBase, accent: text)]);
                index = AfterGroup(index + 1);
                return true;
            }

            if (IsAsciiLetter(text))
            {
                var precededByLetter = index > 0
                    && _tokens[index - 1].Kind == TokenKind.Char
                    && IsAsciiLetter(_tokens[index - 1].Text);
                if (!precededByLetter)
                {
                    var wordEnd = index;
                    var word = new StringBuilder();
                    while (IsCharToken(wordEnd, end) && IsAsciiLetter(_tokens[wordEnd].Text))
                        word.Append(_tokens[wordEnd++].Text);

                    // A run that reaches the end of this range may continue past it; only whole words count.
                    var continues = wordEnd < _tokens.Count
                        && _tokens[wordEnd].Kind == TokenKind.Char
                        && IsAsciiLetter(_tokens[wordEnd].Text);
                    var name = word.ToString();
                    if (!continues && Symbols.FunctionNames.Contains(name))
                    {
                        operand = new Operand([MathRun.PlainText(name)])
                        {
                            FunctionName = name,
                            IsBareFunctionName = true
                        };
                        index = wordEnd;
                        return true;
                    }
                }
            }

            if (!IsOperandCharacter(text) || Symbols.IsStructural(text))
                return false;

            operand = new Operand([MathRun.PlainText(text)]);
            index++;
            return true;
        }

        private Operand ParseGroup(int open, int depth)
        {
            var arguments = SplitTopLevel(InnerStart(open), _match[open], Symbols.ArgumentSeparator)
                .Select(part => ParseRange(part.Start, part.End, depth + 1))
                .ToList();
            var openGlyph = OpenGlyph(open);
            var closeGlyph = CloseGlyph(open);

            if (arguments.Count == 1 && openGlyph.Length == 0 && closeGlyph.Length == 0)
                return new Operand(arguments[0]) { Stripped = arguments[0] };

            var (firstText, firstEquation) = Slot(arguments[0]);
            var additional = arguments.Skip(1).Select(Slot).ToList();
            var delimiter = new MathRun
            {
                Kind = MathRunKind.Delimiter,
                Base = firstText,
                DelimiterContentEquation = firstEquation,
                OpenChar = openGlyph,
                CloseChar = closeGlyph,
                AdditionalDelimiterArguments = additional.Select(slot => slot.Text).ToList(),
                AdditionalDelimiterContentEquations = additional.Select(slot => slot.Equation).ToList(),
                DelimiterSeparator = additional.Count > 0 ? "|" : ","
            };

            return new Operand([delimiter])
            {
                Stripped = arguments.Count == 1 && openGlyph == "(" && closeGlyph == ")" ? arguments[0] : null
            };
        }

        /// <summary>The operand of a prefix structure: the content of a <c>(…)</c> group, or the next primary.</summary>
        private bool TryBracketedOperand(int index, int end, int depth, out List<MathRun> runs, out int after)
        {
            if (IsPlainParenGroup(index, end) && depth < MaxParseDepth)
            {
                runs = ParseRange(InnerStart(index), _match[index], depth + 1);
                after = AfterGroup(index);
                return true;
            }

            var next = index;
            if (TryPrimary(ref next, end, depth + 1, out var operand))
            {
                runs = operand.Runs;
                after = next;
                return true;
            }

            runs = [];
            after = index;
            return false;
        }

        private bool TryRadical(ref int index, int end, int depth, out Operand operand)
        {
            operand = null!;
            var sign = _tokens[index].Text;
            List<MathRun> degree = sign switch
            {
                Symbols.CubeRoot => [MathRun.PlainText("3")],
                Symbols.FourthRoot => [MathRun.PlainText("4")],
                _ => []
            };
            List<MathRun> radicand;
            int after;

            if (IsPlainParenGroup(index + 1, end) && depth < MaxParseDepth)
            {
                var open = index + 1;
                var parts = SplitTopLevel(InnerStart(open), _match[open], "&", maxParts: 2);
                if (sign == Symbols.Radical && parts.Count == 2)
                {
                    degree = ParseRange(parts[0].Start, parts[0].End, depth + 1);
                    radicand = ParseRange(parts[1].Start, parts[1].End, depth + 1);
                }
                else
                {
                    radicand = ParseRange(InnerStart(open), _match[open], depth + 1);
                }

                after = AfterGroup(open);
            }
            else if (!TryBracketedOperand(index + 1, end, depth, out radicand, out after))
            {
                return false;
            }

            var (radicandText, radicandEquation) = Slot(radicand);
            var (degreeText, degreeEquation) = Slot(degree);
            operand = new Operand(
            [
                new MathRun
                {
                    Kind = MathRunKind.Radical,
                    Base = radicandText,
                    RadicandEquation = radicandEquation,
                    Degree = degreeText,
                    DegreeEquation = degreeEquation
                }
            ]);
            index = after;
            return true;
        }

        private bool TryMatrix(ref int index, int end, int depth, out Operand operand)
        {
            operand = null!;
            if (!IsPlainParenGroup(index + 1, end) || depth >= MaxParseDepth)
                return false;

            var isArray = _tokens[index].Text == Symbols.EquationArray;
            var open = index + 1;
            var matrix = new MathMatrix();
            foreach (var row in SplitTopLevel(InnerStart(open), _match[open], "@"))
            {
                var cells = isArray
                    ? [row]
                    : SplitTopLevel(row.Start, row.End, "&");
                var textRow = new List<string>();
                var equationRow = new List<Equation?>();
                foreach (var cell in cells)
                {
                    var (cellText, cellEquation) = Slot(ParseRange(cell.Start, cell.End, depth + 1));
                    textRow.Add(cellText);
                    equationRow.Add(cellEquation);
                }

                matrix.Rows.Add(textRow);
                matrix.CellEquations.Add(equationRow);
            }

            operand = new Operand([isArray ? MathRun.EquationArrayOf(matrix) : MathRun.MatrixOf(matrix)]);
            index = AfterGroup(open);
            return true;
        }

        private bool TryNAry(ref int index, int end, int depth, out Operand operand)
        {
            operand = null!;
            var next = index + 1;
            List<MathRun>? lower = null;
            List<MathRun>? upper = null;
            while (true)
            {
                if (lower is null && IsChar(next, end, "_") && TryScriptArgument(ref next, end, depth, out var lowerArgument))
                {
                    lower = lowerArgument;
                    continue;
                }

                if (upper is null && IsChar(next, end, "^") && TryScriptArgument(ref next, end, depth, out var upperArgument))
                {
                    upper = upperArgument;
                    continue;
                }

                break;
            }

            var hasOf = IsChar(next, end, Symbols.Of);
            if (lower is null && upper is null && !hasOf)
                return false;

            List<MathRun> body = [];
            if (hasOf)
            {
                next++;
                if (TryFactor(ref next, end, depth + 1, out var explicitBody))
                    body = explicitBody.Runs;
            }
            else
            {
                var bodyStart = next;
                if (IsChar(bodyStart, end, " "))
                    bodyStart++;
                if (TryFactor(ref bodyStart, end, depth + 1, out var implicitBody))
                {
                    body = implicitBody.Runs;
                    next = bodyStart;
                }
            }

            var (lowerText, lowerEquation) = Slot(lower ?? []);
            var (upperText, upperEquation) = Slot(upper ?? []);
            var (bodyText, bodyEquation) = Slot(body);
            operand = new Operand(
            [
                new MathRun
                {
                    Kind = MathRunKind.NAry,
                    Operator = _tokens[index].Text,
                    Sub = lowerText,
                    NAryLowerLimitEquation = lowerEquation,
                    Sup = upperText,
                    NAryUpperLimitEquation = upperEquation,
                    Base = bodyText,
                    NAryOperandEquation = bodyEquation
                }
            ]);
            index = next;
            return true;
        }

        private static (string Text, Equation? Equation) Slot(List<MathRun> runs)
        {
            var canonical = EquationStructure.CanonicalRuns(runs);
            if (EquationStructure.IsPlainText(canonical, out var text))
                return (text, null);

            var equation = new Equation(canonical);
            return (equation.LinearText, equation);
        }

        private static MathRun BuildFraction(MathFractionType type, List<MathRun> numerator, List<MathRun> denominator)
        {
            var (numeratorText, numeratorEquation) = Slot(numerator);
            var (denominatorText, denominatorEquation) = Slot(denominator);
            return new MathRun
            {
                Kind = MathRunKind.Fraction,
                FractionType = type,
                Numerator = numeratorText,
                NumeratorEquation = numeratorEquation,
                Denominator = denominatorText,
                DenominatorEquation = denominatorEquation
            };
        }

        private static MathRun BuildScript(MathRunKind kind, List<MathRun> @base, List<MathRun>? sub, List<MathRun>? sup)
        {
            var (baseText, baseEquation) = Slot(@base);
            var (subText, subEquation) = Slot(sub ?? []);
            var (supText, supEquation) = Slot(sup ?? []);
            return new MathRun
            {
                Kind = kind,
                Base = baseText,
                ScriptBaseEquation = baseEquation,
                Sub = subText,
                ScriptSubEquation = subEquation,
                Sup = supText,
                ScriptSupEquation = supEquation
            };
        }

        private static MathRun BuildFunction(List<MathRun> name, List<MathRun> argument)
        {
            var (nameText, nameEquation) = Slot(name);
            var (argumentText, argumentEquation) = Slot(argument);
            return new MathRun
            {
                Kind = MathRunKind.FunctionApply,
                FuncName = nameText,
                FunctionNameEquation = nameEquation,
                Base = argumentText,
                FunctionArgumentEquation = argumentEquation
            };
        }

        private static MathRun BuildDecorated(
            MathRunKind kind,
            List<MathRun> @base,
            string? accent = null,
            bool barTop = true,
            string? groupChr = null,
            string? groupChrPos = null)
        {
            var (baseText, baseEquation) = Slot(@base);
            var run = new MathRun
            {
                Kind = kind,
                Base = baseText,
                DecoratorBaseEquation = baseEquation,
                BarTop = barTop
            };
            if (accent is not null)
                run = run with { Accent = accent };
            if (groupChr is not null)
                run = run with { GroupChr = groupChr, GroupChrPos = groupChrPos ?? "top" };
            return run;
        }
    }

    // ── Serializer ───────────────────────────────────────────────────────────────────────────────────

    private sealed class Serializer
    {
        private readonly List<MathRun> _opaqueParts = [];

        // A fragment's text does not depend on where it stands, so each one is written (and proven) once.
        private readonly Dictionary<MathRun, string> _fragments = new(ReferenceEqualityComparer.Instance);

        public IReadOnlyList<MathRun> OpaqueParts => _opaqueParts;

        public bool Verify(string text, IReadOnlyList<MathRun> expected) =>
            EquationStructure.AreEquivalent(ParseRuns(text, _opaqueParts), expected);

        public string OpaqueToken(MathRun run)
        {
            _opaqueParts.Add(run);
            return Symbols.OpaqueOpen
                + (_opaqueParts.Count - 1).ToString(CultureInfo.InvariantCulture)
                + Symbols.OpaqueClose;
        }

        /// <summary>
        /// Writes a canonical fragment list. Each fragment is first written on its own; where two
        /// neighbours would read differently side by side one of them is isolated in invisible brackets.
        /// </summary>
        public string Sequence(IReadOnlyList<MathRun> runs)
        {
            if (runs.Count == 0)
                return string.Empty;

            var pieces = runs.Select(Fragment).ToList();
            var joined = string.Concat(pieces);
            if (runs.Count == 1 || Verify(joined, runs))
                return joined;

            // Neighbours interact only over a short distance, so first settle each fragment against the two
            // before it; the result is then proven as a whole.
            var windowed = new List<string>(runs.Count);
            for (var index = 0; index < runs.Count; index++)
            {
                var windowStart = Math.Max(0, index - 2);
                var before = string.Concat(windowed.Skip(windowStart));
                var expected = runs.Skip(windowStart).Take(index - windowStart + 1).ToList();
                var isolatedPiece = Isolate(pieces[index]);
                if (Verify(before + pieces[index], expected))
                {
                    windowed.Add(pieces[index]);
                }
                else
                {
                    if (index > 0 && !Verify(before + isolatedPiece, expected))
                        windowed[index - 1] = Isolate(windowed[index - 1]);
                    windowed.Add(isolatedPiece);
                }
            }

            var windowedText = string.Concat(windowed);
            if (Verify(windowedText, runs))
                return windowedText;

            var builder = new StringBuilder();
            var written = new List<MathRun>();
            var previousStart = 0;
            for (var index = 0; index < runs.Count; index++)
            {
                written.Add(runs[index]);
                var prefix = builder.ToString();
                var isolated = Isolate(pieces[index]);
                string? chosen = null;

                if (Verify(prefix + pieces[index], written))
                {
                    chosen = pieces[index];
                }
                else if (Verify(prefix + isolated, written))
                {
                    chosen = isolated;
                }
                else if (index > 0)
                {
                    var isolatedPrevious = prefix[..previousStart] + Isolate(prefix[previousStart..]);
                    if (Verify(isolatedPrevious + isolated, written))
                    {
                        builder.Clear().Append(isolatedPrevious);
                        chosen = isolated;
                    }
                }

                chosen ??= Isolate(OpaqueToken(runs[index]));
                previousStart = builder.Length;
                builder.Append(chosen);
            }

            return builder.ToString();
        }

        private static string Isolate(string piece) => Symbols.InvisibleOpen + piece + Symbols.InvisibleClose;

        private string Fragment(MathRun run)
        {
            if (run.Kind == MathRunKind.Text)
                return Text(run.Text);

            if (_fragments.TryGetValue(run, out var cached))
                return cached;

            var written = WriteFragment(run);
            _fragments[run] = written;
            return written;
        }

        private string WriteFragment(MathRun run)
        {
            if (run.Kind != MathRunKind.Preserved)
            {
                MathRun[] expected = [run];
                var compact = Structure(run, compact: true);
                if (compact is not null && Verify(compact, expected))
                    return compact;

                var strict = Structure(run, compact: false);
                if (strict is not null && strict != compact && Verify(strict, expected))
                    return strict;
            }

            return OpaqueToken(run);
        }

        private string Text(string text)
        {
            MathRun[] expected = [MathRun.PlainText(text)];

            // Brackets are always escaped: one left unpaired in text would pair with a bracket of an
            // enclosing structure once this text is written inside it.
            var minimal = Escape(text, element =>
                element is "\\" or "\"" or "&" or "@" or Symbols.ArgumentSeparator or Symbols.ExplicitOpen or Symbols.ExplicitClose
                || Symbols.OpenBrackets.Contains(element)
                || Symbols.CloseBrackets.Contains(element)
                || Symbols.BarBrackets.Contains(element));
            if (Verify(minimal, expected))
                return minimal;

            var escaped = Escape(text, Symbols.IsStructural);
            if (Verify(escaped, expected))
                return escaped;

            var quoted = new StringBuilder("\"");
            foreach (var ch in text)
            {
                if (ch is '\\' or '"')
                    quoted.Append('\\');
                quoted.Append(ch);
            }

            return quoted.Append('"').ToString();
        }

        private static string Escape(string text, Func<string, bool> needsEscape)
        {
            var builder = new StringBuilder(text.Length + 8);
            foreach (var element in Elements(text))
            {
                if (needsEscape(element))
                    builder.Append('\\');
                builder.Append(element);
            }

            return builder.ToString();
        }

        private static bool IsSelfDelimited(MathRun run) => run.Kind switch
        {
            MathRunKind.Radical or MathRunKind.Matrix or MathRunKind.EquationArray or MathRunKind.Bar
                or MathRunKind.GroupChar or MathRunKind.Preserved => true,
            MathRunKind.Delimiter => !(run.OpenChar == "(" && run.CloseChar == ")" && run.AdditionalDelimiterArguments.Count == 0),
            _ => false
        };

        private static bool IsScript(MathRun run) =>
            run.Kind is MathRunKind.Superscript or MathRunKind.Subscript or MathRunKind.SubSuperscript
                or MathRunKind.LowerLimit or MathRunKind.UpperLimit;

        private string Parenthesized(List<MathRun> runs) => "(" + Sequence(runs) + ")";

        private string Invisible(List<MathRun> runs) => Symbols.InvisibleOpen + Sequence(runs) + Symbols.InvisibleClose;

        /// <summary>An operand in a position where the grammar drops enclosing parentheses.</summary>
        private string StrippedOperand(List<MathRun> runs, bool compact, bool allowScripts)
        {
            if (EquationStructure.IsPlainText(runs, out var text) && IsSimpleAtom(text))
                return text;

            if (compact && runs.Count == 1 && (IsSelfDelimited(runs[0]) || (allowScripts && IsScript(runs[0]))))
                return Fragment(runs[0]);

            return Parenthesized(runs);
        }

        /// <summary>An operand in a position where parentheses would be kept as a delimiter.</summary>
        private string KeptOperand(List<MathRun> runs, bool compact, bool allowFunctionName)
        {
            if (EquationStructure.IsPlainText(runs, out var text))
            {
                if (IsSimpleAtom(text))
                    return text;
                if (compact && allowFunctionName && Symbols.FunctionNames.Contains(text))
                    return text;
            }

            if (compact && runs.Count == 1 && (IsSelfDelimited(runs[0]) || runs[0].Kind == MathRunKind.Delimiter))
                return Fragment(runs[0]);

            return Invisible(runs);
        }

        private string? Structure(MathRun run, bool compact)
        {
            switch (run.Kind)
            {
                case MathRunKind.Superscript:
                    return ScriptBase(run, compact)
                        + "^" + StrippedOperand(EquationStructure.SlotRuns(run.ScriptSupEquation, run.Sup), compact, allowScripts: false);

                case MathRunKind.Subscript:
                    return ScriptBase(run, compact)
                        + "_" + StrippedOperand(EquationStructure.SlotRuns(run.ScriptSubEquation, run.Sub), compact, allowScripts: false);

                case MathRunKind.SubSuperscript:
                    return ScriptBase(run, compact)
                        + "_" + StrippedOperand(EquationStructure.SlotRuns(run.ScriptSubEquation, run.Sub), compact, allowScripts: false)
                        + "^" + StrippedOperand(EquationStructure.SlotRuns(run.ScriptSupEquation, run.Sup), compact, allowScripts: false);

                case MathRunKind.LowerLimit:
                    return ScriptBase(run, compact)
                        + Symbols.Below + StrippedOperand(EquationStructure.SlotRuns(run.ScriptSubEquation, run.Sub), compact, allowScripts: false);

                case MathRunKind.UpperLimit:
                    return ScriptBase(run, compact)
                        + Symbols.Above + StrippedOperand(EquationStructure.SlotRuns(run.ScriptSupEquation, run.Sup), compact, allowScripts: false);

                case MathRunKind.Fraction:
                    return StrippedOperand(EquationStructure.SlotRuns(run.NumeratorEquation, run.Numerator), compact, allowScripts: true)
                        + FractionOperator(run.FractionType)
                        + StrippedOperand(EquationStructure.SlotRuns(run.DenominatorEquation, run.Denominator), compact, allowScripts: true);

                case MathRunKind.Radical:
                {
                    var degree = EquationStructure.SlotRuns(run.DegreeEquation, run.Degree);
                    var radicand = Sequence(EquationStructure.SlotRuns(run.RadicandEquation, run.Base));
                    return degree.Count == 0
                        ? Symbols.Radical + "(" + radicand + ")"
                        : Symbols.Radical + "(" + Sequence(degree) + "&" + radicand + ")";
                }

                case MathRunKind.NAry:
                {
                    if (!Symbols.NAryOperators.Contains(run.Operator))
                        return null;

                    var builder = new StringBuilder(run.Operator);
                    var lower = EquationStructure.SlotRuns(run.NAryLowerLimitEquation, run.Sub);
                    var upper = EquationStructure.SlotRuns(run.NAryUpperLimitEquation, run.Sup);
                    if (lower.Count > 0)
                        builder.Append('_').Append(StrippedOperand(lower, compact, allowScripts: false));
                    if (upper.Count > 0)
                        builder.Append('^').Append(StrippedOperand(upper, compact, allowScripts: false));
                    builder.Append(Symbols.Of)
                        .Append(KeptOperand(EquationStructure.SlotRuns(run.NAryOperandEquation, run.Base), compact, allowFunctionName: false));
                    return builder.ToString();
                }

                case MathRunKind.Accent:
                {
                    if (!Symbols.Accents.Contains(run.Accent))
                        return null;

                    var accentBase = EquationStructure.SlotRuns(run.DecoratorBaseEquation, run.Base);
                    return (EquationStructure.IsPlainText(accentBase, out var text) && IsSimpleAtom(text)
                            ? text
                            : Parenthesized(accentBase))
                        + run.Accent;
                }

                case MathRunKind.Bar:
                    return (run.BarTop ? Symbols.Overbar : Symbols.Underbar)
                        + Parenthesized(EquationStructure.SlotRuns(run.DecoratorBaseEquation, run.Base));

                case MathRunKind.GroupChar:
                    return Symbols.GroupCharacters.TryGetValue(run.GroupChr, out var position)
                        && string.Equals(position, run.GroupChrPos, StringComparison.OrdinalIgnoreCase)
                            ? run.GroupChr + Parenthesized(EquationStructure.SlotRuns(run.DecoratorBaseEquation, run.Base))
                            : null;

                case MathRunKind.Delimiter:
                    return Delimiter(run, compact);

                case MathRunKind.Matrix:
                case MathRunKind.EquationArray:
                    return Matrix(run);

                case MathRunKind.FunctionApply:
                    return Function(run, compact);

                default:
                    return null;
            }
        }

        private string ScriptBase(MathRun run, bool compact) =>
            KeptOperand(EquationStructure.SlotRuns(run.ScriptBaseEquation, run.Base), compact, allowFunctionName: true);

        private string? Delimiter(MathRun run, bool compact)
        {
            if (run.AdditionalDelimiterArguments.Count > 0 && run.DelimiterSeparator != "|")
                return null;

            var arguments = new List<string>
            {
                Sequence(EquationStructure.SlotRuns(run.DelimiterContentEquation, run.Base))
            };
            for (var index = 0; index < run.AdditionalDelimiterArguments.Count; index++)
            {
                var equation = index < run.AdditionalDelimiterContentEquations.Count
                    ? run.AdditionalDelimiterContentEquations[index]
                    : null;
                arguments.Add(Sequence(EquationStructure.SlotRuns(equation, run.AdditionalDelimiterArguments[index])));
            }

            var inner = string.Join(Symbols.ArgumentSeparator, arguments);
            var open = run.OpenChar ?? string.Empty;
            var close = run.CloseChar ?? string.Empty;
            if (compact && open == close && Symbols.BarBrackets.Contains(open))
                return open + inner + close;

            var openMarker = open.Length == 0
                ? Symbols.InvisibleOpen
                : Symbols.OpenBrackets.Contains(open) && open != Symbols.InvisibleOpen
                    ? open
                    : ExplicitGlyph(Symbols.ExplicitOpen, open);
            var closeMarker = close.Length == 0
                ? Symbols.InvisibleClose
                : Symbols.CloseBrackets.Contains(close) && close != Symbols.InvisibleClose
                    ? close
                    : ExplicitGlyph(Symbols.ExplicitClose, close);
            return openMarker is null || closeMarker is null ? null : openMarker + inner + closeMarker;
        }

        private static string? ExplicitGlyph(string marker, string glyph)
        {
            if (ElementAt(glyph, 0).Length != glyph.Length)
                return null;

            return marker + (Symbols.IsStructural(glyph) ? "\\" : string.Empty) + glyph;
        }

        private string? Matrix(MathRun run)
        {
            if (run.Matrix is not { } matrix)
                return null;

            var isArray = run.Kind == MathRunKind.EquationArray;
            var rows = new List<string>();
            for (var rowIndex = 0; rowIndex < matrix.RowCount; rowIndex++)
            {
                var columnCount = EquationStructure.ColumnCount(matrix, rowIndex);
                if (isArray && columnCount != 1)
                    return null;

                rows.Add(string.Join(
                    "&",
                    Enumerable.Range(0, columnCount)
                        .Select(columnIndex => Sequence(EquationStructure.CellRuns(matrix, rowIndex, columnIndex)))));
            }

            return (isArray ? Symbols.EquationArray : Symbols.Matrix) + "(" + string.Join("@", rows) + ")";
        }

        private string Function(MathRun run, bool compact)
        {
            var nameRuns = EquationStructure.SlotRuns(run.FunctionNameEquation, run.FuncName);
            string name;
            if (EquationStructure.IsPlainText(nameRuns, out var nameText)
                && compact
                && (IsSimpleAtom(nameText) || Symbols.FunctionNames.Contains(nameText)))
            {
                name = nameText;
            }
            else if (compact && nameRuns.Count == 1 && IsScript(nameRuns[0]))
            {
                name = Fragment(nameRuns[0]);
            }
            else
            {
                name = Invisible(nameRuns);
            }

            return name
                + Symbols.FunctionApply
                + KeptOperand(EquationStructure.SlotRuns(run.FunctionArgumentEquation, run.Base), compact, allowFunctionName: false);
        }
    }
}
