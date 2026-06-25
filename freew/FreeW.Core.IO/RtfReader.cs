using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// Reads Rich Text Format (<c>.rtf</c>) into a <see cref="TextDocument"/>. Hand-rolled tokenizer over the
/// RTF group (<c>{</c>/<c>}</c>) + control-word grammar with a formatting-state stack that mirrors the
/// nesting: each <c>{</c> pushes a copy of the current run/paragraph state and each <c>}</c> pops it, so
/// formatting scopes exactly as Word's.
///
/// <para>
/// Handles the correctness traps called out in the file-format design doc §5.3:
/// <list type="bullet">
/// <item><c>\'XX</c> hex escapes are decoded against the active code page (<c>\ansicpg</c> / <c>\fcharset</c>).</item>
/// <item><c>\uN</c> Unicode escapes consume the right number of following fallback bytes — the current
/// <c>\ucN</c> skip count — so non-ASCII does not corrupt the following text. <c>N</c> is a SIGNED 16-bit
/// value, so negative params are mapped back to their unsigned code unit.</item>
/// <item>Unknown destination groups (<c>{\*\...}</c>) are skipped wholesale.</item>
/// </list>
/// Maps <c>\b \i \ul \strike \fsN \cfN \fN \super \sub</c> to <see cref="RunFormatting"/>;
/// <c>\par \pard \ql/\qc/\qr/\qj \liN \riN \fiN \sbN \saN</c> to <see cref="Paragraph"/> /
/// <see cref="ParagraphFormatting"/>; and <c>\trowd … \cellxN \cell \row</c> to <see cref="Table"/>.
/// </para>
/// </summary>
public static class RtfReader
{
    static RtfReader()
    {
        // RTF \'XX bytes decode against Windows code pages (\ansicpg/\fcharset); register the provider once
        // so Encoding.GetEncoding(1252)/Shift-JIS/etc. resolve on net10.0 (not in the default provider set).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static TextDocument Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // RTF is a byte stream of 7-bit-friendly ASCII plus raw \'XX bytes; read it as Latin-1 so every byte
        // maps 1:1 to a char and \'XX bytes survive verbatim for later code-page decoding.
        using var reader = new StreamReader(stream, Encoding.Latin1, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        var text = reader.ReadToEnd();

        var parser = new Parser(text);
        return parser.Parse();
    }

    private sealed class State
    {
        public RunFormatting Run = RunFormatting.Default;
        public ParagraphFormatting Paragraph = ParagraphFormatting.Default;
        public int Ucskip = 1;
        public Encoding Encoding;
        public bool InTable;
        // True while inside an unknown/ignored destination group whose text must be discarded.
        public bool Ignore;

        public State(Encoding encoding) => Encoding = encoding;

        public State Clone() => new(Encoding)
        {
            Run = Run,
            Paragraph = Paragraph,
            Ucskip = Ucskip,
            InTable = InTable,
            Ignore = Ignore,
        };
    }

    private sealed class Parser
    {
        private readonly string _rtf;
        private int _pos;

        private readonly TextDocument _document = new();
        private readonly Stack<State> _stack = new();
        private State _state;

        // Pending byte buffer for \'XX sequences, flushed (decoded against the active code page) before the
        // next non-hex token so multi-byte (e.g. Shift-JIS) sequences decode together.
        private readonly List<byte> _pendingBytes = new();

        // Accumulated content for the current paragraph being built.
        private readonly List<Run> _currentRuns = new();
        private readonly StringBuilder _currentText = new();

        // Table building state.
        private Table? _currentTable;
        private TableRow? _currentRow;
        private TableCell? _currentCell;
        private readonly List<Paragraph> _cellParagraphs = new();
        // Cumulative \cellxN boundary positions (twips) for the current row definition, cleared on \trowd.
        private readonly List<int> _cellxBoundaries = new();

        // Header tables resolved while parsing: \colortbl entries (the leading bare ';' defines index 0 =
        // auto/null) and \fonttbl names keyed by \fN id.
        private readonly List<string?> _colorTable = new();
        private readonly Dictionary<int, string> _fontTable = new();

        public Parser(string rtf)
        {
            _rtf = rtf;
            _state = new State(Encoding.GetEncoding(1252));
            _document.Blocks.Clear();
        }

        public TextDocument Parse()
        {
            while (_pos < _rtf.Length)
            {
                var c = _rtf[_pos];
                switch (c)
                {
                    case '{':
                        // Close the current run before entering a new formatting scope so text already
                        // accumulated keeps the OUTER formatting.
                        FlushRun();
                        _pos++;
                        _stack.Push(_state);
                        _state = _state.Clone();
                        break;
                    case '}':
                        // Close the current run before leaving this scope so text accumulated inside the group
                        // keeps the INNER formatting, then restore the outer state.
                        FlushRun();
                        _pos++;
                        if (_stack.Count > 0)
                            _state = _stack.Pop();
                        break;
                    case '\\':
                        ParseControl();
                        break;
                    case '\r':
                    case '\n':
                        _pos++; // raw line breaks in the RTF are not content
                        break;
                    default:
                        FlushPendingBytes();
                        AppendChar(c);
                        _pos++;
                        break;
                }
            }

            // Emit any trailing paragraph that had content but no final \par.
            FlushPendingBytes();
            FinishTableIfOpen();
            if (_currentRuns.Count > 0 || _currentText.Length > 0)
                EndParagraph(emitEmpty: false);

            if (_document.Blocks.Count == 0)
                _document.Blocks.Add(new Paragraph());

            return _document;
        }

        // ---- control words ------------------------------------------------------------------------------

        private void ParseControl()
        {
            _pos++; // consume backslash
            if (_pos >= _rtf.Length)
                return;

            var c = _rtf[_pos];

            // Control symbol (a single non-alpha char), e.g. \\, \{, \}, \*, \'XX, \~, \-, \_.
            if (!char.IsLetter(c))
            {
                _pos++;
                switch (c)
                {
                    case '\\': FlushPendingBytes(); AppendChar('\\'); break;
                    case '{': FlushPendingBytes(); AppendChar('{'); break;
                    case '}': FlushPendingBytes(); AppendChar('}'); break;
                    case '\'': ParseHexByte(); break;
                    case '~': FlushPendingBytes(); AppendChar(' '); break; // non-breaking space
                    case '-': break; // optional hyphen — no visible glyph
                    case '_': FlushPendingBytes(); AppendChar('‑'); break; // non-breaking hyphen
                    case '*': MarkIgnorableDestination(); break;
                    case '\r':
                    case '\n': FlushPendingBytes(); ParPlain(); break; // \<CR>/\<LF> == \par
                    default: break; // unknown control symbol — ignore
                }
                return;
            }

            // Control word: letters then an optional (signed) numeric parameter, then an optional single space.
            var start = _pos;
            while (_pos < _rtf.Length && char.IsLetter(_rtf[_pos]))
                _pos++;
            var word = _rtf.Substring(start, _pos - start);

            int? param = null;
            var negative = false;
            if (_pos < _rtf.Length && _rtf[_pos] == '-')
            {
                negative = true;
                _pos++;
            }
            if (_pos < _rtf.Length && char.IsDigit(_rtf[_pos]))
            {
                var numStart = _pos;
                while (_pos < _rtf.Length && char.IsDigit(_rtf[_pos]))
                    _pos++;
                var n = int.Parse(_rtf.AsSpan(numStart, _pos - numStart), CultureInfo.InvariantCulture);
                param = negative ? -n : n;
            }

            // A single trailing space is the control-word delimiter and is consumed (not content).
            if (_pos < _rtf.Length && _rtf[_pos] == ' ')
                _pos++;

            // \uN is handled specially because its fallback bytes must be skipped.
            if (word == "u")
            {
                HandleUnicode(param ?? 0);
                return;
            }

            FlushPendingBytes();
            HandleControlWord(word, param);
        }

        private void MarkIgnorableDestination()
        {
            // \* introduces an ignorable destination: skip the whole enclosing group's content. We are still
            // inside that group (the '{' was already pushed), so mark this scope to discard text. The next
            // control word is the destination name; we simply ignore all content until the matching '}'.
            _state.Ignore = true;
            SkipToGroupEnd();
        }

        private void SkipToGroupEnd()
        {
            // Consume balanced groups until the '}' that closes the current ignorable destination. The opening
            // '{' has already been consumed and its state pushed; on encountering that '}' we pop normally.
            var depth = 1;
            while (_pos < _rtf.Length && depth > 0)
            {
                var c = _rtf[_pos];
                if (c == '\\')
                {
                    // Skip an escaped delimiter so \{ / \} inside the destination do not change depth.
                    _pos += 2;
                    continue;
                }
                if (c == '{')
                    depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        // Pop the state for the destination group's opening '{'.
                        if (_stack.Count > 0)
                            _state = _stack.Pop();
                        _pos++;
                        return;
                    }
                }
                _pos++;
            }
        }

