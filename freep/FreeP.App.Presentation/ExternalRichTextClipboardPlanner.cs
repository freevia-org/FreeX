using System.Text;
using Free.Shared.AppServices;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Converts a bounded subset of external RTF into the renderer-neutral in-canvas clipboard
/// payload. The parser deliberately ignores unsupported destinations and controls while keeping
/// plain text, paragraph boundaries, common character formatting, bounded list metadata,
/// paragraph layout, external hyperlink fields, and bounded embedded-object payloads usable.
/// </summary>
public static class ExternalRichTextClipboardPlanner
{
    public const int MaxRtfBytes = 8 * 1024 * 1024;
    public const int MaxOutputCharacters = 1_000_000;
    public const int MaxGroupDepth = 256;
    public const int MaxTableCellsPerRow = 4096;

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

    /// <summary>
    /// Projects the renderer-neutral rich fragment to the standard RTF clipboard format.
    /// Unsupported FreeP-only payloads (for example inline tables, OLE, and XamlPackage
    /// resources) remain available through the private clipboard format; this method emits
    /// only the text and run/paragraph semantics that RTF can represent truthfully.
    /// </summary>
    public static byte[] SerializeRtf(InCanvasRichClipboardPayload payload) =>
        ExternalRichTextClipboardRtfWriter.Serialize(payload);

    private sealed class RtfReader
    {
        private enum Destination
        {
            Body,
            FontTable,
            ColorTable,
            ListTable,
            ListOverrideTable,
            ListLevelText,
            ListLevelNumbers,
            FieldInstruction,
            Object,
            ObjectClass,
            ObjectName,
            ObjectData,
            Picture,
            Skip,
        }

        private sealed class ListDefinition
        {
            public int Id { get; set; }
            public Dictionary<int, ListLevelDefinition> Levels { get; } = new();
        }

        private sealed class ListLevelDefinition
        {
            public int NumberFormat { get; set; }
            public int StartAt { get; set; } = 1;
            public bool NumberFormatSpecified { get; set; }
            public bool StartAtSpecified { get; set; }
            public int? LeftIndentTwips { get; set; }
            public int? FirstLineIndentTwips { get; set; }
            public string? BulletChar { get; set; }
            public string LevelTextTemplate { get; set; } = string.Empty;
        }

        private sealed class ListOverrideDefinition
        {
            public int ListId { get; set; }
            public Dictionary<int, int> StartAtByLevel { get; } = new();
            public Dictionary<int, ListLevelDefinition> FormattingByLevel { get; } = new();
        }

        private sealed class FieldContext
        {
            public StringBuilder Instruction { get; } = new();
        }

        private sealed class LegacyListDefinition
        {
            public BulletKind Kind { get; set; } = BulletKind.Auto;
            public string? BulletChar { get; set; }
            public int Level { get; set; }
            public int StartAt { get; set; } = 1;
            public bool StartSpecified { get; set; }
        }

        private sealed class CellStyleDraft
        {
            private TableCellBorderSide? _borderSide;
            private bool _borderDefined;
            private bool _borderNone;
            private int _borderColorRgb;
            private double _borderWidthPt = 0.75;

            public int? FillRgb { get; set; }
            public string? FillPattern { get; set; }
            public int? FillForegroundRgb { get; set; }
            public int? FillBackgroundRgb { get; set; }
            public InCanvasRichClipboardTableBorder? Left { get; private set; }
            public InCanvasRichClipboardTableBorder? Right { get; private set; }
            public InCanvasRichClipboardTableBorder? Top { get; private set; }
            public InCanvasRichClipboardTableBorder? Bottom { get; private set; }
            public TableCellAnchor? Anchor { get; set; }
            public TextVerticalType? TextVerticalType { get; set; }
            public double? InsetLeftPt { get; set; }
            public double? InsetRightPt { get; set; }
            public double? InsetTopPt { get; set; }
            public double? InsetBottomPt { get; set; }
            public bool HorizontalMergeStart { get; set; }
            public bool HorizontalMergeContinuation { get; set; }
            public bool VerticalMergeStart { get; set; }
            public bool VerticalMergeContinuation { get; set; }

            public void BeginBorder(TableCellBorderSide side)
            {
                CommitBorder();
                _borderSide = side;
                _borderDefined = false;
                _borderNone = false;
                _borderColorRgb = 0;
                _borderWidthPt = 0.75;
            }

            public void SetSolid() => _borderDefined = true;

            public void SetNone()
            {
                _borderDefined = true;
                _borderNone = true;
            }

            public void SetColor(int rgb) => _borderColorRgb = rgb;

            public void SetWidth(double widthPt) => _borderWidthPt = widthPt;

            public InCanvasRichClipboardTableCellStyle Snapshot()
            {
                CommitBorder();
                return new(
                    FillRgb,
                    Left,
                    Right,
                    Top,
                    Bottom,
                    Anchor,
                    InsetLeftPt,
                    InsetRightPt,
                    InsetTopPt,
                    InsetBottomPt,
                    HorizontalMergeStart,
                    HorizontalMergeContinuation,
                    VerticalMergeStart,
                    VerticalMergeContinuation,
                    FillPattern,
                    FillForegroundRgb,
                    FillBackgroundRgb ?? FillRgb,
                    TextVerticalType);
            }

            public void Reset()
            {
                FillRgb = null;
                FillPattern = null;
                FillForegroundRgb = null;
                FillBackgroundRgb = null;
                Left = null;
                Right = null;
                Top = null;
                Bottom = null;
                _borderSide = null;
                _borderDefined = false;
                Anchor = null;
                TextVerticalType = null;
                InsetLeftPt = null;
                InsetRightPt = null;
                InsetTopPt = null;
                InsetBottomPt = null;
                HorizontalMergeStart = false;
                HorizontalMergeContinuation = false;
                VerticalMergeStart = false;
                VerticalMergeContinuation = false;
            }

            private void CommitBorder()
            {
                if (_borderSide is not { } side || !_borderDefined)
                    return;

                var border = new InCanvasRichClipboardTableBorder(
                    _borderColorRgb,
                    _borderWidthPt,
                    _borderNone);
                switch (side)
                {
                    case TableCellBorderSide.Left: Left = border; break;
                    case TableCellBorderSide.Right: Right = border; break;
                    case TableCellBorderSide.Top: Top = border; break;
                    case TableCellBorderSide.Bottom: Bottom = border; break;
                }

                _borderSide = null;
                _borderDefined = false;
            }
        }

        private sealed class TableCaptureContext
        {
            public required int Level { get; init; }
            public TableShape Table { get; } = new();
            public TableRow? CurrentRow { get; set; }
            public CellStyleDraft RowBorders { get; } = new();
            public CellStyleDraft RowInsideVerticalBorders { get; } = new();
            public double? RowInsetLeftPt { get; set; }
            public double? RowInsetRightPt { get; set; }
            public double? RowInsetTopPt { get; set; }
            public double? RowInsetBottomPt { get; set; }
            public int CurrentCellIndex { get; set; }
            public List<long> RightEdgesTwips { get; } = new();
            public List<InCanvasRichClipboardTableCellStyle> CellStyles { get; } = new();
            public StringBuilder ActiveText { get; } = new();
            public CharacterStyle ActiveStyle { get; set; }
            public bool HasActiveStyle { get; set; }
            public Run? MarkerRun { get; set; }
            public Run? EndMarkerRun { get; set; }
            public bool RowCompleted { get; set; }
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
            public bool Strikethrough;
            public bool TextOutline;
            public bool TextShadow;
            public int? BaselineOffset;
            public RunTextCaps Caps;
            public bool? RunRightToLeft;
            public int ColorIndex;
            public int TextFillColorIndex;
            public int UnicodeSkip = 1;
            public int UnicodeFallbackRemaining;
            public int CodePage = 1252;
            public int FontTableIndex = -1;
            public string FontTableName = string.Empty;
            public int Red = -1;
            public int Green = -1;
            public int Blue = -1;
            public TextAlign? ParagraphAlignment;
            public bool? ParagraphRightToLeft;
            public int? ListOverrideId;
            public int ListLevel;
            public int? LeftIndentTwips;
            public int? FirstLineIndentTwips;
            public int? SpaceBeforeTwips;
            public int? SpaceAfterTwips;
            public List<TabStop> TabStops { get; set; } = new();
            public TabStopAlignment PendingTabStopAlignment { get; set; }
            public TabStopLeader PendingTabStopLeader { get; set; }
            public FieldContext? Field;
            public Hyperlink? Hyperlink;
            public bool InTable;
            public int TableNesting;
            public int ListLevelTextPrefixBytesRemaining;

            public State Clone()
            {
                var clone = (State)MemberwiseClone();
                clone.TabStops = TabStops
                    .Select(stop => new TabStop
                    {
                        PositionEmu = stop.PositionEmu,
                        Alignment = stop.Alignment,
                        Leader = stop.Leader,
                    })
                    .ToList();
                return clone;
            }
        }

        private readonly record struct CharacterStyle(
            string? FontFamily,
            double? FontSizePt,
            bool Bold,
            bool Italic,
            bool Underline,
            bool Strikethrough,
            bool TextOutline,
            bool TextShadow,
            int? BaselineOffset,
            RunTextCaps Caps,
            bool? RunRightToLeft,
            SrgbColor? Color,
            SrgbColor? TextFillColor,
            bool BoldSet,
            bool ItalicSet,
            Hyperlink? Hyperlink,
            string? FieldType);

