using System.Text;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Converts a bounded subset of external RTF into the renderer-neutral in-canvas clipboard
/// payload. The parser deliberately ignores unsupported destinations and controls while keeping
/// plain text, paragraph boundaries, and common character formatting usable.
/// </summary>
public static class ExternalRichTextClipboardPlanner
{
    public const int MaxRtfBytes = 8 * 1024 * 1024;
    public const int MaxOutputCharacters = 1_000_000;
    public const int MaxGroupDepth = 256;

    public static InCanvasRichClipboardPayload? TryParseRtf(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 } || bytes.Length > MaxRtfBytes)
            return null;

        try
        {
            return new RtfReader(bytes).Read();
        }
        catch
        {
            // Clipboard data is untrusted. A malformed provider must not interrupt paste.
            return null;
        }
    }

    private sealed class RtfReader
    {
        private enum Destination
        {
            Body,
            FontTable,
            ColorTable,
            Skip,
        }

        private sealed class State
        {
            public Destination Destination;
            public bool SkipOutput;
            public int FontIndex = -1;
            public int DefaultFontIndex = -1;
            public double? FontSizePt;
            public bool Bold;
            public bool BoldSet;
            public bool Italic;
            public bool ItalicSet;
            public bool Underline;
            public int ColorIndex;
            public int UnicodeSkip = 1;
            public int UnicodeFallbackRemaining;
            public int CodePage = 1252;
            public int FontTableIndex = -1;
            public string FontTableName = string.Empty;
            public int Red = -1;
            public int Green = -1;
            public int Blue = -1;

            public State Clone() => (State)MemberwiseClone();
        }

        private readonly record struct CharacterStyle(
            string? FontFamily,
            double? FontSizePt,
            bool Bold,
            bool Italic,
            bool Underline,
            SrgbColor? Color,
            bool BoldSet,
            bool ItalicSet);

        private readonly byte[] _bytes;
        private readonly Dictionary<int, string> _fonts = new();
        private readonly List<SrgbColor?> _colors = new();
        private readonly Stack<State> _states = new();
        private readonly TextBody _body = new();
        private State _state = new();
        private int _position;
        private int _depth;
        private int _processed;
        private int _outputCharacters;
        private bool _sawRtfHeader;
        private bool _lastWasParagraphBreak;
        private Paragraph? _activeParagraph;
        private CharacterStyle _activeStyle;
        private bool _hasActiveStyle;
        private readonly StringBuilder _activeText = new();

        public RtfReader(byte[] bytes) => _bytes = bytes;

        public InCanvasRichClipboardPayload? Read()
        {
            if (!LooksLikeRtf())
                return null;

            EnsureParagraph();
            while (_position < _bytes.Length && ReadNext())
            {
            }

            FlushActiveRun();

            if (!_sawRtfHeader)
                return null;

            // RTF documents normally end with the paragraph terminator for the final paragraph;
            // do not turn that structural terminator into an extra empty line.
            if (_lastWasParagraphBreak
                && _body.Paragraphs.Count > 1
                && IsEmpty(_body.Paragraphs[^1]))
            {
                _body.Paragraphs.RemoveAt(_body.Paragraphs.Count - 1);
            }

            EnsureParagraph();
            return new InCanvasRichClipboardPayload(
                _body,
                InCanvasTextEditPlanner.ExtractPlainText(_body));
        }

        private bool ReadNext()
        {
            if (++_processed > MaxRtfBytes * 2)
                return false;

            byte current = _bytes[_position];
            if (_state.UnicodeFallbackRemaining > 0)
            {
                if (current == (byte)'\\' && _position + 1 < _bytes.Length
                    && _bytes[_position + 1] == (byte)'\'')
                {
                    _position = Math.Min(_bytes.Length, _position + 4);
                }
                else if (current != (byte)'{' && current != (byte)'}')
                {
                    _position++;
                }
                _state.UnicodeFallbackRemaining--;
                return true;
            }

            switch (current)
            {
                case (byte)'{':
                    if (++_depth > MaxGroupDepth)
                        return false;
                    _states.Push(_state);
                    _state = _state.Clone();
                    _position++;
                    return true;

                case (byte)'}':
                    if (_states.Count == 0)
                    {
                        _position = _bytes.Length;
                        return true;
                    }
                    _state = _states.Pop();
                    _depth--;
                    _position++;
                    return true;

                case (byte)'\\':
                    ReadControl();
                    return true;

                case (byte)'\r':
                case (byte)'\n':
                    _position++;
                    return true;

                default:
                    _position++;
                    AppendByte(current);
                    return true;
            }
        }

        private void ReadControl()
        {
            _position++;
            if (_position >= _bytes.Length)
                return;

            byte marker = _bytes[_position];
            if (marker == (byte)'\'')
            {
                _position++;
                int high = HexValue(ReadByteOrZero());
                int low = HexValue(ReadByteOrZero());
                if (high >= 0 && low >= 0)
                    AppendByte((byte)((high << 4) | low));
                return;
            }

            if (marker == (byte)'*')
            {
                _state.SkipOutput = true;
                _position++;
                return;
            }

            if (!IsAsciiLetter(marker))
            {
                _position++;
                switch (marker)
                {
                    case (byte)'~': AppendText("\u00A0"); break;
                    case (byte)'-': break;
                    case (byte)'_': AppendText("\u2011"); break;
                    case (byte)'|': AppendText("\u200B"); break;
                    case (byte)'{': AppendText("{"); break;
                    case (byte)'}': AppendText("}"); break;
                }
                return;
            }

            int start = _position;
            while (_position < _bytes.Length && IsAsciiLetter(_bytes[_position]))
                _position++;
            string word = Encoding.ASCII.GetString(_bytes, start, _position - start);

            int? parameter = null;
            if (_position < _bytes.Length
                && (_bytes[_position] == (byte)'-' || IsAsciiDigit(_bytes[_position])))
            {
                int sign = 1;
                if (_bytes[_position] == (byte)'-')
                {
                    sign = -1;
                    _position++;
                }
                int value = 0;
                while (_position < _bytes.Length && IsAsciiDigit(_bytes[_position]))
                {
                    value = Math.Min(100_000_000, value * 10 + (_bytes[_position] - (byte)'0'));
                    _position++;
                }
                parameter = sign * value;
            }

            // A single space delimits a control word and is not document text.
            if (_position < _bytes.Length && _bytes[_position] == (byte)' ')
                _position++;

            ApplyControl(word, parameter);
        }

        private byte ReadByteOrZero() => _position < _bytes.Length ? _bytes[_position++] : (byte)0;

        private void ApplyControl(string word, int? parameter)
        {
            int value = parameter ?? 1;
            switch (word)
            {
                case "rtf": _sawRtfHeader = true; break;
                case "ansicpg": _state.CodePage = parameter is > 0 ? parameter.Value : 1252; break;
                case "deff": _state.DefaultFontIndex = Math.Max(0, value); break;
                case "fonttbl": _state.Destination = Destination.FontTable; _state.SkipOutput = true; break;
                case "colortbl": ResetColorTable(); _state.Destination = Destination.ColorTable; _state.SkipOutput = true; break;
                case "stylesheet":
                case "info":
                case "pict":
                case "object":
                case "filetbl":
                case "listtable":
                case "listoverridetable":
                case "generator":
                case "fldinst":
                case "listtext":
                case "pntext":
                    _state.Destination = Destination.Skip;
                    _state.SkipOutput = true;
                    break;

                case "f":
                    if (parameter is { } fontIndex)
                    {
                        _state.FontIndex = Math.Max(0, fontIndex);
                        if (_state.Destination == Destination.FontTable)
                        {
                            _state.FontTableIndex = _state.FontIndex;
                            _state.FontTableName = string.Empty;
                        }
                    }
                    break;
                case "fs":
                    if (parameter is { } halfPoints && halfPoints > 0 && halfPoints <= 32760)
                        _state.FontSizePt = halfPoints / 2.0;
                    break;
                case "b": _state.Bold = value != 0; _state.BoldSet = true; break;
                case "i": _state.Italic = value != 0; _state.ItalicSet = true; break;
                case "ul": _state.Underline = value != 0; break;
                case "ulnone":
                case "ul0": _state.Underline = false; break;
                case "cf": _state.ColorIndex = Math.Max(0, value); break;
                case "plain": ResetCharacterFormatting(); break;
                case "uc": _state.UnicodeSkip = Math.Clamp(value, 0, 16); break;
                case "u":
                    if (parameter is { } unicode)
                    {
                        short signed = unchecked((short)unicode);
                        AppendText(((char)signed).ToString());
                        _state.UnicodeFallbackRemaining = _state.UnicodeSkip;
                    }
                    break;
                case "par": AppendParagraphBreak(); break;
                case "line": AppendText("\n"); break;
                case "tab": AppendText("\t"); break;
                case "bin":
                    if (parameter is { } count && count > 0)
                        _position = Math.Min(_bytes.Length, _position + count);
                    break;
                case "red": _state.Red = Math.Clamp(value, 0, 255); break;
                case "green": _state.Green = Math.Clamp(value, 0, 255); break;
                case "blue": _state.Blue = Math.Clamp(value, 0, 255); break;
                case "emdash": AppendText("\u2014"); break;
                case "endash": AppendText("\u2013"); break;
                case "bullet": AppendText("\u2022"); break;
                case "lquote": AppendText("\u2018"); break;
                case "rquote": AppendText("\u2019"); break;
                case "ldblquote": AppendText("\u201C"); break;
                case "rdblquote": AppendText("\u201D"); break;
                case "pararsid":
                case "charrsid":
                case "lang":
                case "fcharset":
                case "fnil":
                case "froman":
                case "fswiss":
                case "fmodern":
                case "ftech":
                case "fdecor":
                case "fscript":
                case "viewkind":
                case "viewscale":
                case "pard":
                case "ql":
                case "qr":
                case "qc":
                case "qj":
                    break;
            }
        }

        private void AppendByte(byte value)
        {
            if (_state.Destination == Destination.FontTable)
            {
                if (value == (byte)';')
                {
                    if (_state.FontTableIndex >= 0)
                        _fonts[_state.FontTableIndex] = _state.FontTableName.Trim();
                    _state.FontTableIndex = -1;
                    _state.FontTableName = string.Empty;
                }
                else if (_state.FontTableIndex >= 0 && value != 0)
                    _state.FontTableName += DecodeByte(value);
                return;
            }

            if (_state.Destination == Destination.ColorTable)
            {
                if (value == (byte)';')
                    AddColorTableEntry();
                return;
            }

            if (_state.SkipOutput || _state.Destination != Destination.Body)
                return;

            AppendText(DecodeByte(value).ToString());
        }

        private void AppendText(string text)
        {
            if (text.Length == 0 || _state.SkipOutput || _state.Destination != Destination.Body)
                return;
            if (_outputCharacters > MaxOutputCharacters - text.Length)
                throw new InvalidDataException("RTF output limit exceeded.");

            var paragraph = _body.Paragraphs[^1];
            var style = CurrentStyle();
            if (!ReferenceEquals(_activeParagraph, paragraph)
                || !_hasActiveStyle
                || !SameStyle(_activeStyle, style))
            {
                FlushActiveRun();
                _activeParagraph = paragraph;
                _activeStyle = style;
                _hasActiveStyle = true;
            }
            _activeText.Append(text);
            _outputCharacters += text.Length;
            _lastWasParagraphBreak = false;
        }

        private void AppendParagraphBreak()
        {
            if (_state.SkipOutput || _state.Destination != Destination.Body)
                return;
            FlushActiveRun();
            EnsureParagraph();
            _body.Paragraphs.Add(new Paragraph());
            _lastWasParagraphBreak = true;
        }

        private CharacterStyle CurrentStyle()
        {
            string? fontFamily = null;
            int fontIndex = _state.FontIndex >= 0 ? _state.FontIndex : _state.DefaultFontIndex;
            if (fontIndex >= 0)
                _fonts.TryGetValue(fontIndex, out fontFamily);

            SrgbColor? color = null;
            if (_state.ColorIndex > 0 && _state.ColorIndex < _colors.Count)
                color = _colors[_state.ColorIndex];

            return new CharacterStyle(
                fontFamily,
                _state.FontSizePt,
                _state.Bold,
                _state.Italic,
                _state.Underline,
                color,
                _state.BoldSet,
                _state.ItalicSet);
        }

        private void FlushActiveRun()
        {
            if (_activeParagraph is null || !_hasActiveStyle || _activeText.Length == 0)
            {
                _activeParagraph = null;
                _hasActiveStyle = false;
                _activeText.Clear();
                return;
            }

            _activeParagraph.Runs.Add(new Run
            {
                Text = _activeText.ToString(),
                FontFamily = _activeStyle.FontFamily,
                FontSizePt = _activeStyle.FontSizePt,
                Bold = _activeStyle.Bold,
                BoldSet = _activeStyle.BoldSet,
                Italic = _activeStyle.Italic,
                ItalicSet = _activeStyle.ItalicSet,
                Underline = _activeStyle.Underline,
                Color = _activeStyle.Color is { } color ? new ThemeAwareColor(color) : null,
            });
            _activeParagraph = null;
            _hasActiveStyle = false;
            _activeText.Clear();
        }

        private static bool SameStyle(CharacterStyle left, CharacterStyle right) =>
            string.Equals(left.FontFamily, right.FontFamily, StringComparison.Ordinal)
            && Nullable.Equals(left.FontSizePt, right.FontSizePt)
            && left.Bold == right.Bold
            && left.Italic == right.Italic
            && left.Underline == right.Underline
            && Nullable.Equals(left.Color, right.Color)
            && left.BoldSet == right.BoldSet
            && left.ItalicSet == right.ItalicSet;

        private void ResetCharacterFormatting()
        {
            _state.FontIndex = -1;
            _state.FontSizePt = null;
            _state.Bold = false;
            _state.BoldSet = true;
            _state.Italic = false;
            _state.ItalicSet = true;
            _state.Underline = false;
            _state.ColorIndex = 0;
        }

        private void ResetColorTable()
        {
            _colors.Clear();
            _state.Red = -1;
            _state.Green = -1;
            _state.Blue = -1;
        }

        private void AddColorTableEntry()
        {
            _colors.Add(_state.Red >= 0 && _state.Green >= 0 && _state.Blue >= 0
                ? SrgbColor.FromRgb(
                    (_state.Red << 16) | (_state.Green << 8) | _state.Blue)
                : null);
            _state.Red = -1;
            _state.Green = -1;
            _state.Blue = -1;
        }

        private bool LooksLikeRtf()
        {
            int start = 0;
            if (_bytes.Length >= 3 && _bytes[0] == 0xEF && _bytes[1] == 0xBB && _bytes[2] == 0xBF)
                start = 3;
            return _bytes.Length - start >= 6
                && _bytes[start] == (byte)'{'
                && _bytes[start + 1] == (byte)'\\'
                && _bytes[start + 2] == (byte)'r'
                && _bytes[start + 3] == (byte)'t'
                && _bytes[start + 4] == (byte)'f';
        }

        private string DecodeByte(byte value)
        {
            if (value < 0x80 || value >= 0xA0)
                return ((char)value).ToString();

            return _state.CodePage == 1252
                ? value switch
                {
                    0x80 => "\u20AC",
                    0x82 => "\u201A",
                    0x83 => "\u0192",
                    0x84 => "\u201E",
                    0x85 => "\u2026",
                    0x86 => "\u2020",
                    0x87 => "\u2021",
                    0x88 => "\u02C6",
                    0x89 => "\u2030",
                    0x8A => "\u0160",
                    0x8B => "\u2039",
                    0x8C => "\u0152",
                    0x8E => "\u017D",
                    0x91 => "\u2018",
                    0x92 => "\u2019",
                    0x93 => "\u201C",
                    0x94 => "\u201D",
                    0x95 => "\u2022",
                    0x96 => "\u2013",
                    0x97 => "\u2014",
                    0x98 => "\u02DC",
                    0x99 => "\u2122",
                    0x9A => "\u0161",
                    0x9B => "\u203A",
                    0x9C => "\u0153",
                    0x9E => "\u017E",
                    0x9F => "\u0178",
                    _ => ((char)value).ToString(),
                }
                : ((char)value).ToString();
        }

        private void EnsureParagraph()
        {
            if (_body.Paragraphs.Count == 0)
                _body.Paragraphs.Add(new Paragraph());
        }

        private static bool IsEmpty(Paragraph paragraph) =>
            paragraph.Runs.All(run => string.IsNullOrEmpty(run.Text));

        private static int HexValue(byte value) => value switch
        {
            >= (byte)'0' and <= (byte)'9' => value - (byte)'0',
            >= (byte)'a' and <= (byte)'f' => value - (byte)'a' + 10,
            >= (byte)'A' and <= (byte)'F' => value - (byte)'A' + 10,
            _ => -1,
        };

        private static bool IsAsciiLetter(byte value) =>
            value is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z';

        private static bool IsAsciiDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';
    }
}