        private void HandleControlWord(string word, int? param)
        {
            if (_state.Ignore)
                return;

            switch (word)
            {
                // ---- header tables we parse (so \fN / \cfN resolve to fonts / colours) ----
                case "fonttbl":
                    ParseFontTable();
                    break;
                case "colortbl":
                    ParseColorTable();
                    break;

                // ---- run font / colour references ----
                case "f":
                    if (param is { } fid && _fontTable.TryGetValue(fid, out var fname))
                        _state.Run = ApplyFlushedFont(fname);
                    break;
                case "cf":
                    if (param is { } cid && cid > 0 && cid < _colorTable.Count && _colorTable[cid] is { } chex)
                        _state.Run = ApplyFlushedColor(chex);
                    else if (param == 0)
                        _state.Run = ApplyFlushedColor(null);
                    break;

                // ---- destinations we deliberately skip whole (header tables, info, etc.) ----
                case "stylesheet":
                case "info":
                case "pict":
                case "object":
                case "header":
                case "footer":
                case "footnote":
                    _state.Ignore = true;
                    SkipToGroupEnd();
                    break;

                // ---- document code page ----
                case "ansicpg":
                    if (param is { } cp)
                        _state.Encoding = SafeEncoding(cp);
                    break;
                case "uc":
                    if (param is { } skip && skip >= 0)
                        _state.Ucskip = skip;
                    break;

                // ---- run formatting ----
                // Each toggle closes the run accumulated so far (it had the OLD formatting) before changing
                // state, so `\b bold\b0 plain` within one group splits into two correctly-formatted runs.
                case "b": FlushRun(); _state.Run = _state.Run with { Bold = param != 0 }; break;
                case "i": FlushRun(); _state.Run = _state.Run with { Italic = param != 0 }; break;
                case "ul": FlushRun(); _state.Run = _state.Run with { Underline = param != 0 }; break;
                case "ulnone": FlushRun(); _state.Run = _state.Run with { Underline = false }; break;
                case "strike": FlushRun(); _state.Run = _state.Run with { Strikethrough = param != 0 }; break;
                case "fs": FlushRun(); if (param is { } half) _state.Run = _state.Run with { FontSizePt = half / 2.0 }; break;
                case "super": FlushRun(); _state.Run = _state.Run with { VerticalAlign = VerticalAlign.Superscript }; break;
                case "sub": FlushRun(); _state.Run = _state.Run with { VerticalAlign = VerticalAlign.Subscript }; break;
                case "nosupersub": FlushRun(); _state.Run = _state.Run with { VerticalAlign = VerticalAlign.Baseline }; break;
                case "plain": FlushRun(); _state.Run = RunFormatting.Default; break;

                // ---- paragraph formatting ----
                case "pard": _state.Paragraph = ParagraphFormatting.Default; break;
                case "ql": _state.Paragraph = _state.Paragraph with { Alignment = TextAlignment.Left }; break;
                case "qc": _state.Paragraph = _state.Paragraph with { Alignment = TextAlignment.Center }; break;
                case "qr": _state.Paragraph = _state.Paragraph with { Alignment = TextAlignment.Right }; break;
                case "qj": _state.Paragraph = _state.Paragraph with { Alignment = TextAlignment.Justify }; break;
                case "li": _state.Paragraph = _state.Paragraph with { IndentLeftPt = TwipsToPt(param ?? 0) }; break;
                case "ri": _state.Paragraph = _state.Paragraph with { IndentRightPt = TwipsToPt(param ?? 0) }; break;
                case "fi": _state.Paragraph = _state.Paragraph with { FirstLineIndentPt = TwipsToPt(param ?? 0) }; break;
                case "sb": _state.Paragraph = _state.Paragraph with { SpaceBeforePt = TwipsToPt(param ?? 0) }; break;
                case "sa": _state.Paragraph = _state.Paragraph with { SpaceAfterPt = TwipsToPt(param ?? 0) }; break;

                // ---- binary data ----
                // \binN is followed by exactly N raw binary bytes that must be skipped wholesale;
                // they are not RTF text and must not be parsed as characters or control words.
                case "bin":
                    if (param is { } binCount && binCount > 0)
                        _pos = Math.Min(_pos + binCount, _rtf.Length);
                    break;

                // ---- breaks / structure ----
                case "par": ParPlain(); break;
                case "line": AppendChar('\n'); break;
                case "tab": AppendChar('\t'); break;
                case "page": _currentRuns.Add(Run.PageBreak()); break;

                // ---- tables ----
                case "intbl": _state.InTable = true; break;
                case "trowd": BeginRow(); break;
                case "cellx":
                    // Record the cumulative right-edge boundary of the next cell in twips.
                    if (param is { } cellxTwips)
                        _cellxBoundaries.Add(cellxTwips);
                    break;
                case "cell": EndCell(); break;
                case "row": EndRow(); break;
                case "nestcell": EndCell(); break;
                case "nestrow": EndRow(); break;

                default:
                    // Unknown control word — ignored. (Destinations are handled above / via \*.)
                    break;
            }
        }