        private readonly byte[] _bytes;
        private readonly Dictionary<int, string> _fonts = new();
        private readonly List<SrgbColor?> _colors = new();
        private readonly Dictionary<int, ListDefinition> _lists = new();
        private readonly Dictionary<int, ListOverrideDefinition> _listOverrides = new();
        private readonly Stack<State> _states = new();
        private readonly TextBody _body = new();
        private State _state = new();
        private int _position;
        private int _depth;
        private int _processed;
        private int _outputCharacters;
        private bool _tableRowActive;
        private int _tableCellCount;
        private bool _containsTable;
        private readonly List<long> _tableCellRightEdgesTwips = new();
        private IReadOnlyList<long>? _tableColumnWidthsEmu;
        private readonly List<InCanvasRichClipboardTableCellStyle> _tableCellStyles = new();
        private readonly CellStyleDraft _pendingCellStyle = new();
        private readonly List<TableCaptureContext> _tableCaptures = new();
        private readonly List<TableCaptureContext> _rootTableCaptures = new();
        private bool _rowBorderPending;
        private CellStyleDraft? _activeRowBorderStyle;
        private bool _nestedTableSeen;
        private bool _suppressCaptureForNextAppend;
        private bool _suppressCaptureForNextParagraphBreak;
        private bool _sawRtfHeader;
        private bool _lastWasParagraphBreak;
        private Paragraph? _activeParagraph;
        private CharacterStyle _activeStyle;
        private bool _hasActiveStyle;
        private readonly StringBuilder _activeText = new();
        private ListDefinition? _currentList;
        private ListLevelDefinition? _currentListLevel;
        private int _currentListLevelIndex = -1;
        private ListOverrideDefinition? _currentListOverride;
        private int _currentListOverrideId;
        private int _currentListOverrideLevel = -1;
        private bool _currentListOverrideStartsAt;
        private ListLevelDefinition? _currentListOverrideLevelDefinition;
        private LegacyListDefinition? _legacyList;
        private readonly HashSet<(int ListId, int Level)> _seenListLevels = new();
        private readonly List<byte> _pictureBytes = new();
        private readonly List<InCanvasRichClipboardImage> _picturePayloads = new();
        private int _picturePendingNibble = -1;
        private bool _pictureCaptureStarted;
        private int? _pictureWidthGoalTwips;
        private int? _pictureHeightGoalTwips;
        private double _pictureScaleX = 100;
        private double _pictureScaleY = 100;
        private readonly StringBuilder _objectClass = new();
        private readonly StringBuilder _objectName = new();
        private readonly List<byte> _objectBytes = new();
        private readonly List<InCanvasRichClipboardObject> _objectPayloads = new();
        private int _objectPendingNibble = -1;
        private bool _objectCaptureStarted;

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
            CloseAllTableCaptures();

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
            FinalizePictureCapture();
            FinalizeObjectCapture();
            RewriteNestedTableMarkers();

