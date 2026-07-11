namespace FreeX.Core.Formula;

/// <summary>
/// Recursive descent parser that converts a token stream into an AST.
/// Handles operator precedence: comparison &lt; concatenation &lt; addition &lt; multiplication &lt; power &lt; unary &lt; postfix.
/// </summary>
public sealed class Parser
{
    private static readonly object ParsedTokenCacheGate = new();
    private static readonly Dictionary<int, List<ParsedTokenCacheEntry>> ParsedTokenCache = new();
    private static readonly Queue<ParsedTokenCacheEntry> ParsedTokenCacheOrder = new();
    private static int _parsedTokenCacheCount;

    private readonly List<Token> _tokens;
    private int _pos;
    private int _parseDepth;
    private int _nestingDepth;

    private sealed record ParsedTokenCacheEntry(int Hash, Token[] Tokens, FormulaNode Node);

    public Parser(List<Token> tokens)
    {
        if (tokens.Count > FormulaSafetyLimits.MaxParseTokens)
            throw new FormulaParseException(
                $"Formula contains too many tokens; maximum is {FormulaSafetyLimits.MaxParseTokens}");

        _tokens = tokens;
        _pos = 0;
    }

    /// <summary>Parse the token stream into an AST.</summary>
    public FormulaNode Parse()
    {
        var canUseCache = _pos == 0;
        var tokenHash = 0;
        if (canUseCache && TryGetCachedParse(_tokens, out var cachedNode, out tokenHash))
        {
            _pos = _tokens.Count - 1;
            return cachedNode;
        }

        var node = ParseExpression();

        if (Current.Type != TokenType.EndOfFormula)
            throw new FormulaParseException($"Unexpected token '{Current.Value}' at position {Current.Position}");

        if (canUseCache)
            AddCachedParse(_tokens, tokenHash, node);

        return node;
    }

    private static bool TryGetCachedParse(List<Token> tokens, out FormulaNode node, out int hash)
    {
        hash = ComputeTokenSequenceHash(tokens);

        lock (ParsedTokenCacheGate)
        {
            if (ParsedTokenCache.TryGetValue(hash, out var entries))
            {
                for (var i = entries.Count - 1; i >= 0; i--)
                {
                    var entry = entries[i];
                    if (TokenSequencesEqual(tokens, entry.Tokens))
                    {
                        node = entry.Node;
                        return true;
                    }
                }
            }
        }

        node = null!;
        return false;
    }

    private static void AddCachedParse(List<Token> tokens, int hash, FormulaNode node)
    {
        lock (ParsedTokenCacheGate)
        {
            if (ParsedTokenCache.TryGetValue(hash, out var existingEntries))
            {
                foreach (var existing in existingEntries)
                {
                    if (TokenSequencesEqual(tokens, existing.Tokens))
                        return;
                }
            }

            if (_parsedTokenCacheCount >= FormulaSafetyLimits.MaxParsedTokenFormulaCacheEntries)
                EvictOldestCachedParse();

            if (!ParsedTokenCache.TryGetValue(hash, out var entries))
            {
                entries = new List<ParsedTokenCacheEntry>(1);
                ParsedTokenCache[hash] = entries;
            }

            var entry = new ParsedTokenCacheEntry(hash, tokens.ToArray(), node);
            entries.Add(entry);
            ParsedTokenCacheOrder.Enqueue(entry);
            _parsedTokenCacheCount++;
        }
    }

    private static void EvictOldestCachedParse()
    {
        while (ParsedTokenCacheOrder.TryDequeue(out var oldest))
        {
            if (!ParsedTokenCache.TryGetValue(oldest.Hash, out var entries) || !entries.Remove(oldest))
                continue;

            if (entries.Count == 0)
                ParsedTokenCache.Remove(oldest.Hash);
            _parsedTokenCacheCount--;
            return;
        }
    }

    private static int ComputeTokenSequenceHash(List<Token> tokens)
    {
        var hash = new HashCode();
        hash.Add(tokens.Count);
        foreach (var token in tokens)
        {
            hash.Add(token.Type);
            hash.Add(token.Value, StringComparer.Ordinal);
            hash.Add(token.Position);
        }

        return hash.ToHashCode();
    }