        // ---- header tables ------------------------------------------------------------------------------

        /// <summary>
        /// Closes a run for a font/colour change, applying the font family. Returns the new run formatting.
        /// </summary>
        private RunFormatting ApplyFlushedFont(string family)
        {
            FlushRun();
            return _state.Run with { FontFamily = family };
        }

        private RunFormatting ApplyFlushedColor(string? hex)
        {
            FlushRun();
            return _state.Run with { ColorHex = hex };
        }

        /// <summary>
        /// Parses a <c>{\fonttbl …}</c> group into <see cref="_fontTable"/>. The opening <c>{</c> and the
        /// <c>\fonttbl</c> control word have already been consumed; this reads each <c>{\fN … FontName;}</c>
        /// sub-group and consumes the table group's closing <c>}</c> (popping the pushed state).
        /// </summary>
        private void ParseFontTable()
        {
            var depth = 1; // we are inside the \fonttbl group
            var currentId = -1;
            var name = new StringBuilder();
            while (_pos < _rtf.Length && depth > 0)
            {
                var c = _rtf[_pos];
                if (c == '\\')
                {
                    _pos++;
                    if (_pos < _rtf.Length && _rtf[_pos] == 'f' && _pos + 1 < _rtf.Length && char.IsDigit(_rtf[_pos + 1]))
                    {
                        _pos++; // 'f'
                        var numStart = _pos;
                        while (_pos < _rtf.Length && char.IsDigit(_rtf[_pos]))
                            _pos++;
                        currentId = int.Parse(_rtf.AsSpan(numStart, _pos - numStart), CultureInfo.InvariantCulture);
                        if (_pos < _rtf.Length && _rtf[_pos] == ' ')
                            _pos++;
                    }
                    else
                    {
                        // Skip any other control word/symbol (e.g. \fnil, \fcharsetN, \froman).
                        SkipControlInTable();
                    }
                }
                else if (c == '{')
                {
                    depth++;
                    currentId = -1;
                    name.Clear();
                    _pos++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (currentId >= 0 && name.Length > 0)
                        _fontTable[currentId] = name.ToString().Trim();
                    currentId = -1;
                    name.Clear();
                    _pos++;
                }
                else if (c == ';')
                {
                    if (currentId >= 0 && name.Length > 0)
                        _fontTable[currentId] = name.ToString().Trim();
                    currentId = -1;
                    name.Clear();
                    _pos++;
                }
                else
                {
                    if (currentId >= 0)
                        name.Append(c);
                    _pos++;
                }
            }
            PopAfterTableGroup();
        }

