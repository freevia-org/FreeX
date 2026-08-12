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
    private int _chainedCallCounter;

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

        return ParseIntersection();
    }

    // Intersection → Postfix ( Intersection Postfix )*
    // Excel's explicit INTERSECTION reference operator: a plain space directly between two
    // reference operands (e.g. A1:C3 B2:D4). The Lexer only ever emits an Intersection token
    // when whitespace separates two raw CellRef/NamedRange tokens (see
    // Lexer.InsertIntersectionTokens) -- never around an operator, comma, or paren -- so this
    // loop only fires for genuine intersection syntax; every pre-existing whitespace-tolerant
    // formula shape (e.g. SUM( A1 , B1 )) never produces an Intersection token at all and reaches
    // ParsePostfix directly, unaffected. Sits between ':' (parsed inside ParsePrimary/ParsePostfix,
    // so binds tighter) and ordinary arithmetic (parsed by the callers above, so binds looser),
    // matching Excel's reference-operator-precedence table.
    private FormulaNode ParseIntersection()
    {
        var left = ParsePostfix();

        while (Current.Type == TokenType.Intersection)
        {
            Advance();
            var right = ParsePostfix();
            left = new IntersectionNode(left, right);
        }

        return left;
    }

    // Postfix → Primary ( '%' | '#' )*
    private FormulaNode ParsePostfix()
    {
        var node = ParsePrimary();

        // INDEX(...)/CHOOSE(...) used as one side of ':' -- real Excel's "reference form" of these
        // functions, e.g. the classic SUM(INDEX(A1:C3,1,1):INDEX(A1:C3,3,3)) technique, or
        // SUM(A1:CHOOSE(2,B5,C5)). Only the CellRef-token range path in ParsePrimary's
        // TokenType.CellRef case (below) currently builds a RangeRefNode from ':'; a
        // FunctionCallNode result like INDEX(...)/CHOOSE(...) otherwise falls straight through the
        // loop below to the 'break' at the end, leaving the trailing ':' unconsumed -- Parse() then
        // rejects it as "Unexpected token", which FormulaEvaluator surfaces as #VALUE! for the
        // whole formula. Fold INDEX(...)/CHOOSE(...) to the CellRefNode it targets when that's
        // knowable at parse time (a literal range shape and literal row/column indices for INDEX; a
        // literal index_num selecting a literal reference branch for CHOOSE) -- see
        // TryFoldFunctionEndpointToCellRef for exactly what qualifies. A dynamically-indexed INDEX
        // (e.g. INDEX(A:A,MATCH(...)), the classic dynamic named-range pattern) or a
        // dynamically-indexed CHOOSE can't be resolved this way since the parser has no access to
        // cell values; that shape is left unhandled here exactly as before (still #VALUE!, not a
        // regression). This only ever applies directly to a FunctionCallNode fresh off ParsePrimary
        // -- never after '%'/'#'/chained-call has already been applied to it -- so it's checked
        // once here, before the postfix loop below.
        if (Current.Type == TokenType.Colon && node is FunctionCallNode indexCall &&
            TryFoldFunctionEndpointToCellRef(indexCall, out var startEndpoint))
        {
            Advance();
            // Parse (and consume) the end endpoint unconditionally, even if the start already
            // resolved to an out-of-bounds #REF!, so a valid-looking end doesn't leave stray
            // tokens behind for Parse() to reject as "Unexpected token".
            var endEndpoint = ParseIndexRangeEndpoint();
            if (startEndpoint is not CellRefNode startCellRef)
                return startEndpoint;   // start side out-of-bounds -> #REF!, matching Excel

            return endEndpoint switch
            {
                CellRefNode endCellRef => new RangeRefNode(startCellRef, endCellRef, startCellRef.SheetName),
                // A defined NAME used as the end endpoint (e.g. INDEX(A1:C3,3,3):EndName) --
                // resolved to its top-left cell at evaluation time; see NamedRangeEndpointNode.
                NamedRangeNode => new NamedRangeEndpointNode(startCellRef, endEndpoint),
                _ => endEndpoint // end side out-of-bounds/malformed -> its own #REF!
            };
        }

        while (true)
        {
            if (Current.Type == TokenType.Percent)
            {
                Advance();
                node = new UnaryOpNode(UnaryOperator.Percent, node);
                continue;
            }

            // Immediate/chained invocation of a call/lambda RESULT, e.g. LAMBDA(x,x+1)(5) or the
            // curried mk(5)(3) (mk itself a LAMBDA-returning LAMBDA). FunctionCallNode.FunctionName
            // is a fixed string, not an arbitrary sub-expression, so this can't be represented as a
            // direct call node the way a plain name-call (mk(5)) is. Instead desugar
            // `expr(args)` into `LET(__call<N>, expr, __call<N>(args))` — a synthetic LET binding
            // whose body calls the freshly-bound name. This reuses the LET-scoped lambda-binding
            // path (FormulaEvaluator.EvaluateFunction: context.TryResolveLambdaBinding) that already
            // knows how to invoke a name bound to a LambdaValue, so no new AST node or evaluator
            // support is needed. If `expr` isn't actually a lambda, the bound name resolves to a
            // non-lambda scalar and TryResolveLambdaBinding's caller correctly yields #VALUE!,
            // matching what already happened (via a parse-exception fallback) before this existed.
            if (Current.Type == TokenType.OpenParen)
            {
                var callOpenParen = Advance();
                using var callNesting = EnterNesting(callOpenParen);
                var chainedArgs = ParseArgumentList();
                Expect(TokenType.CloseParen);

                var tempName = $"__call{_chainedCallCounter++}";
                var tempRef = new NamedRangeNode(tempName);
                node = new FunctionCallNode("LET",
                    [tempRef, node, new FunctionCallNode(tempName, chainedArgs)]);
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

    // Parses the end endpoint of an INDEX(...)/CHOOSE(...)-anchored range, e.g. the 'C3' or
    // 'INDEX(A1:C3,3,3)'/'CHOOSE(2,B5,C5)' half of INDEX(A1:C3,1,1):C3 /
    // INDEX(A1:C3,1,1):INDEX(A1:C3,3,3) / A1:CHOOSE(2,B5,C5). Mirrors the plain CellRef range path
    // in ParsePrimary's TokenType.CellRef case: a malformed cell-ref token still yields
    // ErrorNode(#REF!) via ParseCellRef rather than throwing, and a second INDEX(...)/CHOOSE(...)
    // call is folded the same way the start endpoint was (see TryFoldFunctionEndpointToCellRef).
    // Anything else -- an unsupported function call, a dynamically indexed INDEX/CHOOSE, or a token
    // that isn't a reference at all -- throws, exactly as the pre-existing "Expected cell reference
    // after ':'" case does for the plain CellRef path.
    private FormulaNode ParseIndexRangeEndpoint()
    {
        if (Current.Type == TokenType.CellRef)
            return ParseCellRef(Advance());

        // A defined NAME used as the range's end endpoint (e.g. A1:EndName) -- Excel resolves the
        // name to its (top-left) cell and forms the range from there. The caller wraps this in a
        // NamedRangeEndpointNode rather than a plain RangeRefNode since the name can't be resolved
        // to a concrete cell until evaluation time.
        if (Current.Type == TokenType.NamedRange)
            return new NamedRangeNode(Advance().Value);

        if (Current.Type == TokenType.FunctionName)
        {
            var name = Advance();
            var openParen = Expect(TokenType.OpenParen);
            using var nesting = EnterNesting(openParen);
            var args = ParseArgumentList();
            Expect(TokenType.CloseParen);
            var call = new FunctionCallNode(name.Value, args);
            if (TryFoldFunctionEndpointToCellRef(call, out var endResult))
                return endResult;
        }

        throw new FormulaParseException(
            $"Expected cell reference after ':' at position {Current.Position}");
    }

    // Dispatches a FunctionCallNode used as one side of ':' to whichever reference-returning-
    // function fold applies -- INDEX's "reference form" or CHOOSE's -- matching real Excel, where
    // both (alongside IF/OFFSET/INDIRECT) can yield a genuine reference usable as a range endpoint.
    // Only these two are foldable purely from the parsed AST (no cell values available at parse
    // time); anything else returns false so the caller falls back to the pre-existing "unexpected
    // token" #VALUE! behavior, unchanged from before either fold existed.
    private static bool TryFoldFunctionEndpointToCellRef(FunctionCallNode call, out FormulaNode result)
    {
        if (call.FunctionName == "INDEX")
            return TryFoldIndexReferenceToCellRef(call, out result);

        if (call.FunctionName == "CHOOSE")
            return TryFoldChooseReferenceToCellRef(call, out result);

        result = null!;
        return false;
    }

    // INDEX's "reference form": resolves INDEX(range, row_num[, column_num]) to the CellRefNode it
    // points at, when every piece is knowable at parse time -- a literal range shape (a plain
    // CellRef/RangeRef/full-column/full-row reference, or a nested foldable INDEX) and literal
    // row/column indices. This covers real Excel's classic INDEX-as-range-endpoint technique
    // verbatim, e.g. SUM(INDEX(A1:C3,1,1):INDEX(A1:C3,3,3)). Returns false (no fold; caller falls
    // back to the pre-existing "unexpected token" #VALUE! behavior) for anything dynamic -- e.g. a
    // MATCH(...)-computed row_num, the classic dynamic named-range pattern -- since the parser has
    // no access to cell values to resolve that. An in-bounds index yields a CellRefNode; an
    // out-of-bounds one yields ErrorNode(#REF!), matching Excel and mirroring how a malformed
    // literal cell-ref token already produces ErrorNode(#REF!) elsewhere in this file (see
    // ParseCellRef).
    private static bool TryFoldIndexReferenceToCellRef(FunctionCallNode call, out FormulaNode result)
    {
        result = null!;

        // No area_num (4-arg) support; row_num alone (1 arg total) isn't valid INDEX syntax.
        if (call.FunctionName != "INDEX" || call.Arguments.Count is not (2 or 3))
            return false;

        if (!TryResolveStaticRangeDimensions(call.Arguments[0], out var sheetName, out var startRow,
                out var startCol, out var rowCount, out var colCount))
            return false;

        if (call.Arguments[1] is not NumberNode rowArgNode || !TryTruncateNonNegative(rowArgNode.Value, out var rowNum))
            return false;

        int colNum;
        if (call.Arguments.Count == 3)
        {
            if (call.Arguments[2] is not NumberNode colArgNode ||
                !TryTruncateNonNegative(colArgNode.Value, out colNum))
                return false;
        }
        else if (rowCount == 1)
        {
            // column_num omitted over a single-row range: the lone index selects the column
            // instead, matching TryEvaluateIndexDirectRange's runtime handling of the same shape.
            colNum = rowNum;
            rowNum = 1;
        }
        else if (colCount == 1)
        {
            colNum = 1;
        }
        else
        {
            // A 2-D range with column_num omitted selects an entire row (a multi-cell reference),
            // not a single cell -- not a shape this fold supports; leave unhandled.
            return false;
        }

        // 0 means "entire column/row" in real INDEX semantics -- not a single-cell reference,
        // which is all this fold produces; leave unhandled rather than misrepresent it.
        if (rowNum < 1 || colNum < 1)
            return false;

        if (rowNum > rowCount || colNum > colCount)
        {
            result = new ErrorNode(Model.ErrorValue.Ref);
            return true;
        }

        var targetRow = startRow + (uint)(rowNum - 1);
        var targetCol = startCol + (uint)(colNum - 1);
        result = new CellRefNode(Model.CellAddress.NumberToColumnName(targetCol), targetRow, SheetName: sheetName);
        return true;
    }

    // CHOOSE's "reference form": resolves CHOOSE(index_num, ref1, ref2, ...) to the CellRefNode its
    // selected branch names, when index_num is a literal number and that branch is itself a plain
    // cell reference (or another foldable reference-returning call, e.g. a nested INDEX/CHOOSE).
    // This covers real Excel's classic CHOOSE-as-range-endpoint idiom verbatim, e.g.
    // SUM(A1:CHOOSE(2,B5,C5)) or SUM(CHOOSE(1,A1,B1):C10). Returns false (no fold; caller falls
    // back to the pre-existing "unexpected token" #VALUE! behavior) for anything dynamic -- e.g. a
    // MATCH(...)-computed index_num -- since the parser has no access to cell values to resolve
    // that, and for a selected branch that isn't itself a single-cell reference (e.g. a literal or
    // a whole-range branch) -- not a shape this fold supports. A literal index_num outside CHOOSE's
    // valid 1..(Arguments.Count-1) span yields ErrorNode(#VALUE!), matching EvaluateChoose's own
    // out-of-range handling exactly (see FormulaEvaluator.ControlFlow.cs's EvaluateChoose).
    private static bool TryFoldChooseReferenceToCellRef(FunctionCallNode call, out FormulaNode result)
    {
        result = null!;

        if (call.FunctionName != "CHOOSE" || call.Arguments.Count < 2)
            return false;

        if (call.Arguments[0] is not NumberNode indexArgNode ||
            !TryTruncateNonNegative(indexArgNode.Value, out var indexNum) || indexNum < 1)
            return false;

        if (indexNum >= call.Arguments.Count)
        {
            result = new ErrorNode(Model.ErrorValue.Value);
            return true;
        }

        var selected = call.Arguments[indexNum];
        if (selected is CellRefNode)
        {
            result = selected;
            return true;
        }

        return selected is FunctionCallNode nestedCall && TryFoldFunctionEndpointToCellRef(nestedCall, out result);
    }

    // Resolves the shape of an INDEX reference argument -- its top-left cell (sheetName/startRow/
    // startCol) and its dimensions (rowCount/colCount) -- when that's knowable purely from the
    // parsed AST, no cell values needed. A 3-D sheet-span RangeRefNode (EndSheetName set) is
    // deliberately excluded: INDEX doesn't accept a multi-sheet reference, matching Excel.
    private static bool TryResolveStaticRangeDimensions(
        FormulaNode node, out string? sheetName, out uint startRow, out uint startCol,
        out long rowCount, out long colCount)
    {
        switch (node)
        {
            case CellRefNode cell:
                sheetName = cell.SheetName;
                startRow = cell.Row;
                startCol = cell.ColumnNumber;
                rowCount = 1;
                colCount = 1;
                return true;

            case RangeRefNode { EndSheetName: null } range:
                sheetName = range.SheetName;
                startRow = Math.Min(range.Start.Row, range.End.Row);
                startCol = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
                rowCount = Math.Max(range.Start.Row, range.End.Row) - startRow + 1L;
                colCount = Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber) - startCol + 1L;
                return true;

            case FullColumnRangeRefNode fullColumn:
                sheetName = fullColumn.SheetName;
                startRow = 1;
                startCol = Math.Min(fullColumn.StartColumnNumber, fullColumn.EndColumnNumber);
                rowCount = Model.CellAddress.MaxRow;
                colCount = Math.Max(fullColumn.StartColumnNumber, fullColumn.EndColumnNumber) - startCol + 1L;
                return true;

            case FullRowRangeRefNode fullRow:
                sheetName = fullRow.SheetName;
                startRow = Math.Min(fullRow.StartRow, fullRow.EndRow);
                startCol = 1;
                rowCount = Math.Max(fullRow.StartRow, fullRow.EndRow) - startRow + 1L;
                colCount = Model.CellAddress.MaxCol;
                return true;

            case FunctionCallNode nestedIndex when nestedIndex.FunctionName == "INDEX":
                // A nested INDEX(...) reference, e.g. INDEX(INDEX(A1:C10,2,2),1,1) -- fold the
                // inner call to its target cell first, then treat that single cell as a 1x1 range,
                // same as the plain CellRefNode case above.
                if (TryFoldIndexReferenceToCellRef(nestedIndex, out var nestedResult) &&
                    nestedResult is CellRefNode nestedCell)
                {
                    sheetName = nestedCell.SheetName;
                    startRow = nestedCell.Row;
                    startCol = nestedCell.ColumnNumber;
                    rowCount = 1;
                    colCount = 1;
                    return true;
                }

                sheetName = null; startRow = 0; startCol = 0; rowCount = 0; colCount = 0;
                return false;

            default:
                sheetName = null; startRow = 0; startCol = 0; rowCount = 0; colCount = 0;
                return false;
        }
    }

    // Truncates a literal row_num/column_num argument towards zero, the same coercion
    // TryEvaluateIndexDirectRange's runtime row/col handling applies via its plain (int) cast.
    // Rejects non-finite or negative values (and anything too large to be a real row/column) so the
    // caller can fall back to "no fold" instead of risking a wrong #REF!/off-by-one.
    private static bool TryTruncateNonNegative(double value, out int truncated)
    {
        truncated = 0;
        if (!double.IsFinite(value) || value < 0 || value > int.MaxValue)
            return false;

        truncated = (int)value;
        return true;
    }

    /// <summary>
    /// Cap a parsed numeric literal to Excel's 15-significant-digit storage precision. Excel
    /// truncates (zeroes) any low-order digits beyond the 15th significant digit unconditionally
    /// at entry time -- e.g. 1234567890123456 (16 digits) is stored as 1234567890123450, not left
    /// as the raw 16-digit double.Parse result. Mirrors RecalcEngine's/CellEntryParser's own
    /// RoundToSignificantDigits helper (this project cannot reference FreeX.Core.Calc's internal
    /// copy, so the identical logic is duplicated here).
    /// </summary>
    private static double CapLiteralToExcel15SigDigits(double value)
    {
        if (!double.IsFinite(value) || value == 0) return value;

        var scale = 15 - (int)Math.Floor(Math.Log10(Math.Abs(value))) - 1;
        if (scale < 0)
        {
            // The literal has more integer digits than the significant-digit cap (e.g. a 16-digit
            // literal). Excel does not round such values to the nearest 10^-scale -- it truncates
            // (chops) the excess low-order digits to zero, matching its 15-significant-digit
            // storage cap. Math.Round(double, int) only accepts digits in [0, 15] and cannot
            // express a negative scale, so replicate the truncation directly instead of clamping
            // to a no-op.
            var divisor = Math.Pow(10, -scale);
            return Math.Truncate(value / divisor) * divisor;
        }

        // Math.Round(double,int) only accepts digits in [0, 15]. A small-magnitude literal
        // (|value| < 0.1) gives scale > 15 -- clamping that back down to 15 (as if this were still
        // "round to 15 decimal PLACES") would be wrong for a genuinely tiny literal (e.g. 5E-200):
        // rounding to the nearest 1e-15 zeroes it out entirely, which is not Excel's behavior and
        // not what this cap is for. Once scale exceeds the digits Math.Round can express, the
        // literal's magnitude is already far below where a 15-significant-digit cap could ever bite
        // (a double only carries ~15-17 significant digits to begin with), so leave it unchanged.
        if (scale > 15) return value;
        return Math.Round(value, scale, MidpointRounding.AwayFromZero);
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
                return new NumberNode(CapLiteralToExcel15SigDigits(
                    double.Parse(token.Value, System.Globalization.CultureInfo.InvariantCulture)));
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

                // The external-workbook DEFINED-NAME reference shape "[n]!Name" (no sheet segment
                // at all, e.g. [1]!TaxRate) lexes to a SheetQualifier token whose value is the bare
                // "[n]" bracket with nothing to qualify against -- there is no sheet name here, so
                // routing it through ParseSheetQualifiedReference (which expects sheetToken.Value to
                // BE a real/resolvable sheet name) would be wrong. Build the NamedRangeNode directly.
                if (IsExternalLinkIndexOnlyQualifier(sheetToken.Value))
                    return ParseExternalDefinedNameReference(sheetToken.Value);

                return ParseSheetQualifiedReference(sheetToken.Value);
            }

            case TokenType.CellRef:
            {
                var cellRef = ParseCellRef(Advance());
                if (cellRef is not CellRefNode rangeStartRef)
                    return cellRef;

                // Check for range operator ':'. The end endpoint may be a plain cell reference,
                // a defined NAME (e.g. A1:EndName -- see NamedRangeEndpointNode), or -- real
                // Excel's INDEX "reference form" -- an INDEX(...) call that statically folds to
                // one (e.g. A1:INDEX(A1:C3,3,3)); ParseIndexRangeEndpoint handles all three, same
                // as the INDEX-anchored start-endpoint path in ParsePostfix above.
                if (Current.Type == TokenType.Colon)
                {
                    Advance();
                    var endRef = ParseIndexRangeEndpoint();
                    return endRef switch
                    {
                        CellRefNode rangeEndRef => new RangeRefNode(rangeStartRef, rangeEndRef),
                        NamedRangeNode => new NamedRangeEndpointNode(rangeStartRef, endRef),
                        _ => endRef
                    };
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

                // A defined NAME used as the START endpoint of the ':' range operator (e.g.
                // StartCell:B2, StartCell:EndName) -- the 3-D span and full-column/full-row checks
                // above already claimed every other shape a NamedRange-then-Colon token pair can
                // form, so reaching here with a Colon unambiguously means this. Excel resolves the
                // name to its (top-left) cell and forms the range from there; see
                // NamedRangeEndpointNode.
                if (Current.Type == TokenType.Colon)
                {
                    Advance();
                    var endpoint = ParseIndexRangeEndpoint();
                    return new NamedRangeEndpointNode(new NamedRangeNode(token.Value), endpoint);
                }

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
                // The bare '@' shorthand (no table qualifier, e.g. =[@]*2 inside a table's own row)
                // means "this entire row" — same as [#This Row] — with no column name at all, not a
                // column literally named "@". Previously only `value.Length > 1` reached this branch,
                // so the single-character "@" fell through to the generic StructuredReferenceNode
                // below, which then hunted for a column named "@" and always missed (#NAME?).
                // Route it here too, with an empty ColumnName that EvaluateCurrentRowReference
                // recognizes as the whole-row case.
                if (value.StartsWith('@'))
                    return new StructuredCurrentRowReferenceNode(value.Length > 1 ? value[1..].Trim() : "");
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

                // R85/R93-formula-areas-union: Excel's union operator groups multiple reference
                // "areas" behind an extra set of parens, e.g. AREAS((A1:B2,D5,F1:F10)) = 3, and
                // SUM((A1:A2,B1:B2)) sums across both areas. A comma here means the caller is
                // trying exactly that -- collect every comma-separated operand at this paren depth
                // into a UnionNode rather than rejecting the shape outright (R85's prior behavior).
                // See UnionNode/UnionValue for why this stays a Core.Formula-only construct instead
                // of a new Core.Model.ScalarValue kind.
                if (Current.Type == TokenType.Comma)
                {
                    var areas = new List<FormulaNode> { expr };
                    while (Current.Type == TokenType.Comma)
                    {
                        Advance();
                        areas.Add(ParseExpression());
                    }

                    Expect(TokenType.CloseParen);
                    return new UnionNode(areas);
                }

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
            TokenType.Number => new NumberNode(CapLiteralToExcel15SigDigits(
                double.Parse(Advance().Value, System.Globalization.CultureInfo.InvariantCulture))),
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

        var value = CapLiteralToExcel15SigDigits(
            double.Parse(Advance().Value, System.Globalization.CultureInfo.InvariantCulture));
        return new NumberNode(negative ? -value : value);
    }

    /// <summary>
    /// True when <paramref name="value"/> is the bracketed EXTERNAL-WORKBOOK-INDEX-ONLY shape
    /// "[n]" -- an all-digit numeric external-reference index with no sheet-name segment (e.g. the
    /// "[1]" <see cref="Lexer.TryReadExternalSheetQualifier"/> produces for the on-disk external
    /// defined-name reference form <c>[1]!TaxRate</c>) -- as opposed to the ordinary sheet-qualified
    /// shape "[n]SheetName" (e.g. "[1]Sheet1") that <see cref="ParseSheetQualifiedReference"/>
    /// handles.
    /// </summary>
    private static bool IsExternalLinkIndexOnlyQualifier(string value)
    {
        if (value.Length < 3 || value[0] != '[' || value[^1] != ']')
            return false;

        for (var i = 1; i < value.Length - 1; i++)
        {
            if (!char.IsDigit(value[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Parses the trailing defined-name identifier of an external-workbook DEFINED-NAME reference
    /// with no sheet segment (e.g. <c>[1]!TaxRate</c>) into a <see cref="NamedRangeNode"/> whose
    /// <see cref="NamedRangeNode.Name"/> carries the whole "[n]!Name" text verbatim -- a real Excel
    /// defined name can never itself contain '[', ']', or '!' (Workbook.InvalidSheetNameChars-style
    /// name-validation rules forbid them), so this shape can never collide with an ordinary name and
    /// is safe to pass straight through as an opaque lookup key. <see cref="NamedRangeNode.SheetQualifier"/>
    /// is left null -- there is no sheet to qualify against here, unlike the sheet-qualified
    /// "[n]SheetName!Name" shape. See FormulaEvaluator.Contexts.cs's
    /// ExternalSheetReferenceResolver.TryResolveExternalDefinedName for the resolution side, which
    /// recognizes this same "[n]!Name" shape (via SheetEvalContext.TryGetNamedFormulaText) and
    /// rewrites it to the already-supported quoted external-sheet cell-reference form using the
    /// cached ExternalLinkModel.DefinedNames RefersTo text.
    /// </summary>
    private FormulaNode ParseExternalDefinedNameReference(string bracketIndexText)
    {
        if (Current.Type != TokenType.NamedRange)
            throw new FormulaParseException(
                $"Expected a defined name after '{bracketIndexText}!' at position {Current.Position}");

        var nameToken = Advance();
        return new NamedRangeNode(bracketIndexText + "!" + nameToken.Value);
    }

    private FormulaNode ParseSheetQualifiedReference(string sheetName)
    {
        if (TryParseFullColumnRange(sheetName, out var fullColumnRange))
            return fullColumnRange;

        if (TryParseFullRowRange(sheetName, out var fullRowRange))
            return fullRowRange;

        // Sheet-qualified defined-name reference (e.g. =SUM(Sheet2!TaxRate)). Real Excel always
        // writes this shape for a name used from a sheet other than its own — including the
        // extremely common case of a workbook-global name qualified with an (often redundant)
        // sheet prefix. The token following the '!' lexes as NamedRange (not CellRef) whenever it
        // isn't itself a valid cell address (see Lexer.ReadIdentifierOrRef), so without this branch
        // every such formula previously threw here and surfaced as #VALUE! at every call site
        // (plain cell formulas, conditional-format rules, data-validation formulas, dependency
        // collection). The sheet qualifier is now carried on NamedRangeNode.SheetQualifier so a
        // name that is itself scope-limited to a *different* sheet than the one it's being
        // qualified with here can, in principle, resolve via that sheet's local scope; wiring the
        // evaluator's scope-resolution (FormulaEvaluator.References.cs EvaluateNamedRange /
        // ResolveNamedRangeNodeAsReference / IsSheetScopedName) to actually consult this field is a
        // residual follow-up — it still resolves purely against the formula's own current-sheet
        // scope, honouring the formula's own current-sheet scope precedence. Passing the qualifier
        // through here is otherwise the exact match for the ordinary case (workbook-global names,
        // or a qualifier that merely echoes the formula's own sheet).
        if (Current.Type == TokenType.NamedRange)
            return new NamedRangeNode(Advance().Value, sheetName);

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