    private static bool TokenSequencesEqual(List<Token> tokens, Token[] cachedTokens)
    {
        if (tokens.Count != cachedTokens.Length)
            return false;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var cached = cachedTokens[i];
            if (token.Type != cached.Type ||
                token.Position != cached.Position ||
                !string.Equals(token.Value, cached.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private Token Current => _tokens[_pos];

    private Token Peek(int offset = 1)
    {
        var index = _pos + offset;
        return index < _tokens.Count ? _tokens[index] : _tokens[^1];
    }

    private Token Advance()
    {
        var token = _tokens[_pos];
        _pos++;
        return token;
    }

    private Token Expect(TokenType type)
    {
        if (Current.Type != type)
            throw new FormulaParseException(
                $"Expected {type} but got {Current.Type} ('{Current.Value}') at position {Current.Position}");
        return Advance();
    }

    private ParseDepthFrame EnterParseFrame()
    {
        if (_parseDepth >= FormulaSafetyLimits.MaxParseDepth)
            throw new FormulaParseException(
                $"Formula nesting is too deep; maximum parse depth is {FormulaSafetyLimits.MaxParseDepth}");

        _parseDepth++;
        return new ParseDepthFrame(this);
    }

    private ParseNestingFrame EnterNesting(Token token)
    {
        if (_nestingDepth >= FormulaSafetyLimits.MaxParseNesting)
            throw new FormulaParseException(
                $"Formula nesting is too deep near '{token.Value}' at position {token.Position}; maximum nesting is {FormulaSafetyLimits.MaxParseNesting}");

        _nestingDepth++;
        return new ParseNestingFrame(this);
    }

    private readonly struct ParseDepthFrame : IDisposable
    {
        private readonly Parser _parser;

        public ParseDepthFrame(Parser parser) => _parser = parser;

        public void Dispose() => _parser._parseDepth--;
    }

    private readonly struct ParseNestingFrame : IDisposable
    {
        private readonly Parser _parser;

        public ParseNestingFrame(Parser parser) => _parser = parser;

        public void Dispose() => _parser._nestingDepth--;
    }

    // Expression → Comparison
    private FormulaNode ParseExpression()
    {
        using var frame = EnterParseFrame();
        return ParseComparison();
    }

    // Comparison → Concatenation (( '=' | '<>' | '<' | '>' | '<=' | '>=' ) Concatenation)*
    private FormulaNode ParseComparison()
    {
        var left = ParseConcatenation();

        while (Current.Type is TokenType.Equal or TokenType.NotEqual or
               TokenType.LessThan or TokenType.GreaterThan or
               TokenType.LessOrEqual or TokenType.GreaterOrEqual)
        {
            var op = Current.Type switch
            {
                TokenType.Equal => BinaryOperator.Equal,
                TokenType.NotEqual => BinaryOperator.NotEqual,
                TokenType.LessThan => BinaryOperator.LessThan,
                TokenType.GreaterThan => BinaryOperator.GreaterThan,
                TokenType.LessOrEqual => BinaryOperator.LessOrEqual,
                TokenType.GreaterOrEqual => BinaryOperator.GreaterOrEqual,
                _ => throw new InvalidOperationException()
            };
            Advance();
            var right = ParseConcatenation();
            left = new BinaryOpNode(left, op, right);
        }

        return left;
    }

    // Concatenation → Addition ( '&' Addition )*
    private FormulaNode ParseConcatenation()
    {
        var left = ParseAddition();

        while (Current.Type == TokenType.Ampersand)
        {
            Advance();
            var right = ParseAddition();
            left = new BinaryOpNode(left, BinaryOperator.Concatenate, right);
        }

        return left;
    }

    // Addition → Multiplication ( ('+' | '-') Multiplication )*
    private FormulaNode ParseAddition()
    {
        var left = ParseMultiplication();

        while (Current.Type is TokenType.Plus or TokenType.Minus)
        {
            var op = Current.Type == TokenType.Plus ? BinaryOperator.Add : BinaryOperator.Subtract;
            Advance();
            var right = ParseMultiplication();
            left = new BinaryOpNode(left, op, right);
        }

        return left;
    }

    // Multiplication -> Power ( ('*' | '/') Power )*
    private FormulaNode ParseMultiplication()
    {
        var left = ParsePower();

        while (Current.Type is TokenType.Multiply or TokenType.Divide)
        {
            var op = Current.Type == TokenType.Multiply ? BinaryOperator.Multiply : BinaryOperator.Divide;
            Advance();
            var right = ParsePower();
            left = new BinaryOpNode(left, op, right);
        }

        return left;
    }

    // Power -> Unary ( '^' Power )? - right-associative: 2^3^2 = 2^(3^2) = 512
    // Excel gives unary signs higher precedence than exponentiation: -2^2 = (-2)^2.
    private FormulaNode ParsePower()
    {
        using var frame = EnterParseFrame();
        var left = ParseUnary();

        if (Current.Type == TokenType.Power)
        {
            Advance();
            var right = ParsePower();
            return new BinaryOpNode(left, BinaryOperator.Power, right);
        }

        return left;
    }

    // Unary -> ('-' | '+' | '@') Unary | Postfix
    private FormulaNode ParseUnary()
    {
        using var frame = EnterParseFrame();
        if (Current.Type == TokenType.ImplicitIntersection)
        {
            Advance();
            var operand = ParseUnary();
            return new UnaryOpNode(UnaryOperator.ImplicitIntersection, operand);
        }

        if (Current.Type == TokenType.Minus)
        {
            Advance();
            var operand = ParseUnary();
            return new UnaryOpNode(UnaryOperator.Negate, operand);
        }

        if (Current.Type == TokenType.Plus)
        {
            Advance();
            return ParseUnary();
        }

        return ParsePostfix();
    }

    // Postfix → Primary ( '%' | '#' )*
    private FormulaNode ParsePostfix()
    {
        var node = ParsePrimary();

        while (true)
        {
            if (Current.Type == TokenType.Percent)
            {
                Advance();
                node = new UnaryOpNode(UnaryOperator.Percent, node);
                continue;
            }

            if (Current.Type == TokenType.Hash)
            {
                var hashToken = Advance();

                // A1#:B5 — the spill range used as the start endpoint of a larger range, e.g.
                // =SUM(A1#:B5). Excel expands this to the union of A1's current spill extent and
                // B5 (i.e. the smallest rectangle covering both). Only meaningful directly after a
                // spill anchor, so this check only fires here, never for a bare ':' range that
                // ParsePrimary's CellRef case already consumed on its own. The anchor argument
                // must stay the raw CellRefNode/NamedRangeNode (not first wrapped in its own
                // ANCHORARRAY(ref) via WrapSpillAnchor below) — EvaluateAnchorArray's
                // TryResolveAnchorAddress only understands those two shapes directly, so a
                // double-wrapped ANCHORARRAY(ANCHORARRAY(ref), end) would fail to resolve.
                if (Current.Type == TokenType.Colon)
                {
                    // Validate the anchor shape up front (same check WrapSpillAnchor performs)
                    // so A1#:B5 with an invalid anchor still reports the same parse error as A1#.
                    if (node is not (CellRefNode or NamedRangeNode))
                        throw new FormulaParseException($"Unexpected '#' at position {hashToken.Position}");

                    Advance();
                    if (Current.Type != TokenType.CellRef)
                        throw new FormulaParseException(
                            $"Expected cell reference after ':' at position {Current.Position}");
                    var endRef = ParseCellRef(Advance());
                    // A malformed end token (e.g. row 0 or out of range) parses to an ErrorNode
                    // rather than a CellRefNode — surface that #REF! directly, same as the plain
                    // A1:B5 range case (ParsePrimary's CellRef case) does for its end token.
                    node = endRef is CellRefNode endCellRef
                        ? new FunctionCallNode("ANCHORARRAY", [node, endCellRef])
                        : endRef;
                }
                else
                {
                    node = WrapSpillAnchor(node, hashToken);
                }

                continue;
            }

            break;
        }

        return node;
    }

    // The A1# spill-anchor operator: only meaningful directly after a reference to a single cell
    // (the anchor of a dynamic-array spill) — either a plain cell reference (A1#) or a named range
    // that itself resolves to a single cell (MyCell#). Represented internally as ANCHORARRAY(ref) —
    // the same node the evaluator and dependency collector already know how to evaluate/track — so
    // no other component needs to learn a new node type. Anything else is a parse error, same as
    // before '#' was recognized at all.
    private static FormulaNode WrapSpillAnchor(FormulaNode node, Token hashToken)
    {
        return node is CellRefNode or NamedRangeNode
            ? new FunctionCallNode("ANCHORARRAY", [node])
            : throw new FormulaParseException($"Unexpected '#' at position {hashToken.Position}");
    }

    // Primary → Number | String | Boolean | FunctionCall | CellRef (potentially with ':' range) | '(' Expression ')'
    private FormulaNode ParsePrimary()
    {
        switch (Current.Type)
        {
            case TokenType.Number:
            {
                if (Peek().Type == TokenType.Colon && TryParseFullRowRange(null, out var fullRowRange))
                    return fullRowRange;

                var token = Advance();
                return new NumberNode(double.Parse(token.Value, System.Globalization.CultureInfo.InvariantCulture));
            }

            case TokenType.String:
            {
                var token = Advance();
                return new StringNode(token.Value);
            }

            case TokenType.Boolean:
            {
                var token = Advance();
                return new BooleanNode(token.Value == "TRUE");
            }

            case TokenType.Error:
            {
                var token = Advance();
                return new ErrorNode(ParseErrorValue(token.Value));
            }

            case TokenType.FunctionName:
            {
                var name = Advance();
                var openParen = Expect(TokenType.OpenParen);
                using var nesting = EnterNesting(openParen);
                var args = ParseArgumentList();
                Expect(TokenType.CloseParen);
                return new FunctionCallNode(name.Value, args);
            }

            case TokenType.SheetQualifier:
            {
                var sheetToken = Advance();

                // A quoted 3-D span (e.g. 'Sheet 1:Sheet 3'!A1) is lexed as a single SheetQualifier
                // token whose value contains the whole "Start:End" text — quoting always wraps the
                // entire span when either sheet name needs it, mirroring Excel's own serialization.
                // ':' can never appear in a real (unquoted-content) sheet name (see
                // Workbook.InvalidSheetNameChars), so any ':' found here is unambiguously the span
                // separator, never part of either sheet's name.
                var colonIndex = sheetToken.Value.IndexOf(':');
                if (colonIndex >= 0)
                {
                    var startSheet = sheetToken.Value[..colonIndex];
                    var endSheet = sheetToken.Value[(colonIndex + 1)..];
                    return ParseSheetSpanBody(startSheet, endSheet);
                }

                return ParseSheetQualifiedReference(sheetToken.Value);
            }

            case TokenType.CellRef:
            {
                var cellRef = ParseCellRef(Advance());
                if (cellRef is not CellRefNode rangeStartRef)
                    return cellRef;

                // Check for range operator ':'
                if (Current.Type == TokenType.Colon)
                {
                    Advance();
                    if (Current.Type != TokenType.CellRef)
                        throw new FormulaParseException(
                            $"Expected cell reference after ':' at position {Current.Position}");
                    var endRef = ParseCellRef(Advance());
                    if (endRef is not CellRefNode rangeEndRef)
                        return endRef;
                    return new RangeRefNode(rangeStartRef, rangeEndRef);
                }

                return cellRef;
            }

            case TokenType.NamedRange:
            {
                // A 3-D sheet-span reference (e.g. Sheet1:Sheet3!A1) starts with a bare sheet-name
                // token, followed by ':', followed by the end sheet's SheetQualifier token (the
                // lexer already consumed the end sheet's trailing '!' into that token). This shape
                // is unambiguous versus a full-column/full-row range: those only ever have a bare
                // column/row token (never a SheetQualifier) as their second endpoint when the range
                // itself is unqualified, so check for the span first.
                //
                // Known narrow limitation: this only fires when the start sheet name lexes as a
                // NamedRange token. A sheet whose name happens to look exactly like a valid cell
                // reference (e.g. a sheet literally named "AB12") instead lexes its bare name as a
                // CellRef token (see Lexer.IsCellReference), so a span starting with such a sheet
                // (unquoted) falls through to the CellRef primary case below and fails to parse
                // (-> #VALUE!, not a crash) rather than being recognized as a span. Real Excel
                // cannot produce this case: it refuses to let you name a sheet like a cell reference
                // in the first place, unlike this codebase's own (looser) sheet-name validation.
                if (Current.Type == TokenType.NamedRange &&
                    Peek().Type == TokenType.Colon &&
                    Peek(2).Type == TokenType.SheetQualifier &&
                    TryParseSheetSpanReference(Current.Value, out var spanRange))
                    return spanRange;

                if (Peek().Type == TokenType.Colon && TryParseFullColumnRange(null, out var fullColumnRange))
                    return fullColumnRange;

                if (Peek().Type == TokenType.Colon && TryParseFullRowRange(null, out var fullRowRange))
                    return fullRowRange;

                var token = Advance();
                if (Current.Type == TokenType.StructuredReferenceSelector)
                {
                    var selector = Advance();
                    // An empty selector — tblName[] — means the entire data body (all columns).
                    // Pass "" through to the resolver which handles this case.
                    if (string.IsNullOrWhiteSpace(selector.Value))
                        return new StructuredReferenceNode(token.Value, "");
                    if (selector.Value.Trim().StartsWith('@'))
                        return new StructuredCurrentRowReferenceNode(
                            selector.Value.Trim()[1..].Trim(),
                            token.Value);
                    return new StructuredReferenceNode(token.Value, selector.Value.Trim());
                }

                return new NamedRangeNode(token.Value);
            }

            case TokenType.StructuredReferenceSelector:
            {
                var selector = Advance();
                var value = selector.Value.Trim();
                if (value.StartsWith('@') && value.Length > 1)
                    return new StructuredCurrentRowReferenceNode(value[1..].Trim());
                if (value.Contains("#This Row", StringComparison.OrdinalIgnoreCase))
                    return new StructuredReferenceNode("", value);
                if (!string.IsNullOrWhiteSpace(value))
                    // A bare [Column] (no @, no #This Row, no table name) is an unqualified structured
                    // reference to the table the formula cell belongs to — e.g. =SUBTOTAL(109,[Sales]) in a
                    // totals row. Resolve it against the owning table rather than failing to parse.
                    return new StructuredReferenceNode("", value);

                throw new FormulaParseException(
                    $"Expected current-row structured reference at position {selector.Position}");
            }

            case TokenType.OpenParen:
            {
                var openParen = Advance();
                using var nesting = EnterNesting(openParen);
                var expr = ParseExpression();
                Expect(TokenType.CloseParen);
                return expr;
            }

            case TokenType.OpenBrace:
                return ParseArrayConstant();

            default:
                throw new FormulaParseException(
                    $"Unexpected token '{Current.Value}' at position {Current.Position}");
        }
    }

    private FormulaNode ParseArrayConstant()
    {
        var openBrace = Expect(TokenType.OpenBrace);
        using var nesting = EnterNesting(openBrace);
        var rows = new List<IReadOnlyList<FormulaNode>>();
        int? expectedColumnCount = null;

        while (true)
        {
            var row = new List<FormulaNode> { ParseArrayConstantElement() };
            while (Current.Type == TokenType.Comma)
            {
                Advance();
                row.Add(ParseArrayConstantElement());
            }

            expectedColumnCount ??= row.Count;
            if (row.Count != expectedColumnCount.Value)
                throw new FormulaParseException(
                    $"Array constant rows must have the same number of columns at position {Current.Position}");
            rows.Add(row);

            if (Current.Type != TokenType.Semicolon)
                break;

            Advance();
        }

        Expect(TokenType.CloseBrace);
        return new ArrayConstantNode(rows);
    }

    private FormulaNode ParseArrayConstantElement()
    {
        return Current.Type switch
        {
            TokenType.Number => new NumberNode(double.Parse(Advance().Value, System.Globalization.CultureInfo.InvariantCulture)),
            TokenType.String => new StringNode(Advance().Value),
            TokenType.Boolean => new BooleanNode(Advance().Value == "TRUE"),
            TokenType.Error => new ErrorNode(ParseErrorValue(Advance().Value)),
            TokenType.Plus or TokenType.Minus => ParseSignedArrayConstantNumber(),
            _ => throw new FormulaParseException(
                $"Expected array constant at position {Current.Position}")
        };
    }

    private FormulaNode ParseSignedArrayConstantNumber()
    {
        bool negative = Current.Type == TokenType.Minus;
        Advance();
        if (Current.Type != TokenType.Number)
            throw new FormulaParseException(
                $"Expected number after array constant sign at position {Current.Position}");

        var value = double.Parse(Advance().Value, System.Globalization.CultureInfo.InvariantCulture);
        return new NumberNode(negative ? -value : value);
    }

    private FormulaNode ParseSheetQualifiedReference(string sheetName)
    {
        if (TryParseFullColumnRange(sheetName, out var fullColumnRange))
            return fullColumnRange;

        if (TryParseFullRowRange(sheetName, out var fullRowRange))
            return fullRowRange;

        if (Current.Type != TokenType.CellRef)
            throw new FormulaParseException(
                $"Expected cell reference after '{sheetName}!' at position {Current.Position}");

        var startRef = ParseCellRefWithSheet(Advance(), sheetName);
        if (startRef is not CellRefNode rangeStartRef)
            return startRef;

        if (Current.Type == TokenType.Colon)
        {
            Advance();
            if (Current.Type == TokenType.SheetQualifier)
                ExpectMatchingSheetQualifier(sheetName);

            if (Current.Type != TokenType.CellRef)
                throw new FormulaParseException(
                    $"Expected cell reference after ':' at position {Current.Position}");
            var endRef = ParseCellRef(Advance());
            if (endRef is not CellRefNode rangeEndRef)
                return endRef;
            return new RangeRefNode(rangeStartRef, rangeEndRef, sheetName);
        }

        return startRef;
    }

    // ── 3-D sheet-span references (e.g. Sheet1:Sheet3!A1, Sheet1:Sheet3!A1:B5) ────────────────
    //
    // Excel's 3-D references name a start and end sheet separated by ':' before the usual '!'
    // reference part; the reference covers every sheet from start to end inclusive, in workbook
    // tab order (reversed spans are normalized the same way). They are only meaningful as
    // arguments to the aggregate functions Excel allows (SUM, AVERAGE, COUNT, ...) — elsewhere
    // they evaluate to #VALUE!, matching Excel. Represented here as a RangeRefNode with
    // EndSheetName set (see FormulaNode.cs) rather than a new node kind, so the existing
    // Start/End cell-ref machinery (including the range-vs-single-cell shape) is reused as-is.

    /// <summary>
    /// Attempts to parse a 3-D span whose start sheet is a bare (unquoted) identifier already
    /// sitting in <c>Current</c> — i.e. the token shape "NamedRange Colon SheetQualifier ...".
    /// Consumes the NamedRange and Colon tokens (the SheetQualifier is consumed by
    /// <see cref="ParseSheetSpanBody"/>) only on success; leaves position unchanged on failure so
    /// callers can fall back to other interpretations (full-column/row range, plain named range).
    /// </summary>
    private bool TryParseSheetSpanReference(string startSheetName, out FormulaNode range)
    {
        var saved = _pos;
        Advance(); // the start-sheet NamedRange token
        Advance(); // ':'

        if (Current.Type != TokenType.SheetQualifier)
        {
            _pos = saved;
            range = null!;
            return false;
        }

        var endSheetToken = Advance();
        range = ParseSheetSpanBody(startSheetName, endSheetToken.Value);
        return true;
    }

    /// <summary>
    /// Parses the reference part (after "Start:End!") of a 3-D sheet span, given the already-known
    /// start/end sheet names. Shared by the unquoted-start path (<see cref="TryParseSheetSpanReference"/>)
    /// and the fully-quoted path (a single SheetQualifier token like 'Sheet 1:Sheet 3' whose value
    /// is split on ':' by the SheetQualifier case in ParsePrimary before reaching here).
    /// </summary>
    private FormulaNode ParseSheetSpanBody(string startSheetName, string endSheetName)
    {
        // A span's reference part can be a whole-column (A:A) or whole-row (1:1) shape too, just
        // like the single-sheet path (ParseSheetQualifiedReference tries these first) — represent
        // it directly as a RangeRefNode spanning row 1..MaxRow (or col A..MaxCol) with EndSheetName
        // set, since FullColumnRangeRefNode/FullRowRangeRefNode have no span (EndSheetName) slot of
        // their own and RangeRefNode already carries one.
        if (TryParseFullColumnSpanBody(startSheetName, endSheetName, out var fullColumnSpan))
            return fullColumnSpan;

        if (TryParseFullRowSpanBody(startSheetName, endSheetName, out var fullRowSpan))
            return fullRowSpan;

        if (Current.Type != TokenType.CellRef)
            throw new FormulaParseException(
                $"Expected cell reference after '{startSheetName}:{endSheetName}!' at position {Current.Position}");

        var startCellToken = Advance();
        var startCell = ParseCellRef(startCellToken);
        if (startCell is not CellRefNode startCellRef)
            return startCell; // malformed cell ref -> #REF! (ParseCellRef already produced ErrorNode)

        if (Current.Type == TokenType.Colon)
        {
            Advance();
            // A span's range part is never itself sheet-qualified again (Sheet1:Sheet3!A1:Sheet1!B5
            // is not valid Excel syntax) — same restriction the single-sheet path enforces via
            // ExpectMatchingSheetQualifier, just rejected outright here since there is no sensible
            // "matching" qualifier for a span's second endpoint.
            if (Current.Type == TokenType.SheetQualifier)
                throw new FormulaParseException(
                    $"Unexpected sheet qualifier '{Current.Value}!' at position {Current.Position}");

            if (Current.Type != TokenType.CellRef)
                throw new FormulaParseException(
                    $"Expected cell reference after ':' at position {Current.Position}");

            var endCellToken = Advance();
            var endCell = ParseCellRef(endCellToken);
            if (endCell is not CellRefNode endCellRef)
                return endCell;

            return new RangeRefNode(startCellRef, endCellRef, startSheetName, endSheetName);
        }

        // Bare single-cell span (e.g. Sheet1:Sheet3!A1, no ':A1:B5' range part) — represent as
        // Start == End with IsSingleCellSpan set so FormulaSerializer reprints just "A1", not a
        // synthesized "A1:A1" that was never in the source text.
        return new RangeRefNode(startCellRef, startCellRef, startSheetName, endSheetName, IsSingleCellSpan: true);
    }

    /// <summary>
    /// Parses a whole-column reference part of a 3-D span (e.g. the "A:A" in Sheet1:Sheet3!A:A),
    /// mirroring <see cref="TryParseFullColumnRange"/> but producing a RangeRefNode with
    /// EndSheetName set (spanning row 1..MaxRow on the given column(s)) since
    /// FullColumnRangeRefNode has no span slot. Leaves position unchanged on failure.
    /// </summary>
    private bool TryParseFullColumnSpanBody(string startSheetName, string endSheetName, out FormulaNode range)
    {
        range = null!;
        if (!TryParseColumnToken(Current, out var startColumn, out var isStartAbsolute))
            return false;

        if (Peek().Type != TokenType.Colon)
            return false;

        var saved = _pos;
        Advance();
        Advance();

        // Same restriction as the cell-range span body: the second endpoint is never itself
        // sheet-qualified again.
        if (Current.Type == TokenType.SheetQualifier)
            throw new FormulaParseException(
                $"Unexpected sheet qualifier '{Current.Value}!' at position {Current.Position}");

        if (!TryParseColumnToken(Current, out var endColumn, out var isEndAbsolute))
        {
            _pos = saved;
            return false;
        }

        Advance();
        var start = new CellRefNode(startColumn, 1, isStartAbsolute, false, startSheetName);
        var end = new CellRefNode(endColumn, Model.CellAddress.MaxRow, isEndAbsolute, false);
        range = new RangeRefNode(start, end, startSheetName, endSheetName);
        return true;
    }

    /// <summary>
    /// Parses a whole-row reference part of a 3-D span (e.g. the "1:1" in Sheet1:Sheet3!1:1),
    /// mirroring <see cref="TryParseFullRowRange"/> but producing a RangeRefNode with
    /// EndSheetName set (spanning col A..MaxCol on the given row(s)) since FullRowRangeRefNode
    /// has no span slot. Leaves position unchanged on failure.
    /// </summary>
    private bool TryParseFullRowSpanBody(string startSheetName, string endSheetName, out FormulaNode range)
    {
        range = null!;
        if (!TryParseRowToken(Current, out var startRow, out var isStartAbsolute))
            return false;

        if (Peek().Type != TokenType.Colon)
            return false;

        var saved = _pos;
        Advance();
        Advance();

        if (Current.Type == TokenType.SheetQualifier)
            throw new FormulaParseException(
                $"Unexpected sheet qualifier '{Current.Value}!' at position {Current.Position}");

        if (!TryParseRowToken(Current, out var endRow, out var isEndAbsolute))
        {
            _pos = saved;
            return false;
        }

        Advance();
        var start = new CellRefNode("A", startRow, false, isStartAbsolute, startSheetName);
        var end = new CellRefNode(Model.CellAddress.NumberToColumnName(Model.CellAddress.MaxCol), endRow, false, isEndAbsolute);
        range = new RangeRefNode(start, end, startSheetName, endSheetName);
        return true;
    }

    private bool TryParseFullColumnRange(string? sheetName, out FormulaNode range)
    {
        range = null!;
        if (!TryParseColumnToken(Current, out var startColumn, out var isStartAbsolute))
            return false;

        if (Peek().Type != TokenType.Colon)
            return false;

        var saved = _pos;
        Advance();
        Advance();

        if (Current.Type == TokenType.SheetQualifier)
            ExpectMatchingSheetQualifier(sheetName);

        if (!TryParseColumnToken(Current, out var endColumn, out var isEndAbsolute))
        {
            _pos = saved;
            return false;
        }

        Advance();
        range = new FullColumnRangeRefNode(startColumn, endColumn, isStartAbsolute, isEndAbsolute, sheetName);
        return true;
    }

    private bool TryParseFullRowRange(string? sheetName, out FormulaNode range)
    {
        range = null!;
        if (!TryParseRowToken(Current, out var startRow, out var isStartAbsolute))
            return false;

        if (Peek().Type != TokenType.Colon)
            return false;

        var saved = _pos;
        Advance();
        Advance();

        if (Current.Type == TokenType.SheetQualifier)
            ExpectMatchingSheetQualifier(sheetName);

        if (!TryParseRowToken(Current, out var endRow, out var isEndAbsolute))
        {
            _pos = saved;
            return false;
        }

        Advance();
        range = new FullRowRangeRefNode(startRow, endRow, isStartAbsolute, isEndAbsolute, sheetName);
        return true;
    }

    private void ExpectMatchingSheetQualifier(string? sheetName)
    {
        var endSheetToken = Advance();
        if (sheetName is null)
            throw new FormulaParseException(
                $"Unexpected sheet qualifier '{endSheetToken.Value}!' at position {endSheetToken.Position}");

        if (!string.Equals(endSheetToken.Value, sheetName, StringComparison.OrdinalIgnoreCase))
            throw new FormulaParseException(
                $"Range start and end must be on the same sheet; got '{sheetName}' and '{endSheetToken.Value}'");
    }

    private static FormulaNode ParseCellRef(Token token)
    {
        var value = token.Value;   // e.g. "$B$3", "$B3", "B$3", "B3"
        var i = 0;

        bool isColAbs = false;
        if (i < value.Length && value[i] == '$') { isColAbs = true; i++; }

        int colStart = i;
        while (i < value.Length && char.IsLetter(value[i])) i++;
        var colName = value[colStart..i];

        // No column letters parsed — not a valid cell reference
        if (colStart == i) return new ErrorNode(Model.ErrorValue.Ref);

        bool isRowAbs = false;
        if (i < value.Length && value[i] == '$') { isRowAbs = true; i++; }

        if (!uint.TryParse(value[i..], out var row) || row == 0 || row > Model.CellAddress.MaxRow)
            return new ErrorNode(Model.ErrorValue.Ref);

        var colNum = Model.CellAddress.ColumnNameToNumber(colName);
        if (colNum == 0 || colNum > Model.CellAddress.MaxCol)
            return new ErrorNode(Model.ErrorValue.Ref);

        return new CellRefNode(colName, row, isColAbs, isRowAbs);
    }

    private static bool TryParseColumnToken(Token token, out string columnName, out bool isAbsolute)
    {
        columnName = "";
        isAbsolute = false;
        if (token.Type != TokenType.NamedRange)
            return false;

        var value = token.Value;
        if (value.StartsWith('$'))
        {
            isAbsolute = true;
            value = value[1..];
        }

        if (value.Length == 0 || value.Length > 3 || !value.All(char.IsLetter))
            return false;

        var colNum = Model.CellAddress.ColumnNameToNumber(value);
        if (colNum == 0 || colNum > Model.CellAddress.MaxCol)
            return false;

        columnName = value.ToUpperInvariant();
        return true;
    }

    private static bool TryParseRowToken(Token token, out uint row, out bool isAbsolute)
    {
        row = 0;
        isAbsolute = false;
        var value = token.Value;

        if (token.Type == TokenType.NamedRange && value.StartsWith('$'))
        {
            isAbsolute = true;
            value = value[1..];
        }
        else if (token.Type != TokenType.Number)
        {
            return false;
        }

        return uint.TryParse(value, out row) &&
               row is > 0 and <= Model.CellAddress.MaxRow;
    }

    private static FormulaNode ParseCellRefWithSheet(Token token, string sheetName)
    {
        var node = ParseCellRef(token);
        return node is CellRefNode cellRef
            ? cellRef with { SheetName = sheetName }
            : node;
    }

    private static Model.ErrorValue ParseErrorValue(string code) => code.ToUpperInvariant() switch
    {
        "#DIV/0!" => Model.ErrorValue.DivByZero,
        "#VALUE!" => Model.ErrorValue.Value,
        "#REF!" => Model.ErrorValue.Ref,
        "#NAME?" => Model.ErrorValue.Name,
        "#NULL!" => Model.ErrorValue.Null,
        "#N/A" => Model.ErrorValue.NA,
        "#NUM!" => Model.ErrorValue.Num,
        "#SPILL!" => Model.ErrorValue.Spill,
        "#CALC!" => Model.ErrorValue.Calc,
        _ => new Model.ErrorValue(code)
    };

    private List<FormulaNode> ParseArgumentList()
    {
        var args = new List<FormulaNode>();

        if (Current.Type == TokenType.CloseParen)
            return args;

        args.Add(Current.Type == TokenType.Comma
            ? new OmittedArgumentNode()
            : ParseExpression());

        while (Current.Type == TokenType.Comma)
        {
            Advance();
            args.Add(Current.Type is TokenType.Comma or TokenType.CloseParen
                ? new OmittedArgumentNode()
                : ParseExpression());
        }

        return args;
    }
}