        /// <summary>
        /// Parses a <c>{\colortbl;…}</c> group into <see cref="_colorTable"/>. Index 0 is the implicit auto
        /// colour (a leading bare <c>;</c>). Each subsequent <c>;</c>-terminated entry is built from the
        /// <c>\redN\greenN\blueN</c> triplet seen since the previous <c>;</c>. Consumes the closing <c>}</c>.
        /// </summary>
        private void ParseColorTable()
        {
            int r = 0, g = 0, b = 0;
            var hasComponent = false;
            var depth = 1;
            while (_pos < _rtf.Length && depth > 0)
            {
                var c = _rtf[_pos];
                if (c == '\\')
                {
                    _pos++;
                    var start = _pos;
                    while (_pos < _rtf.Length && char.IsLetter(_rtf[_pos]))
                        _pos++;
                    var word = _rtf.Substring(start, _pos - start);
                    var val = 0;
                    if (_pos < _rtf.Length && char.IsDigit(_rtf[_pos]))
                    {
                        var ns = _pos;
                        while (_pos < _rtf.Length && char.IsDigit(_rtf[_pos]))
                            _pos++;
                        val = int.Parse(_rtf.AsSpan(ns, _pos - ns), CultureInfo.InvariantCulture);
                    }
                    if (_pos < _rtf.Length && _rtf[_pos] == ' ')
                        _pos++;
                    switch (word)
                    {
                        case "red": r = val; hasComponent = true; break;
                        case "green": g = val; hasComponent = true; break;
                        case "blue": b = val; hasComponent = true; break;
                    }
                }
                else if (c == ';')
                {
                    if (hasComponent)
                        _colorTable.Add($"#{r:X2}{g:X2}{b:X2}");
                    else
                        _colorTable.Add(null); // a bare ; is the auto colour / empty entry
                    r = g = b = 0;
                    hasComponent = false;
                    _pos++;
                }
                else if (c == '}')
                {
                    depth--;
                    _pos++;
                }
                else if (c == '{')
                {
                    depth++;
                    _pos++;
                }
                else
                {
                    _pos++;
                }
            }
            PopAfterTableGroup();
        }