            if (_lastWasParagraphBreak
                && _body.Paragraphs.Count > 1
                && IsEmpty(_body.Paragraphs[^1]))
            {
                _body.Paragraphs.RemoveAt(_body.Paragraphs.Count - 1);
            }
            var firstPicture = _picturePayloads.FirstOrDefault();
            return new InCanvasRichClipboardPayload(
                _body,
                InCanvasTextEditPlanner.ExtractPlainText(_body),
                ImageBytes: firstPicture?.Bytes,
                ImageContentType: firstPicture?.ContentType,
                ContainsTable: _containsTable,
                TableColumnWidthsEmu: _tableColumnWidthsEmu,
                TableCellStyles: _tableCellStyles.Count == 0 ? null : _tableCellStyles,
                ImagePayloads: _picturePayloads.Count == 0 ? null : _picturePayloads.ToArray(),
                ObjectPayloads: _objectPayloads.Count == 0 ? null : _objectPayloads.ToArray());
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
                    if (_state.Destination == Destination.ObjectData)
                        FinalizeObjectCapture();
                    if (_state.Destination == Destination.Picture)
                        FinalizePictureCapture();
                    State restoredState = _states.Pop();
                    if (_hasActiveStyle && !SameStyle(_activeStyle, CurrentStyle(restoredState)))
                        FlushActiveRun();
                    _state = restoredState;
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
                case "listtable": _state.Destination = Destination.ListTable; _state.SkipOutput = true; break;
                case "listoverridetable": _state.Destination = Destination.ListOverrideTable; _state.SkipOutput = true; break;
                case "list":
                    if (_state.Destination == Destination.ListTable)
                    {
                        _currentList = new ListDefinition();
                        _currentListLevel = null;
                        _currentListLevelIndex = -1;
                    }
                    break;
                case "listoverride":
                    if (_state.Destination == Destination.ListOverrideTable)
                    {
                        _currentListOverride = new ListOverrideDefinition();
                        _currentListOverrideId = 0;
                        _currentListOverrideLevel = -1;
                        _currentListOverrideStartsAt = false;
                        _currentListOverrideLevelDefinition = null;
                    }
                    break;
                case "lfolevel":
                    if (_state.Destination == Destination.ListOverrideTable && _currentListOverride is not null)
                    {
                        _currentListOverrideLevel = Math.Clamp(_currentListOverrideLevel + 1, 0, 8);
                        _currentListOverrideStartsAt = false;
                        _currentListOverrideLevelDefinition = null;
                    }
                    break;
                case "listlevel":
                    if (_state.Destination == Destination.ListTable && _currentList is not null)
                    {
                        _currentListLevelIndex = Math.Clamp(_currentListLevelIndex + 1, 0, 8);
                        _currentListLevel = new ListLevelDefinition();
                        _currentList.Levels[_currentListLevelIndex] = _currentListLevel;
                    }
                    else if (_state.Destination == Destination.ListOverrideTable
                             && _currentListOverride is not null
                             && _currentListOverrideLevel >= 0)
                    {
                        _currentListOverrideLevelDefinition = new ListLevelDefinition();
                        _currentListOverride.FormattingByLevel[_currentListOverrideLevel] =
                            _currentListOverrideLevelDefinition;
                    }
                    break;
                case "listid":
                    if (_state.Destination == Destination.ListTable && _currentList is not null && parameter is { } listId)
                    {
                        _currentList.Id = Math.Max(0, listId);
                        if (_currentList.Id > 0)
                            _lists[_currentList.Id] = _currentList;
                    }
                    else if (_state.Destination == Destination.ListOverrideTable
                             && _currentListOverride is not null
                             && parameter is { } overrideListId)
                    {
                        _currentListOverride.ListId = Math.Max(0, overrideListId);
                    }
                    break;
                case "ls":
                    if (_state.Destination == Destination.ListOverrideTable
                        && _currentListOverride is not null
                        && parameter is { } overrideId)
                    {
                        _currentListOverrideId = Math.Max(0, overrideId);
                        if (_currentListOverrideId > 0)
                            _listOverrides[_currentListOverrideId] = _currentListOverride;
                    }
                    else if (_state.Destination == Destination.Body)
                    {
                        _state.ListOverrideId = parameter is > 0 ? parameter : null;
                    }
                    break;
                case "ilvl":
                    _state.ListLevel = Math.Clamp(value, 0, 8);
                    break;
                case "levelnfc":
                    if (_state.Destination == Destination.ListTable && _currentListLevel is not null)
                    {
                        _currentListLevel.NumberFormat = Math.Clamp(value, 0, 255);
                        _currentListLevel.NumberFormatSpecified = true;
                    }
                    else if (_state.Destination == Destination.ListOverrideTable
                             && _currentListOverrideLevelDefinition is not null)
                    {
                        _currentListOverrideLevelDefinition.NumberFormat = Math.Clamp(value, 0, 255);
                        _currentListOverrideLevelDefinition.NumberFormatSpecified = true;
                    }
                    break;
                case "levelstartat":
                    if (_state.Destination == Destination.ListTable && _currentListLevel is not null)
                    {
                        _currentListLevel.StartAt = Math.Clamp(value, 1, 1_000_000);
                        _currentListLevel.StartAtSpecified = true;
                    }
                    else if (_state.Destination == Destination.ListOverrideTable
                             && _currentListOverride is not null
                             && _currentListOverrideLevel >= 0)
                    {
                        var startAt = Math.Clamp(value, 1, 1_000_000);
                        if (_currentListOverrideStartsAt)
                            _currentListOverride.StartAtByLevel[_currentListOverrideLevel] = startAt;
                        if (_currentListOverrideLevelDefinition is not null)
                        {
                            _currentListOverrideLevelDefinition.StartAt = startAt;
                            _currentListOverrideLevelDefinition.StartAtSpecified = true;
                        }
                    }
                    break;
                case "listoverridestart":
                case "listoverridestartat":
                    if (_state.Destination == Destination.ListOverrideTable && _currentListOverride is not null)
                        _currentListOverrideStartsAt = value != 0;
                    break;
                case "leveltext":
                    _state.Destination = Destination.ListLevelText;
                    _state.SkipOutput = true;
                    // The first hex byte is the RTF level-text length marker.
                    _state.ListLevelTextPrefixBytesRemaining = 1;
                    break;
                case "levelnumbers":
                    _state.Destination = Destination.ListLevelNumbers;
                    _state.SkipOutput = true;
                    // The first hex byte is the number of level-number bytes.
                    _state.ListLevelTextPrefixBytesRemaining = 1;
                    break;
                case "field":
                    _state.Field = new FieldContext();
                    break;
                case "fldinst":
                    _state.Field ??= new FieldContext();
                    _state.Destination = Destination.FieldInstruction;
                    _state.SkipOutput = true;
                    break;
                case "fldrslt":
                    _state.Destination = Destination.Body;
                    _state.SkipOutput = false;
                    _state.Hyperlink = TryReadExternalHyperlink(_state.Field?.Instruction.ToString());
                    break;
                case "trowd":
                case "nesttableprops":
                    BeginTableRow();
                    break;
                case "intbl":
                    _state.InTable = value != 0;
                    if (!_state.InTable)
                        CloseAllTableCaptures();
                    break;
                case "itap":
                    _state.TableNesting = Math.Clamp(value, 0, 8);
                    break;
                case "cell":
                case "nestcell":
                    AppendTableCellBoundary();
                    break;
                case "row":
                case "nestrow":
                    AppendTableRowBoundary();
                    break;
                case "stylesheet":
                case "info":
                case "filetbl":
                case "generator":
                case "listtext":
                case "pntext":
                    _state.Destination = Destination.Skip;
                    _state.SkipOutput = true;
                    break;
                case "object":
                    FinalizeObjectCapture();
                    _objectClass.Clear();
                    _objectName.Clear();
                    _objectBytes.Clear();
                    _objectPendingNibble = -1;
                    _state.Destination = Destination.Object;
                    _state.SkipOutput = true;
                    break;
                case "objclass":
                    _state.Destination = Destination.ObjectClass;
                    _state.SkipOutput = true;
                    break;
                case "objname":
                    _state.Destination = Destination.ObjectName;
                    _state.SkipOutput = true;
                    break;
                case "objdata":
                    _objectCaptureStarted = true;
                    _objectPendingNibble = -1;
                    _state.Destination = Destination.ObjectData;
                    _state.SkipOutput = true;
                    break;
                case "result":
                case "objresult":
                    // Keep the visible fallback result while separately retaining objdata.
                    if (_state.Destination == Destination.Object)
                    {
                        _state.Destination = Destination.Body;
                        _state.SkipOutput = false;
                    }
                    break;
                case "pict":
                    FinalizePictureCapture();
                    _pictureCaptureStarted = true;
                    _picturePendingNibble = -1;
                    _pictureWidthGoalTwips = null;
                    _pictureHeightGoalTwips = null;
                    _pictureScaleX = 100;
                    _pictureScaleY = 100;
                    _state.Destination = Destination.Picture;
                    _state.SkipOutput = true;
                    break;
                case "picwgoal":
                    if (_state.Destination == Destination.Picture && parameter is { } widthGoal)
                        _pictureWidthGoalTwips = Math.Clamp(widthGoal, 1, 100_000_000);
                    break;
                case "pichgoal":
                    if (_state.Destination == Destination.Picture && parameter is { } heightGoal)
                        _pictureHeightGoalTwips = Math.Clamp(heightGoal, 1, 100_000_000);
                    break;
                case "picscalex":
                    if (_state.Destination == Destination.Picture && parameter is { } scaleX)
                        _pictureScaleX = Math.Clamp(scaleX, 1, 1_000);
                    break;
                case "picscaley":
                    if (_state.Destination == Destination.Picture && parameter is { } scaleY)
                        _pictureScaleY = Math.Clamp(scaleY, 1, 1_000);
                    break;
                case "pngblip":
                    break;
                case "jpegblip":
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
                // The shared run model keeps the underline semantic while RTF's
                // stroke-specific variants are normalized at the clipboard boundary.
                case "uldb":
                case "uld":
                case "ulw":
                case "uldash":
                case "uldashd":
                case "uldashdd":
                case "uldashdot":
                case "ulth":
                case "ulthd":
                case "ulthdash":
                case "ulthdashd":
                case "ulthdashdd":
                case "ulthdashdot":
                case "ulthldash":
                case "ulwave":
                case "ulhwave":
                case "ululdbwave":
                    _state.Underline = true;
                    break;
                case "ulnone":
                case "ul0": _state.Underline = false; break;
                case "outl": _state.TextOutline = value != 0; break;
                case "outl0": _state.TextOutline = false; break;
                case "shad": _state.TextShadow = value != 0; break;
                case "shad0": _state.TextShadow = false; break;
                case "strike":
                case "striked": _state.Strikethrough = value != 0; break;
                case "strike0": _state.Strikethrough = false; break;
                case "super": _state.BaselineOffset = RtfBaselineOffset(parameter ?? 6); break;
                case "sub": _state.BaselineOffset = -RtfBaselineOffset(parameter ?? 6); break;
                case "up":
                    if (parameter is { } up)
                        _state.BaselineOffset = RtfBaselineOffset(up);
                    break;
                case "dn":
                    if (parameter is { } down)
                        _state.BaselineOffset = -RtfBaselineOffset(down);
                    break;
                case "nosupersub": _state.BaselineOffset = null; break;
                case "caps": _state.Caps = value != 0 ? RunTextCaps.All : RunTextCaps.None; break;
                case "scaps": _state.Caps = value != 0 ? RunTextCaps.Small : RunTextCaps.None; break;
                case "rtlch": _state.RunRightToLeft = true; break;
                case "ltrch": _state.RunRightToLeft = false; break;
                case "cf": _state.ColorIndex = Math.Max(0, value); break;
                case "highlight":
                case "chcbpat": _state.TextFillColorIndex = Math.Max(0, value); break;
                case "plain": ResetCharacterFormatting(); break;
                case "pard": ResetParagraphFormatting(); break;
                case "uc": _state.UnicodeSkip = Math.Clamp(value, 0, 16); break;
                case "u":
                    if (parameter is { } unicode)
                    {
                        short signed = unchecked((short)unicode);
                        string text = ((char)signed).ToString();
                        if (_state.Destination == Destination.ListLevelText)
                            CaptureListLevelTextChar(text);
                        else
                            AppendText(text);
                        _state.UnicodeFallbackRemaining = _state.UnicodeSkip;
                    }
                    break;
                case "par": AppendParagraphBreak(); break;
                case "line": AppendText("\n"); break;
                case "tab": AppendText("\t"); break;
                case "bin":
                    if (parameter is { } count && count > 0)
                    {
                        if (_state.Destination == Destination.Picture)
                            ReadPictureBinary(count);
                        else if (_state.Destination == Destination.ObjectData)
                            ReadObjectBinary(count);
                        else
                            _position = Math.Min(_bytes.Length, _position + count);
                    }
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
                case "ql": _state.ParagraphAlignment = TextAlign.Left; break;
                case "qr": _state.ParagraphAlignment = TextAlign.Right; break;
                case "qc": _state.ParagraphAlignment = TextAlign.Center; break;
                case "qj": _state.ParagraphAlignment = TextAlign.Justify; break;
                case "rtlpar": _state.ParagraphRightToLeft = true; break;
                case "ltrpar": _state.ParagraphRightToLeft = false; break;
                case "li":
                    var leftIndent = parameter is { } left ? Math.Clamp(left, -100_000, 100_000) : 0;
                    if (_state.Destination == Destination.ListTable && _currentListLevel is not null)
                        _currentListLevel.LeftIndentTwips = leftIndent;
                    else if (_state.Destination == Destination.ListOverrideTable && _currentListOverrideLevelDefinition is not null)
                        _currentListOverrideLevelDefinition.LeftIndentTwips = leftIndent;
                    else
                        _state.LeftIndentTwips = leftIndent;
                    break;
                case "fi":
                    var firstIndent = parameter is { } first ? Math.Clamp(first, -100_000, 100_000) : 0;
                    if (_state.Destination == Destination.ListTable && _currentListLevel is not null)
                        _currentListLevel.FirstLineIndentTwips = firstIndent;
                    else if (_state.Destination == Destination.ListOverrideTable && _currentListOverrideLevelDefinition is not null)
                        _currentListOverrideLevelDefinition.FirstLineIndentTwips = firstIndent;
                    else
                        _state.FirstLineIndentTwips = firstIndent;
                    break;
                case "sb": _state.SpaceBeforeTwips = parameter is { } before ? Math.Clamp(before, -100_000, 100_000) : 0; break;
                case "sa": _state.SpaceAfterTwips = parameter is { } after ? Math.Clamp(after, -100_000, 100_000) : 0; break;
                case "tql":
                    if (_state.Destination == Destination.Body)
                        _state.PendingTabStopAlignment = TabStopAlignment.Left;
                    break;
                case "tqc":
                    if (_state.Destination == Destination.Body)
                        _state.PendingTabStopAlignment = TabStopAlignment.Center;
                    break;
                case "tqr":
                    if (_state.Destination == Destination.Body)
                        _state.PendingTabStopAlignment = TabStopAlignment.Right;
                    break;
                case "tqdec":
                    if (_state.Destination == Destination.Body)
                        _state.PendingTabStopAlignment = TabStopAlignment.Decimal;
                    break;
                case "tlnone":
                    if (_state.Destination == Destination.Body)
                        _state.PendingTabStopLeader = TabStopLeader.None;
                    break;
                case "tldot":
                    if (_state.Destination == Destination.Body)
                        _state.PendingTabStopLeader = TabStopLeader.Dots;
                    break;
                case "tlhyph":
                    if (_state.Destination == Destination.Body)
                        _state.PendingTabStopLeader = TabStopLeader.Hyphens;
                    break;
                case "tlul":
                    if (_state.Destination == Destination.Body)
                        _state.PendingTabStopLeader = TabStopLeader.Underscore;
                    break;
                case "tlth":
                    if (_state.Destination == Destination.Body)
                        _state.PendingTabStopLeader = TabStopLeader.ThickLine;
                    break;
                case "tleq":
                    if (_state.Destination == Destination.Body)
                        _state.PendingTabStopLeader = TabStopLeader.Equal;
                    break;
                case "tx":
                    if (_state.Destination == Destination.Body
                        && parameter is > 0 and <= 100_000_000)
                    {
                        long positionEmu = parameter.Value * 635L;
                        var stop = new TabStop
                        {
                            PositionEmu = positionEmu,
                            Alignment = _state.PendingTabStopAlignment,
                            Leader = _state.PendingTabStopLeader,
                        };
                        int existing = _state.TabStops.FindIndex(
                            candidate => candidate.PositionEmu == positionEmu);
                        if (existing >= 0)
                            _state.TabStops[existing] = stop;
                        else
                            _state.TabStops.Add(stop);
                        _state.TabStops.Sort(
                            (left, right) => left.PositionEmu.CompareTo(right.PositionEmu));
                        _state.PendingTabStopAlignment = TabStopAlignment.Left;
                        _state.PendingTabStopLeader = TabStopLeader.None;
                    }
                    break;
                case "pn":
                    _legacyList = new LegacyListDefinition();
                    break;
                case "pnlvlblt":
                    _legacyList ??= new LegacyListDefinition();
                    _legacyList.Kind = BulletKind.Char;
                    _legacyList.BulletChar = "\u2022";
                    break;
                case "pnlvlbody":
                    _legacyList ??= new LegacyListDefinition();
                    _legacyList.Kind = BulletKind.Auto;
                    break;
                case "pnlvlcont":
                    _legacyList ??= new LegacyListDefinition();
                    _legacyList.StartSpecified = false;
                    break;
                case "pnlvlrestart":
                case "pnrestart":
                    _legacyList ??= new LegacyListDefinition();
                    _legacyList.StartSpecified = true;
                    break;
                case "pnstart":
                    _legacyList ??= new LegacyListDefinition();
                    _legacyList.StartAt = Math.Clamp(value, 1, 1_000_000);
                    _legacyList.StartSpecified = true;
                    break;
                case "pnseclvl":
                    _legacyList ??= new LegacyListDefinition();
                    _legacyList.Level = Math.Clamp(value - 1, 0, 8);
                    break;
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
                case "ri":
                case "sl":
                case "slmult":
                case "pnf":
                case "pnfs":
                case "cellx":
                    if (_tableRowActive)
                    {
                        CaptureTableCellStyle();
                        if (parameter is > 0
                            && _tableCellRightEdgesTwips.Count < MaxTableCellsPerRow)
                        {
                            _tableCellRightEdgesTwips.Add(parameter.Value);
                        }
                        if (parameter is > 0
                            && CurrentTableCapture() is { } capture
                            && capture.RightEdgesTwips.Count < MaxTableCellsPerRow)
                        {
                            capture.RightEdgesTwips.Add(parameter.Value);
                        }
                    }
                    break;
                case "clcbpat":
                    _pendingCellStyle.FillRgb = ResolveColorRgb(value);
                    _pendingCellStyle.FillBackgroundRgb = _pendingCellStyle.FillRgb;
                    break;
                case "clcfpat":
                    _pendingCellStyle.FillForegroundRgb = ResolveColorRgb(value);
                    break;
                case "clshdng":
                    _pendingCellStyle.FillPattern = PatternForPercentage(value);
                    break;
                case "clbghoriz": _pendingCellStyle.FillPattern = "horzStripe"; break;
                case "clbgvert": _pendingCellStyle.FillPattern = "vertStripe"; break;
                case "clbgfdiag": _pendingCellStyle.FillPattern = "upDiag"; break;
                case "clbgbdiag": _pendingCellStyle.FillPattern = "diagStripe"; break;
                case "clbgcross": _pendingCellStyle.FillPattern = "cross"; break;
                case "clbgdcross": _pendingCellStyle.FillPattern = "diagCross"; break;
                case "clbgdkhoriz": _pendingCellStyle.FillPattern = "ltHorz"; break;
                case "clbgdkvert": _pendingCellStyle.FillPattern = "ltVert"; break;
                case "clbgdkfdiag": _pendingCellStyle.FillPattern = "ltUpDiag"; break;
                case "clbgdkbdiag": _pendingCellStyle.FillPattern = "ltDnDiag"; break;
                case "clbgdkcross": _pendingCellStyle.FillPattern = "smGrid"; break;
                case "clbgdkdcross": _pendingCellStyle.FillPattern = "diagCross"; break;
                case "clbrdrl": BeginCellBorder(TableCellBorderSide.Left); break;
                case "clbrdrr": BeginCellBorder(TableCellBorderSide.Right); break;
                case "clbrdrt": BeginCellBorder(TableCellBorderSide.Top); break;
                case "clbrdrb": BeginCellBorder(TableCellBorderSide.Bottom); break;
                case "brdrs": SetBorderSolid(); break;
                case "brdrnil":
                case "brdrnone": SetBorderNone(); break;
                case "brdrw":
                    SetBorderWidth(Math.Clamp(value / 20.0, 0.05, 72.0));
                    break;
                case "brdrcf":
                    SetBorderColor(ResolveColorRgb(value) ?? 0);
                    break;
                case "clvertalt": _pendingCellStyle.Anchor = TableCellAnchor.Top; break;
                case "clvertalc": _pendingCellStyle.Anchor = TableCellAnchor.Middle; break;
                case "clvertalb": _pendingCellStyle.Anchor = TableCellAnchor.Bottom; break;
                case "cltxlrtb": _pendingCellStyle.TextVerticalType = TextVerticalType.Horizontal; break;
                case "cltxtbrl": _pendingCellStyle.TextVerticalType = TextVerticalType.Vertical; break;
                case "cltxbtlr": _pendingCellStyle.TextVerticalType = TextVerticalType.Vertical270; break;
                case "clpadl": _pendingCellStyle.InsetLeftPt = ToCellInsetPoints(value); break;
                case "clpadr": _pendingCellStyle.InsetRightPt = ToCellInsetPoints(value); break;
                case "clpadt": _pendingCellStyle.InsetTopPt = ToCellInsetPoints(value); break;
                case "clpadb": _pendingCellStyle.InsetBottomPt = ToCellInsetPoints(value); break;
                case "clmgf": _pendingCellStyle.HorizontalMergeStart = true; break;
                case "clmrg": _pendingCellStyle.HorizontalMergeContinuation = true; break;
                case "clvmgf": _pendingCellStyle.VerticalMergeStart = true; break;
                case "clvmrg": _pendingCellStyle.VerticalMergeContinuation = true; break;
                case "trleft":
                    if (CurrentTableCapture() is { } indentCapture && parameter is { } indentTwips)
                        indentCapture.Table.RichTextLeftIndentPt = indentTwips / 20.0;
                    break;
                case "trgaph":
                    if (CurrentTableCapture() is { } spacingCapture && parameter is { } gapTwips)
                        spacingCapture.Table.RichTextCellSpacingPt = Math.Max(0, gapTwips / 10.0);
                    break;
                case "trql":
                    SetCurrentTableRowAlignment(TableRowHorizontalAlignment.Left);
                    break;
                case "trqc":
                    SetCurrentTableRowAlignment(TableRowHorizontalAlignment.Center);
                    break;
                case "trqr":
                    SetCurrentTableRowAlignment(TableRowHorizontalAlignment.Right);
                    break;
                case "trbrdrl": BeginRowBorder(TableCellBorderSide.Left); break;
                case "trbrdrr": BeginRowBorder(TableCellBorderSide.Right); break;
                case "trbrdrt": BeginRowBorder(TableCellBorderSide.Top); break;
                case "trbrdrb": BeginRowBorder(TableCellBorderSide.Bottom); break;
                case "trbrdrv": BeginRowInteriorBorder(); break;
                case "trpaddl":
                    if (CurrentTableCapture() is { } leftCapture && parameter is { } leftPadding)
                        leftCapture.RowInsetLeftPt = ToCellInsetPoints(leftPadding);
                    break;
                case "trpaddr":
                    if (CurrentTableCapture() is { } rightCapture && parameter is { } rightPadding)
                        rightCapture.RowInsetRightPt = ToCellInsetPoints(rightPadding);
                    break;
                case "trpaddt":
                    if (CurrentTableCapture() is { } topCapture && parameter is { } topPadding)
                        topCapture.RowInsetTopPt = ToCellInsetPoints(topPadding);
                    break;
                case "trpaddb":
                    if (CurrentTableCapture() is { } bottomCapture && parameter is { } bottomPadding)
                        bottomCapture.RowInsetBottomPt = ToCellInsetPoints(bottomPadding);
                    break;
                case "clpadfl":
                case "clpadfr":
                case "clpadft":
                case "clpadfb":
                case "clshdrawnil":
                    break;
                case "trrh":
                    if (CurrentTableCapture() is { CurrentRow: { } row }
                        && parameter is { } rowHeightTwips
                        && rowHeightTwips != 0)
                    {
                        long heightTwips = Math.Abs((long)rowHeightTwips);
                        row.HeightEmu = Math.Clamp(heightTwips * 635L, 0L, 63_500_000_000L);
                        row.HeightRule = rowHeightTwips > 0
                            ? TableRowHeightRule.AtLeast
                            : TableRowHeightRule.Exact;
                    }
                    break;
            }
        }

        private void AppendByte(byte value)
        {
            if (_state.Destination == Destination.Picture)
            {
                AppendPictureHex(value);
                return;
            }

            if (_state.Destination == Destination.ObjectData)
            {
                AppendObjectHex(value);
                return;
            }

            if (_state.Destination == Destination.ObjectClass)
            {
                if (value != 0)
                    _objectClass.Append(DecodeByte(value));
                return;
            }

            if (_state.Destination == Destination.ObjectName)
            {
                if (value != 0)
                    _objectName.Append(DecodeByte(value));
                return;
            }

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

            if (_state.Destination == Destination.ListLevelText)
            {
                if (_state.ListLevelTextPrefixBytesRemaining > 0)
                {
                    _state.ListLevelTextPrefixBytesRemaining--;
                    return;
                }

                if (value is not (byte)';' and not (byte)'\r' and not (byte)'\n')
                    CaptureListLevelTextByte(value);
                return;
            }

            if (_state.Destination == Destination.ListLevelNumbers)
            {
                if (_state.ListLevelTextPrefixBytesRemaining > 0)
                    _state.ListLevelTextPrefixBytesRemaining--;
                return;
            }

            if (_state.Destination == Destination.FieldInstruction)
            {
                AppendFieldInstruction(DecodeByte(value));
                return;
            }

            if (_state.Destination is Destination.ListTable
                or Destination.ListOverrideTable
                or Destination.ListLevelText
                or Destination.Skip)
                return;

            if (_state.SkipOutput || _state.Destination != Destination.Body)
                return;

            AppendText(DecodeByte(value).ToString());
        }

        private void AppendPictureHex(byte value)
        {
            if (value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                return;

            int nibble = HexValue(value);
            if (nibble < 0)
                return;

            if (_picturePendingNibble < 0)
            {
                _picturePendingNibble = nibble;
                return;
            }

            if (_pictureBytes.Count >= MaxRtfBytes)
                throw new InvalidDataException("RTF picture limit exceeded.");

            _pictureBytes.Add((byte)((_picturePendingNibble << 4) | nibble));
            _picturePendingNibble = -1;
        }

        private void ReadPictureBinary(int count)
        {
            int end = Math.Min(_bytes.Length, _position + count);
            if (end - _position > MaxRtfBytes - _pictureBytes.Count)
                throw new InvalidDataException("RTF picture limit exceeded.");

            for (; _position < end; _position++)
                _pictureBytes.Add(_bytes[_position]);
        }

        private void AppendObjectHex(byte value)
        {
            if (value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                return;

            int nibble = HexValue(value);
            if (nibble < 0)
                return;

            if (_objectPendingNibble < 0)
            {
                _objectPendingNibble = nibble;
                return;
            }

            if (_objectBytes.Count >= MaxRtfBytes)
                throw new InvalidDataException("RTF embedded-object limit exceeded.");

            _objectBytes.Add((byte)((_objectPendingNibble << 4) | nibble));
            _objectPendingNibble = -1;
        }

        private void ReadObjectBinary(int count)
        {
            int end = Math.Min(_bytes.Length, _position + count);
            if (end - _position > MaxRtfBytes - _objectBytes.Count)
                throw new InvalidDataException("RTF embedded-object limit exceeded.");

            for (; _position < end; _position++)
                _objectBytes.Add(_bytes[_position]);
        }

        private void FinalizePictureCapture()
        {
            if (!_pictureCaptureStarted)
                return;

            byte[] payload = _pictureBytes.ToArray();
            long? widthEmu = ToPictureExtentEmu(_pictureWidthGoalTwips, _pictureScaleX);
            long? heightEmu = ToPictureExtentEmu(_pictureHeightGoalTwips, _pictureScaleY);
            string? contentType = HasPrefix(payload, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
                ? "image/png"
                : HasPrefix(payload, [0xFF, 0xD8, 0xFF])
                    ? "image/jpeg"
                    : null;
            if (contentType is not null)
            {
                var image = new InCanvasRichClipboardImage(payload, contentType, widthEmu, heightEmu);
                _picturePayloads.Add(image);
                FlushActiveRun();
                EnsureParagraph();
                ApplyParagraphState(_body.Paragraphs[^1]);
                var style = CurrentStyle();
                _body.Paragraphs[^1].Runs.Add(new Run
                {
                    Text = "\uFFFC",
                    InlineImage = new ImagePart
                    {
                        Bytes = payload,
                        ContentType = contentType,
                    },
                    InlineImageWidthEmu = widthEmu,
                    InlineImageHeightEmu = heightEmu,
                    FontFamily = style.FontFamily,
                    FontSizePt = style.FontSizePt,
                    Bold = style.Bold,
                    BoldSet = style.BoldSet,
                    Italic = style.Italic,
                    ItalicSet = style.ItalicSet,
                    Underline = style.Underline,
                    Strikethrough = style.Strikethrough,
                    BaselineOffset = style.BaselineOffset,
                    Caps = style.Caps,
                    RightToLeft = style.RunRightToLeft,
                    Color = style.Color is { } color ? new ThemeAwareColor(color) : null,
                    Hyperlink = style.Hyperlink,
                });
            }

            _pictureBytes.Clear();
            _picturePendingNibble = -1;
            _pictureCaptureStarted = false;
            _pictureWidthGoalTwips = null;
            _pictureHeightGoalTwips = null;
            _pictureScaleX = 100;
            _pictureScaleY = 100;
        }

        private static long? ToPictureExtentEmu(int? twips, double scalePercent)
        {
            if (twips is not > 0 || !double.IsFinite(scalePercent) || scalePercent <= 0)
                return null;

            double scaledTwips = twips.Value * Math.Clamp(scalePercent, 1, 1_000) / 100.0;
            return Math.Clamp(
                (long)Math.Round(scaledTwips * 635.0),
                9_525L,
                63_500_000_000L);
        }

        private void FinalizeObjectCapture()
        {
            if (!_objectCaptureStarted)
                return;

            if (_objectBytes.Count > 0)
            {
                byte[] bytes = _objectBytes.ToArray();
                string fileName = ResolveObjectFileName();
                string? className = ResolveObjectClassName();
                _objectPayloads.Add(new InCanvasRichClipboardObject(bytes, fileName, className));

                // Keep object order and caret semantics inside the rich editor. The detached
                // payload list remains for slide-level fallback insertion, while this marker
                // lets a rich-text paste retain the object at its authored inline position.
                FlushActiveRun();
                EnsureParagraph();
                ApplyParagraphState(_body.Paragraphs[^1]);
                var style = CurrentStyle();
                _body.Paragraphs[^1].Runs.Add(new Run
                {
                    Text = "\uFFFC",
                    InlineOleObject = new InlineOleObjectInfo
                    {
                        EmbeddedBytes = bytes,
                        FileName = fileName,
                        ClassName = className,
                    },
                    FontFamily = style.FontFamily,
                    FontSizePt = style.FontSizePt,
                    Bold = style.Bold,
                    BoldSet = style.BoldSet,
                    Italic = style.Italic,
                    ItalicSet = style.ItalicSet,
                    Underline = style.Underline,
                    Strikethrough = style.Strikethrough,
                    BaselineOffset = style.BaselineOffset,
                    Caps = style.Caps,
                    RightToLeft = style.RunRightToLeft,
                    Color = style.Color is { } color ? new ThemeAwareColor(color) : null,
                    Hyperlink = style.Hyperlink,
                });
            }

            _objectBytes.Clear();
            _objectPendingNibble = -1;
            _objectCaptureStarted = false;
        }

        private string ResolveObjectFileName()
        {
            string name = Path.GetFileName(_objectName.ToString().Trim());
            if (Path.GetExtension(name).Length > 0)
                return name;

            string objectClass = _objectClass.ToString().Trim();
            if (objectClass.Contains("excel.sheet", StringComparison.OrdinalIgnoreCase))
                return "Embedded.xlsx";
            if (objectClass.Contains("word.document", StringComparison.OrdinalIgnoreCase))
                return "Embedded.docx";
            if (objectClass.Contains("powerpoint", StringComparison.OrdinalIgnoreCase))
                return "Embedded.pptx";
            return "Embedded.bin";
        }

        private string? ResolveObjectClassName()
        {
            var className = _objectClass.ToString().Trim();
            return className.Length == 0 ? null : className;
        }

        private static bool HasPrefix(byte[] value, byte[] prefix)
        {
            if (value.Length < prefix.Length)
                return false;

            for (int i = 0; i < prefix.Length; i++)
            {
                if (value[i] != prefix[i])
                    return false;
            }

            return true;
        }

        private void AppendText(string text)
        {
            if (text.Length == 0)
                return;

            if (_state.Destination == Destination.FieldInstruction)
            {
                AppendFieldInstruction(text);
                return;
            }

            if (_state.SkipOutput || _state.Destination != Destination.Body)
                return;
            if (_outputCharacters > MaxOutputCharacters - text.Length)
                throw new InvalidDataException("RTF output limit exceeded.");

            CloseCompletedCapturesForText();
            var paragraph = _body.Paragraphs[^1];
            ApplyParagraphState(paragraph);
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
            if (!_suppressCaptureForNextAppend)
                AppendCapturedText(text);
            _lastWasParagraphBreak = false;
        }

        private void AppendParagraphBreak()
        {
            if (_state.SkipOutput || _state.Destination != Destination.Body)
                return;
            CloseCompletedCapturesForText();
            if (!_suppressCaptureForNextParagraphBreak)
                AppendCapturedParagraphBreak();
            _suppressCaptureForNextParagraphBreak = false;
            FlushActiveRun();
            EnsureParagraph();
            ApplyParagraphState(_body.Paragraphs[^1]);
            _legacyList = null;
            _body.Paragraphs.Add(new Paragraph());
            _lastWasParagraphBreak = true;
        }

        private void BeginTableRow()
        {
            if (_state.SkipOutput || _state.Destination != Destination.Body)
                return;

            BeginCapturedTableRow();
            FlushActiveRun();
            if (_tableRowActive)
                CaptureTableColumnWidths();
            _tableRowActive = true;
            _tableCellCount = 0;
            _tableCellRightEdgesTwips.Clear();
            _containsTable = true;
            _state.InTable = true;
        }

        private void AppendTableCellBoundary()
        {
            if (_state.SkipOutput || _state.Destination != Destination.Body)
                return;

            CaptureTableCellBoundary(CurrentTableLevel());

            if (!_tableRowActive)
            {
                if (!_state.InTable)
                    return;
                BeginTableRow();
            }

            if (++_tableCellCount > MaxTableCellsPerRow)
                throw new InvalidDataException("RTF table cell limit exceeded.");

            // WPF's FlowDocument text projection places a tab at every cell boundary;
            // AppendTableRowBoundary removes the final delimiter for the completed row.
            _suppressCaptureForNextAppend = true;
            try
            {
                AppendText("\t");
            }
            finally
            {
                _suppressCaptureForNextAppend = false;
            }
        }

        private void AppendTableRowBoundary()
        {
            if (_state.SkipOutput || _state.Destination != Destination.Body || !_tableRowActive)
                return;

            CaptureTableRowBoundary(CurrentTableLevel());
            FlushActiveRun();
            RemoveTrailingTableDelimiter();

            // A \par inside the final cell already created the row's terminating
            // paragraph. Avoid introducing an extra blank line in that case.
            if (!_lastWasParagraphBreak)
            {
                _suppressCaptureForNextParagraphBreak = true;
                try
                {
                    AppendParagraphBreak();
                }
                finally
                {
                    _suppressCaptureForNextParagraphBreak = false;
                }
            }

            CaptureTableColumnWidths();
            _tableRowActive = false;
            _tableCellCount = 0;
            _tableCellRightEdgesTwips.Clear();
        }

        private int CurrentTableLevel() => Math.Max(1, _state.TableNesting);

        private TableCaptureContext? CurrentTableCapture() =>
            _tableCaptures.Count == 0 ? null : _tableCaptures[^1];

        private void SetCurrentTableRowAlignment(TableRowHorizontalAlignment alignment)
        {
            if (CurrentTableCapture() is { CurrentRow: { } row })
                row.HorizontalAlignment = alignment;
        }

        private void BeginRowBorder(TableCellBorderSide side)
        {
            if (CurrentTableCapture() is not { } capture)
            {
                _rowBorderPending = false;
                _activeRowBorderStyle = null;
                return;
            }

            _activeRowBorderStyle = capture.RowBorders;
            _activeRowBorderStyle.BeginBorder(side);
            _rowBorderPending = true;
        }

        private void BeginRowInteriorBorder()
        {
            if (CurrentTableCapture() is not { } capture)
            {
                _rowBorderPending = false;
                _activeRowBorderStyle = null;
                return;
            }

            _activeRowBorderStyle = capture.RowInsideVerticalBorders;
            _activeRowBorderStyle.BeginBorder(TableCellBorderSide.Right);
            _rowBorderPending = true;
        }

        private void BeginCellBorder(TableCellBorderSide side)
        {
            _rowBorderPending = false;
            _activeRowBorderStyle = null;
            _pendingCellStyle.BeginBorder(side);
        }

        private void SetBorderSolid()
        {
            if (_rowBorderPending && _activeRowBorderStyle is { } rowStyle)
                rowStyle.SetSolid();
            else
                _pendingCellStyle.SetSolid();
        }

        private void SetBorderNone()
        {
            if (_rowBorderPending && _activeRowBorderStyle is { } rowStyle)
                rowStyle.SetNone();
            else
                _pendingCellStyle.SetNone();
        }

        private void SetBorderWidth(double widthPt)
        {
            if (_rowBorderPending && _activeRowBorderStyle is { } rowStyle)
                rowStyle.SetWidth(widthPt);
            else
                _pendingCellStyle.SetWidth(widthPt);
        }

        private void SetBorderColor(int rgb)
        {
            if (_rowBorderPending && _activeRowBorderStyle is { } rowStyle)
                rowStyle.SetColor(rgb);
            else
                _pendingCellStyle.SetColor(rgb);
        }

        private void BeginCapturedTableRow()
        {
            int level = CurrentTableLevel();
            CloseCapturedLevelsAbove(level);
            var capture = CurrentTableCapture();
            if (capture is null || capture.Level != level)
            {
                FlushActiveRun();
                capture = new TableCaptureContext { Level = level };
                _tableCaptures.Add(capture);
                if (level > 1)
                {
                    _nestedTableSeen = true;
                }
                else
                {
                    var marker = new Run { Text = "\uFFFC" };
                    EnsureParagraph();
                    _body.Paragraphs[^1].Runs.Add(marker);
                    capture.MarkerRun = marker;
                    _rootTableCaptures.Add(capture);
                }
            }

            if (capture.CurrentRow is null)
            {
                capture.CurrentRow = new TableRow();
                capture.RowInsetLeftPt = null;
                capture.RowInsetRightPt = null;
                capture.RowInsetTopPt = null;
                capture.RowInsetBottomPt = null;
                capture.RowBorders.Reset();
                capture.RowInsideVerticalBorders.Reset();
                capture.CurrentCellIndex = 0;
                capture.RightEdgesTwips.Clear();
                capture.CellStyles.Clear();
                capture.RowCompleted = false;
                _rowBorderPending = false;
                _activeRowBorderStyle = null;
            }
        }

        private void CaptureTableCellBoundary(int level)
        {
            CloseCapturedLevelsAbove(level);
            var capture = CurrentTableCapture();
            if (capture is null || capture.Level != level)
                return;

            EnsureCapturedCell(capture);
            FlushCapturedRun(capture);
            capture.CurrentCellIndex++;
        }

        private void CaptureTableRowBoundary(int level)
        {
            CloseCapturedLevelsAbove(level);
            var capture = CurrentTableCapture();
            if (capture is null || capture.Level != level || capture.CurrentRow is null)
                return;

            FlushCapturedRun(capture);
            if (capture.CurrentRow.Cells.Count == 0)
                EnsureCapturedCell(capture);
            ApplyCapturedRowBorders(capture.CurrentRow, capture.RowBorders.Snapshot());
            capture.Table.Rows.Add(capture.CurrentRow);
            ApplyCapturedInteriorVerticalBorders(
                capture.CurrentRow,
                capture.RowInsideVerticalBorders.Snapshot());
            CaptureTableWidths(capture);
            capture.CurrentRow = null;
            capture.CurrentCellIndex = 0;
            capture.RowCompleted = true;
            _rowBorderPending = false;
            _activeRowBorderStyle = null;
        }

        private void AppendCapturedText(string text)
        {
            var capture = CurrentTableCapture();
            if (capture is null)
                return;

            EnsureCapturedCell(capture);
            var paragraph = capture.CurrentRow!.Cells[capture.CurrentCellIndex].TextBody!
                .Paragraphs[^1];
            ApplyParagraphState(paragraph);
            var style = CurrentStyle();
            if (!capture.HasActiveStyle || !SameStyle(capture.ActiveStyle, style))
            {
                FlushCapturedRun(capture);
                capture.ActiveStyle = style;
                capture.HasActiveStyle = true;
            }
            capture.ActiveText.Append(text);
        }

        private void AppendCapturedParagraphBreak()
        {
            var capture = CurrentTableCapture();
            if (capture is null)
                return;

            EnsureCapturedCell(capture);
            FlushCapturedRun(capture);
            var body = capture.CurrentRow!.Cells[capture.CurrentCellIndex].TextBody!;
            ApplyParagraphState(body.Paragraphs[^1]);
            body.Paragraphs.Add(new Paragraph());
        }

        private void EnsureCapturedCell(TableCaptureContext capture)
        {
            capture.CurrentRow ??= new TableRow();
            while (capture.CurrentRow.Cells.Count <= capture.CurrentCellIndex)
            {
                int index = capture.CurrentRow.Cells.Count;
                var cell = new TableCell
                {
                    TextBody = new TextBody
                    {
                        Paragraphs = { new Paragraph() },
                    },
                };
                if (index < capture.CellStyles.Count)
                    ApplyCapturedCellStyle(cell, capture.CellStyles[index]);
                cell.InsetLeftPt ??= capture.RowInsetLeftPt;
                cell.InsetRightPt ??= capture.RowInsetRightPt;
                cell.InsetTopPt ??= capture.RowInsetTopPt;
                cell.InsetBottomPt ??= capture.RowInsetBottomPt;
                capture.CurrentRow.Cells.Add(cell);
            }
        }

        private void FlushCapturedRuns()
        {
            foreach (var capture in _tableCaptures)
                FlushCapturedRun(capture);
        }

        private static void FlushCapturedRun(TableCaptureContext capture)
        {
            if (capture.CurrentRow is null
                || capture.CurrentCellIndex < 0
                || capture.CurrentCellIndex >= capture.CurrentRow.Cells.Count
                || !capture.HasActiveStyle
                || capture.ActiveText.Length == 0)
            {
                capture.ActiveText.Clear();
                capture.HasActiveStyle = false;
                return;
            }

            capture.CurrentRow.Cells[capture.CurrentCellIndex].TextBody!.Paragraphs[^1].Runs.Add(
                RunFromCharacterStyle(capture.ActiveText.ToString(), capture.ActiveStyle));
            capture.ActiveText.Clear();
            capture.HasActiveStyle = false;
        }

        private static Run RunFromCharacterStyle(string text, CharacterStyle style) => new()
        {
            Text = text,
            FontFamily = style.FontFamily,
            FontSizePt = style.FontSizePt,
            Bold = style.Bold,
            BoldSet = style.BoldSet,
            Italic = style.Italic,
            ItalicSet = style.ItalicSet,
            Underline = style.Underline,
            Strikethrough = style.Strikethrough,
            TextOutline = style.TextOutline
                ? new ShapeOutline.Visible(ThemeAwareColor.Black, 0.75)
                : null,
            TextShadow = style.TextShadow
                ? new RunTextShadow()
                : null,
            BaselineOffset = style.BaselineOffset,
            Caps = style.Caps,
            RightToLeft = style.RunRightToLeft,
            Color = style.Color is { } color ? new ThemeAwareColor(color) : null,
            TextFill = style.TextFillColor is { } textFill ? new ShapeFill.Solid(textFill) : null,
            Hyperlink = style.Hyperlink,
        };

        private void CloseCapturedLevelsAbove(int level)
        {
            while (_tableCaptures.Count > 0 && _tableCaptures[^1].Level > level)
            {
                var child = _tableCaptures[^1];
                FinalizeCapturedContext(child);
                _tableCaptures.RemoveAt(_tableCaptures.Count - 1);
                if (_tableCaptures.Count > 0)
                {
                    var parent = _tableCaptures[^1];
                    EnsureCapturedCell(parent);
                    FlushCapturedRun(parent);
                    parent.CurrentRow!.Cells[parent.CurrentCellIndex].TextBody!.Paragraphs[^1].Runs.Add(
                        new Run
                        {
                            Text = "\uFFFC",
                            InlineTable = new InlineTableInfo { Table = child.Table },
                        });
                }
            }
        }

        private void CloseCompletedCapturesForText()
        {
            while (_tableCaptures.Count > 0
                && _tableCaptures[^1].RowCompleted)
            {
                var capture = _tableCaptures[^1];
                FinalizeCapturedContext(capture);
                _tableCaptures.RemoveAt(_tableCaptures.Count - 1);
                if (_tableCaptures.Count == 0)
                    continue;

                var parent = _tableCaptures[^1];
                EnsureCapturedCell(parent);
                FlushCapturedRun(parent);
                parent.CurrentRow!.Cells[parent.CurrentCellIndex].TextBody!.Paragraphs[^1].Runs.Add(
                    new Run
                    {
                        Text = "\uFFFC",
                        InlineTable = new InlineTableInfo { Table = capture.Table },
                    });
            }
        }

        private void CloseAllTableCaptures()
        {
            while (_tableCaptures.Count > 0)
            {
                var capture = _tableCaptures[^1];
                FinalizeCapturedContext(capture);
                _tableCaptures.RemoveAt(_tableCaptures.Count - 1);
                if (_tableCaptures.Count > 0)
                {
                    var parent = _tableCaptures[^1];
                    EnsureCapturedCell(parent);
                    FlushCapturedRun(parent);
                    parent.CurrentRow!.Cells[parent.CurrentCellIndex].TextBody!.Paragraphs[^1].Runs.Add(
                        new Run
                        {
                            Text = "\uFFFC",
                            InlineTable = new InlineTableInfo { Table = capture.Table },
                        });
                }
            }
        }

        private void FinalizeCapturedContext(TableCaptureContext capture)
        {
            FlushCapturedRun(capture);
            if (capture.CurrentRow is not null)
            {
                if (capture.CurrentRow.Cells.Count == 0)
                    EnsureCapturedCell(capture);
                capture.Table.Rows.Add(capture.CurrentRow);
                CaptureTableWidths(capture);
                capture.CurrentRow = null;
            }

            if (capture.Level == 1 && capture.EndMarkerRun is null)
            {
                EnsureParagraph();
                capture.EndMarkerRun = new Run { Text = "\uE000" };
                _body.Paragraphs[^1].Runs.Add(capture.EndMarkerRun);
                capture.MarkerRun!.InlineTable = new InlineTableInfo { Table = capture.Table };
            }
        }

        private static void CaptureTableWidths(TableCaptureContext capture)
        {
            if (capture.Table.ColumnWidthsEmu.Count > 0 || capture.RightEdgesTwips.Count < 2)
                return;

            long previous = 0;
            foreach (var edge in capture.RightEdgesTwips)
            {
                if (edge <= previous)
                    return;
                capture.Table.ColumnWidthsEmu.Add((edge - previous) * 635L);
                previous = edge;
            }
        }

        private static void ApplyCapturedCellStyle(
            TableCell cell,
            InCanvasRichClipboardTableCellStyle style)
        {
            if (style.FillPattern is { Length: > 0 } pattern)
            {
                cell.Fill = new ShapeFill.Pattern(
                    pattern,
                    new ThemeAwareColor(SrgbColor.FromRgb(style.FillForegroundRgb ?? style.FillRgb ?? 0)),
                    new ThemeAwareColor(SrgbColor.FromRgb(style.FillBackgroundRgb ?? style.FillRgb ?? 0xFFFFFF)));
            }
            else if (style.FillRgb is { } fillRgb)
            {
                cell.Fill = new ShapeFill.Solid(SrgbColor.FromRgb(fillRgb));
            }

            cell.Anchor = style.Anchor;
            if (style.TextVerticalType is { } textVerticalType && cell.TextBody is { } body)
                body.VerticalType = textVerticalType;
            cell.InsetLeftPt = style.InsetLeftPt;
            cell.InsetRightPt = style.InsetRightPt;
            cell.InsetTopPt = style.InsetTopPt;
            cell.InsetBottomPt = style.InsetBottomPt;
            cell.HMerge = style.HorizontalMergeContinuation;
            cell.VMerge = style.VerticalMergeContinuation;
            if (style.HorizontalMergeStart)
                cell.GridSpan = 2;
            if (style.VerticalMergeStart)
                cell.RowSpan = 2;

            cell.Borders = new TableCellBorders
            {
                Left = ToCapturedOutline(style.Left),
                Right = ToCapturedOutline(style.Right),
                Top = ToCapturedOutline(style.Top),
                Bottom = ToCapturedOutline(style.Bottom),
            };
        }

        private static void ApplyCapturedRowBorders(
            TableRow row,
            InCanvasRichClipboardTableCellStyle style)
        {
            if (row.Cells.Count == 0)
                return;

            SetRowBorder(row.Cells[0], TableCellBorderSide.Left, style.Left);
            SetRowBorder(row.Cells[^1], TableCellBorderSide.Right, style.Right);
            foreach (var cell in row.Cells)
            {
                SetRowBorder(cell, TableCellBorderSide.Top, style.Top);
                SetRowBorder(cell, TableCellBorderSide.Bottom, style.Bottom);
            }
        }

        private static void ApplyCapturedInteriorVerticalBorders(
            TableRow row,
            InCanvasRichClipboardTableCellStyle style)
        {
            if (row.Cells.Count < 2 || style.Right is null)
                return;

            for (int i = 0; i < row.Cells.Count - 1; i++)
            {
                SetRowBorder(row.Cells[i], TableCellBorderSide.Right, style.Right);
                SetRowBorder(row.Cells[i + 1], TableCellBorderSide.Left, style.Right);
            }
        }

        private static void SetRowBorder(
            TableCell cell,
            TableCellBorderSide side,
            InCanvasRichClipboardTableBorder? border)
        {
            if (border is null)
                return;

            cell.Borders ??= new TableCellBorders();
            var outline = ToCapturedOutline(border);
            switch (side)
            {
                case TableCellBorderSide.Left when cell.Borders.Left is null:
                    cell.Borders.Left = outline;
                    break;
                case TableCellBorderSide.Right when cell.Borders.Right is null:
                    cell.Borders.Right = outline;
                    break;
                case TableCellBorderSide.Top when cell.Borders.Top is null:
                    cell.Borders.Top = outline;
                    break;
                case TableCellBorderSide.Bottom when cell.Borders.Bottom is null:
                    cell.Borders.Bottom = outline;
                    break;
            }
        }

        private static ShapeOutline? ToCapturedOutline(InCanvasRichClipboardTableBorder? border) =>
            border switch
            {
                null => null,
                { IsNone: true } => ShapeOutline.None.Instance,
                _ => new ShapeOutline.Visible(
                    SrgbColor.FromRgb(border.ColorRgb),
                    border.WidthPt <= 0 ? 0.75 : border.WidthPt),
            };

        private void RewriteNestedTableMarkers()
        {
            foreach (var root in _rootTableCaptures)
            {
                if (root.MarkerRun is null || root.EndMarkerRun is null)
                    continue;

                var start = FindRun(root.MarkerRun);
                var end = FindRun(root.EndMarkerRun);
                if (start is null || end is null)
                    continue;

                if (!_nestedTableSeen)
                {
                    start.Value.Paragraph.Runs.Remove(root.MarkerRun);
                    end.Value.Paragraph.Runs.Remove(root.EndMarkerRun);
                    continue;
                }

                RemoveRunsBetween(start.Value, end.Value);
            }
        }

        private (Paragraph Paragraph, int Index)? FindRun(Run target)
        {
            foreach (var paragraph in _body.Paragraphs)
            {
                int index = paragraph.Runs.IndexOf(target);
                if (index >= 0)
                    return (paragraph, index);
            }
            return null;
        }

        private void RemoveRunsBetween(
            (Paragraph Paragraph, int Index) start,
            (Paragraph Paragraph, int Index) end)
        {
            int startParagraph = _body.Paragraphs.IndexOf(start.Paragraph);
            int endParagraph = _body.Paragraphs.IndexOf(end.Paragraph);
            if (startParagraph < 0 || endParagraph < startParagraph)
                return;

            if (startParagraph == endParagraph)
            {
                int count = end.Index - start.Index - 1;
                if (count > 0)
                    start.Paragraph.Runs.RemoveRange(start.Index + 1, count);
                start.Paragraph.Runs.RemoveAt(start.Index + 1);
                return;
            }

            if (start.Paragraph.Runs.Count > start.Index + 1)
                start.Paragraph.Runs.RemoveRange(
                    start.Index + 1,
                    start.Paragraph.Runs.Count - start.Index - 1);
            for (int paragraphIndex = endParagraph - 1; paragraphIndex > startParagraph; paragraphIndex--)
                _body.Paragraphs.RemoveAt(paragraphIndex);
            if (end.Paragraph.Runs.Count > end.Index)
                end.Paragraph.Runs.RemoveAt(end.Index);
        }

        private void CaptureTableCellStyle()
        {
            var style = _pendingCellStyle.Snapshot();
            _tableCellStyles.Add(style);
            if (CurrentTableCapture() is { } capture)
                capture.CellStyles.Add(style);
            _pendingCellStyle.Reset();
        }

        private void CaptureTableColumnWidths()
        {
            if (_tableColumnWidthsEmu is not null || _tableCellRightEdgesTwips.Count < 2)
                return;

            long previousTwips = 0;
            var widths = new List<long>(_tableCellRightEdgesTwips.Count);
            foreach (long rightEdgeTwips in _tableCellRightEdgesTwips)
            {
                if (rightEdgeTwips <= previousTwips)
                    return;

                widths.Add((rightEdgeTwips - previousTwips) * 635L);
                previousTwips = rightEdgeTwips;
            }

            _tableColumnWidthsEmu = widths;
        }

        private void RemoveTrailingTableDelimiter()
        {
            if (_body.Paragraphs.Count == 0)
                return;

            var paragraph = _body.Paragraphs[^1];
            var run = paragraph.Runs.LastOrDefault();
            if (run is null || !run.Text.EndsWith('\t'))
                return;

            run.Text = run.Text[..^1];
            _outputCharacters = Math.Max(0, _outputCharacters - 1);
            if (run.Text.Length == 0)
                paragraph.Runs.Remove(run);
        }

        private CharacterStyle CurrentStyle(State? state = null)
        {
            state ??= _state;
            string? fontFamily = null;
            int fontIndex = state.FontIndex >= 0 ? state.FontIndex : state.DefaultFontIndex;
            if (fontIndex >= 0)
                _fonts.TryGetValue(fontIndex, out fontFamily);

            SrgbColor? color = null;
            if (state.ColorIndex > 0 && state.ColorIndex < _colors.Count)
                color = _colors[state.ColorIndex];

            return new CharacterStyle(
                fontFamily,
                state.FontSizePt,
                state.Bold,
                state.Italic,
                state.Underline,
                state.Strikethrough,
                state.TextOutline,
                state.TextShadow,
                state.BaselineOffset,
                state.Caps,
                state.RunRightToLeft,
                color,
                state.TextFillColorIndex > 0
                    && state.TextFillColorIndex < _colors.Count
                    ? _colors[state.TextFillColorIndex]
                    : null,
                state.BoldSet,
                state.ItalicSet,
                state.Hyperlink,
                TryReadExternalFieldType(state.Field?.Instruction.ToString()));
        }

        private int? ResolveColorRgb(int colorIndex) =>
            colorIndex > 0 && colorIndex < _colors.Count && _colors[colorIndex] is { } color
                ? (color.R << 16) | (color.G << 8) | color.B
                : null;

        private static double ToCellInsetPoints(int twips) =>
            Math.Clamp(twips / 20.0, 0.0, 72.0);

        private static string PatternForPercentage(int percentage)
        {
            int[] supported = [0, 5, 10, 20, 25, 30, 40, 50, 60, 75, 90, 100];
            int normalized = Math.Clamp(percentage, 0, 100);
            int nearest = supported.OrderBy(value => Math.Abs(value - normalized)).First();
            return $"pct{nearest}";
        }

        // RTF up/dn values are half-points; the shared run model stores the
        // equivalent DrawingML-style thousandths of a percent of the font size.
        private int RtfBaselineOffset(int halfPoints)
        {
            double fontSizePt = _state.FontSizePt ?? 12.0;
            return (int)Math.Clamp(
                Math.Round(halfPoints * 50_000.0 / fontSizePt),
                1,
                100_000);
        }

        private void FlushActiveRun()
        {
            FlushCapturedRuns();
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
                Strikethrough = _activeStyle.Strikethrough,
                TextOutline = _activeStyle.TextOutline
                    ? new ShapeOutline.Visible(ThemeAwareColor.Black, 0.75)
                    : null,
                TextShadow = _activeStyle.TextShadow
                    ? new RunTextShadow()
                    : null,
                BaselineOffset = _activeStyle.BaselineOffset,
                Caps = _activeStyle.Caps,
                RightToLeft = _activeStyle.RunRightToLeft,
                Color = _activeStyle.Color is { } color ? new ThemeAwareColor(color) : null,
                TextFill = _activeStyle.TextFillColor is { } textFill ? new ShapeFill.Solid(textFill) : null,
                Hyperlink = _activeStyle.Hyperlink,
                Field = _activeStyle.FieldType is { } fieldType
                    ? new FieldRun
                    {
                        FieldType = fieldType,
                        CachedText = _activeText.ToString(),
                        FontFamily = _activeStyle.FontFamily,
                        FontSizePt = _activeStyle.FontSizePt,
                        Bold = _activeStyle.Bold,
                        Italic = _activeStyle.Italic,
                        Color = _activeStyle.Color,
                    }
                    : null,
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
            && left.Strikethrough == right.Strikethrough
            && left.TextOutline == right.TextOutline
            && left.TextShadow == right.TextShadow
            && left.BaselineOffset == right.BaselineOffset
            && left.Caps == right.Caps
            && left.RunRightToLeft == right.RunRightToLeft
            && Nullable.Equals(left.Color, right.Color)
            && Nullable.Equals(left.TextFillColor, right.TextFillColor)
            && left.BoldSet == right.BoldSet
            && left.ItalicSet == right.ItalicSet
            && SameHyperlink(left.Hyperlink, right.Hyperlink)
            && string.Equals(left.FieldType, right.FieldType, StringComparison.Ordinal);

        private void ResetCharacterFormatting()
        {
            _state.FontIndex = -1;
            _state.FontSizePt = null;
            _state.Bold = false;
            _state.BoldSet = true;
            _state.Italic = false;
            _state.ItalicSet = true;
            _state.Underline = false;
            _state.Strikethrough = false;
            _state.TextOutline = false;
            _state.TextShadow = false;
            _state.BaselineOffset = null;
            _state.Caps = RunTextCaps.None;
            _state.RunRightToLeft = null;
            _state.ColorIndex = 0;
            _state.TextFillColorIndex = 0;
        }

        private void ResetParagraphFormatting()
        {
            _state.ParagraphAlignment = null;
            _state.ParagraphRightToLeft = null;
            _state.ListOverrideId = null;
            _state.ListLevel = 0;
            _state.LeftIndentTwips = null;
            _state.FirstLineIndentTwips = null;
            _state.SpaceBeforeTwips = null;
            _state.SpaceAfterTwips = null;
            _state.TabStops.Clear();
            _state.PendingTabStopAlignment = TabStopAlignment.Left;
            _state.PendingTabStopLeader = TabStopLeader.None;
            _state.InTable = false;
            _state.TableNesting = 0;
        }

        private void ApplyParagraphState(Paragraph paragraph)
        {
            paragraph.Align = _state.ParagraphAlignment;
            paragraph.RightToLeft = _state.ParagraphRightToLeft;
            paragraph.Level = Math.Clamp(
                _state.ListOverrideId is not null ? _state.ListLevel : 0,
                0,
                8);
            paragraph.MarginLeftEmu = ToEmu(_state.LeftIndentTwips);
            paragraph.IndentEmu = ToEmu(_state.FirstLineIndentTwips);
            paragraph.SpaceBeforePt = ToPoints(_state.SpaceBeforeTwips);
            paragraph.SpaceAfterPt = ToPoints(_state.SpaceAfterTwips);
            paragraph.TabStops.Clear();
            foreach (var tabStop in _state.TabStops)
            {
                paragraph.TabStops.Add(new TabStop
                {
                    PositionEmu = tabStop.PositionEmu,
                    Alignment = tabStop.Alignment,
                    Leader = tabStop.Leader,
                });
            }

            if (_legacyList is { } legacyList)
            {
                paragraph.Level = legacyList.Level;
                paragraph.BulletKind = legacyList.Kind;
                paragraph.BulletChar = legacyList.BulletChar;
                paragraph.AutoNumStartAt = legacyList.StartAt;
                paragraph.AutoNumStartAtSpecified = legacyList.StartSpecified;
                return;
            }

            if (_state.ListOverrideId is not { } overrideId
                || !_listOverrides.TryGetValue(overrideId, out var listOverride)
                || !_lists.TryGetValue(listOverride.ListId, out var list)
                || !list.Levels.TryGetValue(_state.ListLevel, out var level))
            {
                paragraph.BulletKind = BulletKind.None;
                paragraph.AutoNumStartAtSpecified = false;
                return;
            }

            var formattingOverride = listOverride.FormattingByLevel.TryGetValue(
                _state.ListLevel,
                out var overrideLevel)
                ? overrideLevel
                : null;
            int numberFormat = formattingOverride is { NumberFormatSpecified: true }
                ? formattingOverride.NumberFormat
                : level.NumberFormat;
            if ((formattingOverride?.LeftIndentTwips ?? level.LeftIndentTwips) is { } overrideLeftIndent
                && _state.LeftIndentTwips is null)
                paragraph.MarginLeftEmu = ToEmu(overrideLeftIndent);
            if ((formattingOverride?.FirstLineIndentTwips ?? level.FirstLineIndentTwips) is { } overrideFirstIndent
                && _state.FirstLineIndentTwips is null)
                paragraph.IndentEmu = ToEmu(overrideFirstIndent);

            if (numberFormat == 23)
            {
                paragraph.BulletKind = BulletKind.Char;
                paragraph.BulletChar = (formattingOverride?.BulletChar ?? level.BulletChar) ?? "\u2022";
                paragraph.AutoNumStartAtSpecified = false;
                return;
            }

            paragraph.BulletKind = BulletKind.Auto;
            string levelTextTemplate = formattingOverride?.LevelTextTemplate ?? level.LevelTextTemplate;
            paragraph.AutoNumType = MapAutoNumType(
                numberFormat,
                levelTextTemplate);
            paragraph.AutoNumTextTemplate = ContainsLevelSubstitution(levelTextTemplate)
                ? levelTextTemplate
                : null;
            paragraph.AutoNumStartAt = listOverride.StartAtByLevel.TryGetValue(_state.ListLevel, out var overrideStartAt)
                ? overrideStartAt
                : formattingOverride is { StartAtSpecified: true }
                    ? formattingOverride.StartAt
                    : level.StartAt;
            bool firstOccurrence = !paragraph.AutoNumStartAtSpecified
                && _seenListLevels.Add((overrideId, _state.ListLevel));
            paragraph.AutoNumStartAtSpecified |= firstOccurrence;
        }

        private void CaptureListLevelTextByte(byte value)
        {
            var level = _state.Destination == Destination.ListOverrideTable
                ? _currentListOverrideLevelDefinition
                : _currentListLevel;
            if (level is null)
                return;

            // RTF level-text stores level substitutions as zero-based control bytes
            // (0x00 = current level 0, 0x01 = level 1, ...). Keep them as the
            // renderer-neutral %1..%9 form instead of silently dropping them.
            if (value <= 8)
            {
                string token = $"%{value + 1}";
                if (level.LevelTextTemplate.Length + token.Length <= 64)
                    level.LevelTextTemplate += token;
                return;
            }

            string candidate = DecodeByte(value);
            if (candidate.Length == 0 || char.IsControl(candidate[0]) || candidate[0] == ';')
                return;

            if (level.NumberFormat == 23)
            {
                if (level.BulletChar is null)
                    level.BulletChar = candidate;
                return;
            }

            if (level.LevelTextTemplate.Length + candidate.Length <= 64)
                level.LevelTextTemplate += candidate;
        }

        private void CaptureListLevelTextChar(string text)
        {
            var level = _state.Destination == Destination.ListOverrideTable
                ? _currentListOverrideLevelDefinition
                : _currentListLevel;
            if (level is null || string.IsNullOrEmpty(text))
                return;

            if (level.NumberFormat == 23)
            {
                if (level.BulletChar is null)
                    level.BulletChar = text;
                return;
            }

            if (level.LevelTextTemplate.Length + text.Length <= 64)
                level.LevelTextTemplate += text;
        }

        private static bool ContainsLevelSubstitution(string? template)
        {
            if (string.IsNullOrEmpty(template))
                return false;

            for (int index = 0; index + 1 < template.Length; index++)
            {
                if (template[index] == '%' && template[index + 1] is >= '1' and <= '9')
                    return true;
            }

            return false;
        }

        private void AppendFieldInstruction(string text)
        {
            if (_state.Field is null || _state.Field.Instruction.Length > 4096 - text.Length)
                return;
            _state.Field.Instruction.Append(text);
        }

        private static Hyperlink? TryReadExternalHyperlink(string? instruction)
        {
            if (string.IsNullOrWhiteSpace(instruction))
                return null;

            const string prefix = "HYPERLINK";
            string value = instruction.Trim();
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;

            value = value[prefix.Length..].TrimStart();
            if (value.Length < 2 || value[0] != '"')
                return null;
            int endQuote = value.IndexOf('"', 1);
            if (endQuote <= 1 || endQuote > 4096)
                return null;

            string url = value[1..endQuote];
            if (!ExternalUriLauncher.TryCreateAllowedUri(url, out var uri)
                || uri.Scheme is not ("http" or "https" or "mailto" or "file"))
                return null;

            return new Hyperlink { Url = uri.AbsoluteUri };
        }

        private static string? TryReadExternalFieldType(string? instruction)
        {
            if (string.IsNullOrWhiteSpace(instruction))
                return null;

            string value = instruction.Trim();
            int end = 0;
            while (end < value.Length
                && !char.IsWhiteSpace(value[end])
                && value[end] != '\\'
                && value[end] != '"')
            {
                end++;
            }

            if (end == 0)
                return null;

            string fieldType = value[..end];
            // Hyperlinks retain their dedicated URI policy and must not be emitted as
            // generic field runs, otherwise the PPTX writer would lose hlinkClick metadata.
            if (fieldType.Equals("HYPERLINK", StringComparison.OrdinalIgnoreCase))
                return null;

            return fieldType.Length <= 64 ? fieldType : null;
        }

        private static bool SameHyperlink(Hyperlink? left, Hyperlink? right) =>
            string.Equals(left?.Url, right?.Url, StringComparison.Ordinal)
            && string.Equals(left?.TargetSlideId, right?.TargetSlideId, StringComparison.Ordinal)
            && string.Equals(left?.Tooltip, right?.Tooltip, StringComparison.Ordinal);

        private static long? ToEmu(int? twips) => twips is { } value
            ? Math.Clamp((long)value * 635L, -63_500_000_000L, 63_500_000_000L)
            : null;

        private static double? ToPoints(int? twips) => twips / 20.0;

        private static AutoNumType MapAutoNumType(int numberFormat, string? levelTextTemplate)
        {
            bool opensWithParen = levelTextTemplate?.Contains('(') == true;
            bool closesWithParen = levelTextTemplate?.EndsWith(')') == true;
            bool hasBothParens = opensWithParen && closesWithParen;

            return numberFormat switch
            {
                0 => hasBothParens
                    ? AutoNumType.ArabicParenBoth
                    : closesWithParen ? AutoNumType.ArabicParenR : AutoNumType.ArabicPeriod,
                1 => closesWithParen ? AutoNumType.RomanUcParenR : AutoNumType.RomanUcPeriod,
                2 => closesWithParen ? AutoNumType.RomanLcParenR : AutoNumType.RomanLcPeriod,
                3 => hasBothParens
                    ? AutoNumType.AlphaUcParenBoth
                    : closesWithParen ? AutoNumType.AlphaUcParenR : AutoNumType.AlphaUcPeriod,
                4 => hasBothParens
                    ? AutoNumType.AlphaLcParenBoth
                    : closesWithParen ? AutoNumType.AlphaLcParenR : AutoNumType.AlphaLcPeriod,
                _ => AutoNumType.ArabicPeriod,
            };
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