        private void SkipControlInTable()
        {
            // Consume control-word letters + optional numeric param + optional trailing space.
            while (_pos < _rtf.Length && char.IsLetter(_rtf[_pos]))
                _pos++;
            if (_pos < _rtf.Length && _rtf[_pos] == '-')
                _pos++;
            while (_pos < _rtf.Length && char.IsDigit(_rtf[_pos]))
                _pos++;
            if (_pos < _rtf.Length && _rtf[_pos] == ' ')
                _pos++;
        }

        private void PopAfterTableGroup()
        {
            // The closing '}' of the table group was consumed above; restore the state pushed by its '{'.
            if (_stack.Count > 0)
                _state = _stack.Pop();
        }

        // ---- \uN and \'XX -------------------------------------------------------------------------------

        private void HandleUnicode(int param)
        {
            FlushPendingBytes();

            // \uN takes a signed 16-bit value; negative means the code unit > 32767.
            if (param < 0)
                param += 65536;
            if (!_state.Ignore)
                AppendChar((char)param);

            // Skip the configured number of fallback bytes/chars that follow the \uN escape. Each escaped
            // sequence (\'XX, \\, etc.) and each control word counts as ONE skipped unit, as does each literal
            // char — getting this count right is what keeps non-ASCII from corrupting following text.
            var toSkip = _state.Ucskip;
            while (toSkip > 0 && _pos < _rtf.Length)
            {
                var c = _rtf[_pos];
                if (c == '\\')
                {
                    if (_pos + 1 >= _rtf.Length)
                    {
                        _pos++;
                    }
                    else if (_rtf[_pos + 1] == '\'')
                    {
                        // \'XX hex escape — always exactly 4 chars: \  '  H  H
                        _pos += 4;
                    }
                    else if (char.IsLetter(_rtf[_pos + 1]))
                    {
                        // Control word: consume backslash + letter-run + optional signed numeric param + optional trailing space.
                        // This mirrors ParseControl's lexing so every control-word fallback counts as ONE unit.
                        _pos++; // consume backslash
                        while (_pos < _rtf.Length && char.IsLetter(_rtf[_pos]))
                            _pos++;
                        if (_pos < _rtf.Length && _rtf[_pos] == '-')
                            _pos++;
                        while (_pos < _rtf.Length && char.IsDigit(_rtf[_pos]))
                            _pos++;
                        if (_pos < _rtf.Length && _rtf[_pos] == ' ')
                            _pos++;
                    }
                    else
                    {
                        // Control symbol (e.g. \\ \{ \} \~ \- \'): always exactly 2 chars: \  X
                        _pos += 2;
                    }
                }
                else if (c == '{' || c == '}')
                {
                    break; // group boundaries are never fallback bytes
                }
                else
                {
                    _pos++;
                }
                toSkip--;
            }
        }

        private void ParseHexByte()
        {
            if (_pos + 1 >= _rtf.Length)
                return;
            var hex = _rtf.Substring(_pos, 2);
            _pos += 2;
            if (byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                _pendingBytes.Add(b);
        }

        /// <summary>Decodes accumulated <c>\'XX</c> bytes against the active code page and appends the text.</summary>
        private void FlushPendingBytes()
        {
            if (_pendingBytes.Count == 0)
                return;
            var decoded = _state.Encoding.GetString(_pendingBytes.ToArray());
            _pendingBytes.Clear();
            if (!_state.Ignore)
                _currentText.Append(decoded);
        }

        // ---- content accumulation -----------------------------------------------------------------------

        private void AppendChar(char c)
        {
            if (_state.Ignore)
                return;
            _currentText.Append(c);
        }

        /// <summary>
        /// Closes the current text span into a <see cref="Run"/> carrying the active run formatting, if any
        /// text has accumulated. Called before the run formatting changes or the paragraph ends.
        /// </summary>
        private void FlushRun()
        {
            FlushPendingBytes();
            if (_currentText.Length == 0)
                return;
            _currentRuns.Add(new Run(_currentText.ToString(), _state.Run));
            _currentText.Clear();
        }

        // ---- paragraphs ---------------------------------------------------------------------------------

        private void ParPlain()
        {
            if (_state.InTable)
            {
                // Inside a table cell, \par separates paragraphs within the cell rather than ending a block.
                EndCellParagraph();
                return;
            }
            EndParagraph(emitEmpty: true);
        }

        private void EndParagraph(bool emitEmpty)
        {
            FlushRun();
            if (_currentRuns.Count == 0 && !emitEmpty)
                return;

            // A body paragraph after a table's rows ends that table — commit it to the body first so block
            // order is preserved (table, then this paragraph).
            CommitTableIfComplete();

            var paragraph = new Paragraph { Formatting = _state.Paragraph };
            foreach (var run in _currentRuns)
                paragraph.Runs.Add(run);
            _currentRuns.Clear();
            _document.Blocks.Add(paragraph);
        }

        private Paragraph BuildCurrentParagraph()
        {
            FlushRun();
            var paragraph = new Paragraph { Formatting = _state.Paragraph };
            foreach (var run in _currentRuns)
                paragraph.Runs.Add(run);
            _currentRuns.Clear();
            return paragraph;
        }

        // ---- tables -------------------------------------------------------------------------------------

        private void BeginRow()
        {
            // Starting a new row definition. If a table is already open continue it, otherwise open one.
            _currentTable ??= new Table();
            _currentRow = new TableRow();
            _cellParagraphs.Clear();
            _currentCell = new TableCell();
            _cellxBoundaries.Clear();
        }

        private void EndCellParagraph()
        {
            // Finish the in-progress paragraph within the current cell.
            _cellParagraphs.Add(BuildCurrentParagraph());
        }

        private void EndCell()
        {
            _currentCell ??= new TableCell();
            _cellParagraphs.Add(BuildCurrentParagraph());
            foreach (var p in _cellParagraphs)
                _currentCell.Paragraphs.Add(p);
            _cellParagraphs.Clear();
            _currentRow?.Cells.Add(_currentCell);
            _currentCell = new TableCell();
        }

        private void EndRow()
        {
            if (_currentTable is null || _currentRow is null)
                return;

            // A cell may have content not yet closed by \cell (some writers omit the final \cell before \row).
            if (_cellParagraphs.Count > 0 || _currentText.Length > 0 || _currentRuns.Count > 0)
                EndCell();

            // Apply per-cell widths from the \cellxN boundary list. The first cell's width is boundary[0]
            // (from the row's left edge at 0); each subsequent cell's width is boundary[i] - boundary[i-1].
            // Width is in points: twips / 20 (20 twips per point, 1440 twips per inch = 72 pt/inch).
            if (_cellxBoundaries.Count == _currentRow.Cells.Count && _cellxBoundaries.Count > 0)
            {
                for (var i = 0; i < _currentRow.Cells.Count; i++)
                {
                    var prev = i == 0 ? 0 : _cellxBoundaries[i - 1];
                    _currentRow.Cells[i].WidthPt = (_cellxBoundaries[i] - prev) / 20.0;
                }
            }

            _currentTable.Rows.Add(_currentRow);
            _currentRow = null;
            _state.InTable = false;

            // The table is committed to the body lazily (when a non-row block follows or the document ends),
            // so consecutive \trowd…\row runs accrue into one Table.
        }

        /// <summary>Commits the open table to the body if it has at least one completed row.</summary>
        private void CommitTableIfComplete()
        {
            if (_currentTable is { Rows.Count: > 0 } table)
                _document.Blocks.Add(table);
            _currentTable = null;
            _currentRow = null;
            _currentCell = null;
            _cellParagraphs.Clear();
        }

        private void FinishTableIfOpen()
        {
            // Commit any open table whose rows are complete, plus any trailing in-progress row.
            if (_currentRow is { Cells.Count: > 0 } row && _currentTable is not null)
            {
                _currentTable.Rows.Add(row);
                _currentRow = null;
            }
            CommitTableIfComplete();
        }

        // ---- helpers ------------------------------------------------------------------------------------

        private static double TwipsToPt(int twips) => twips / 20.0;

        private static Encoding SafeEncoding(int codePage)
        {
            try
            {
                return Encoding.GetEncoding(codePage);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                return Encoding.GetEncoding(1252);
            }
        }
    }
}
