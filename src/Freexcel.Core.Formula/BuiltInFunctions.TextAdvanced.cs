using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.XPath;

using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // Additional text, XML, and character conversion functions.

    // TEXTJOIN(delimiter, ignore_empty, text1, [text2, ...])
    private static ScalarValue Textjoin(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count < 3) return ErrorValue.Value;
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var delimiters = FlattenTextjoinArgument(args[0]);
        if (delimiters.Error is not null) return delimiters.Error;
        bool ignoreEmpty = ToBool(args[1]);
        var parts = new List<string>();
        for (int i = 2; i < args.Count; i++)
        {
            if (args[i] is ErrorValue e) return e;
            var values = FlattenTextjoinArgument(args[i]);
            if (values.Error is not null) return values.Error;
            foreach (var t in values.Text)
            {
                if (ignoreEmpty && t.Length == 0) continue;
                parts.Add(t);
            }
        }
        var result = JoinTextjoinParts(parts, delimiters.Text);
        return result.Length > 32767 ? ErrorValue.Value : new TextValue(result);
    }

    private static (List<string> Text, ErrorValue? Error) FlattenTextjoinArgument(ScalarValue value)
    {
        var text = new List<string>();
        if (value is RangeValue range)
        {
            foreach (var cell in range.Flatten())
            {
                if (cell is ErrorValue e) return (text, e);
                text.Add(ToText(cell));
            }
        }
        else
        {
            text.Add(ToText(value));
        }

        return (text, null);
    }

    private static string JoinTextjoinParts(IReadOnlyList<string> parts, IReadOnlyList<string> delimiters)
    {
        if (parts.Count == 0) return "";
        if (delimiters.Count == 0) return string.Concat(parts);

        var result = new StringBuilder(parts[0]);
        for (int i = 1; i < parts.Count; i++)
        {
            result.Append(delimiters[(i - 1) % delimiters.Count]);
            result.Append(parts[i]);
        }

        return result.ToString();
    }

    private static ScalarValue Exact(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[0] is RangeValue left || args[1] is RangeValue right)
            return ExactRange(args[0], args[1]);

        return new BoolValue(string.Equals(ToText(args[0]), ToText(args[1]), StringComparison.Ordinal));
    }

    private static ScalarValue ExactRange(ScalarValue left, ScalarValue right)
    {
        var leftRange = left as RangeValue;
        var rightRange = right as RangeValue;
        int rows = leftRange?.RowCount ?? rightRange?.RowCount ?? 1;
        int cols = leftRange?.ColCount ?? rightRange?.ColCount ?? 1;
        if (leftRange is not null && !CanBroadcastExactRange(leftRange, rows, cols)) return ErrorValue.Value;
        if (rightRange is not null && !CanBroadcastExactRange(rightRange, rows, cols)) return ErrorValue.Value;

        var result = new ScalarValue[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var l = ExactValueAt(left, r, c);
                var rr = ExactValueAt(right, r, c);
                if (l is ErrorValue le) return le;
                if (rr is ErrorValue re) return re;
                result[r, c] = new BoolValue(string.Equals(ToText(l), ToText(rr), StringComparison.Ordinal));
            }

        return new RangeValue(result);
    }

    private static bool CanBroadcastExactRange(RangeValue range, int rows, int cols) =>
        (range.RowCount == rows && range.ColCount == cols) || (range.RowCount == 1 && range.ColCount == 1);

    private static ScalarValue ExactValueAt(ScalarValue value, int row, int col) =>
        value is RangeValue range
            ? range.Cells[range.RowCount == 1 ? 0 : row, range.ColCount == 1 ? 0 : col]
            : value;

    private static ScalarValue Code(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapTextAdvancedRange(range, CodeScalar);
        return CodeScalar(args[0]);
    }

    private static ScalarValue CodeScalar(ScalarValue value)
    {
        if (value is ErrorValue e) return e;
        var text = ToText(value);
        if (text.Length == 0) return ErrorValue.Value;
        return new NumberValue(CharToExcelAnsiCode(text[0]));
    }

    private static ScalarValue Char(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapTextAdvancedRange(range, CharScalar);
        return CharScalar(args[0]);
    }

    private static ScalarValue CharScalar(ScalarValue value)
    {
        if (value is ErrorValue e) return e;
        var n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Value;
        int code = (int)n;
        if (code <= 0 || code > 255) return ErrorValue.Value;
        return new TextValue(ExcelAnsiCodeToChar(code).ToString());
    }

    private static RangeValue MapTextAdvancedRange(RangeValue range, Func<ScalarValue, ScalarValue> map)
    {
        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
                cells[r, c] = map(range.Cells[r, c]);

        return new RangeValue(cells);
    }

    private static ScalarValue Asc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TextResult(ConvertToHalfWidth(ToText(args[0])));
    }

    private static ScalarValue Dbcs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TextResult(ConvertToFullWidth(ToText(args[0])));
    }

    private static ScalarValue Phonetic(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var value = args[0] is RangeValue rv ? rv.At(1, 1) : args[0];
        return value is ErrorValue rangeError ? rangeError : TextResult(ToText(value));
    }

    private static ScalarValue BahtText(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;

        var value = ToNumber(args[0]);
        if (!double.IsFinite(value)) return ErrorValue.Value;

        var rounded = Math.Round(Math.Abs(value) + 1e-12, 2, MidpointRounding.AwayFromZero);
        if (rounded > long.MaxValue) return ErrorValue.Num;

        long baht = (long)Math.Floor(rounded);
        int satang = (int)Math.Round((rounded - baht) * 100, MidpointRounding.AwayFromZero);
        if (satang == 100)
        {
            baht++;
            satang = 0;
        }

        var result = new StringBuilder();
        if (value < 0) result.Append("ลบ");
        if (baht > 0 || satang == 0)
        {
            result.Append(ThaiNumberToText(baht));
            result.Append("บาท");
        }
        result.Append(satang == 0 ? "ถ้วน" : ThaiNumberToText(satang) + "สตางค์");
        return TextResult(result.ToString());
    }

    private static ScalarValue EncodeUrl(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, EncodeUrlScalar);
        return EncodeUrlScalar(args[0]);
    }

    private static ScalarValue EncodeUrlScalar(ScalarValue value) =>
        TextResult(Uri.EscapeDataString(ToText(value)));

    private static ScalarValue FilterXml(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            using var stringReader = new StringReader(ToText(args[0]));
            using var xmlReader = XmlReader.Create(stringReader, settings);
            var document = new XPathDocument(xmlReader);
            var navigator = document.CreateNavigator();
            var xpath = ToText(args[1]);
            var result = navigator.Evaluate(xpath);

            return result switch
            {
                XPathNodeIterator nodes => FilterXmlNodeResult(nodes),
                string s when s.Length > 0 => TextResult(s),
                double d when double.IsFinite(d) => TextResult(d.ToString(CultureInfo.InvariantCulture)),
                bool b => TextResult(b ? "TRUE" : "FALSE"),
                _ => ErrorValue.Value
            };
        }
        catch (XmlException)
        {
            return ErrorValue.Value;
        }
        catch (XPathException)
        {
            return ErrorValue.Value;
        }
        catch (ArgumentException)
        {
            return ErrorValue.Value;
        }
    }

    private static ScalarValue FilterXmlNodeResult(XPathNodeIterator nodes)
    {
        var values = new List<ScalarValue>();
        while (nodes.MoveNext())
        {
            values.Add(TextResult(nodes.Current?.Value ?? ""));
        }

        return values.Count switch
        {
            0 => ErrorValue.Value,
            1 => values[0],
            _ => new RangeValue(ToVerticalRange(values))
        };
    }

    private static ScalarValue[,] ToVerticalRange(IReadOnlyList<ScalarValue> values)
    {
        var cells = new ScalarValue[values.Count, 1];
        for (int i = 0; i < values.Count; i++)
            cells[i, 0] = values[i];
        return cells;
    }

    private static string ConvertToHalfWidth(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch == '\u3000')
            {
                sb.Append(' ');
            }
            else if (ch is >= '\uFF01' and <= '\uFF5E')
            {
                sb.Append((char)(ch - 0xFEE0));
            }
            else if (FullWidthKanaToHalfWidth.TryGetValue(ch, out var kana))
            {
                sb.Append(kana);
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static string ConvertToFullWidth(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == ' ')
            {
                sb.Append('\u3000');
            }
            else if (ch is >= '!' and <= '~')
            {
                sb.Append((char)(ch + 0xFEE0));
            }
            else if (i + 1 < text.Length &&
                     (text[i + 1] == '\uFF9E' || text[i + 1] == '\uFF9F') &&
                     HalfWidthKanaToFullWidth.TryGetValue(text.Substring(i, 2), out var combinedKana))
            {
                sb.Append(combinedKana);
                i++;
            }
            else if (HalfWidthKanaToFullWidth.TryGetValue(ch.ToString(), out var kana))
            {
                sb.Append(kana);
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static string ThaiNumberToText(long value)
    {
        if (value == 0) return "ศูนย์";
        if (value >= 1_000_000)
        {
            var high = value / 1_000_000;
            var low = value % 1_000_000;
            return ThaiNumberToText(high) + "ล้าน" + (low == 0 ? "" : ThaiNumberUnderMillionToText((int)low));
        }

        return ThaiNumberUnderMillionToText((int)value);
    }

    private static string ThaiNumberUnderMillionToText(int value)
    {
        string[] digits = ["", "หนึ่ง", "สอง", "สาม", "สี่", "ห้า", "หก", "เจ็ด", "แปด", "เก้า"];
        string[] positions = ["", "สิบ", "ร้อย", "พัน", "หมื่น", "แสน"];
        var chars = value.ToString(CultureInfo.InvariantCulture).Reverse().ToArray();
        var sb = new StringBuilder();

        for (int pos = chars.Length - 1; pos >= 0; pos--)
        {
            int digit = chars[pos] - '0';
            if (digit == 0) continue;

            if (pos == 1)
            {
                sb.Append(digit switch
                {
                    1 => "สิบ",
                    2 => "ยี่สิบ",
                    _ => digits[digit] + "สิบ"
                });
            }
            else if (pos == 0 && digit == 1 && value > 10)
            {
                sb.Append("เอ็ด");
            }
            else
            {
                sb.Append(digits[digit]);
                sb.Append(positions[pos]);
            }
        }

        return sb.ToString();
    }

    private static readonly Dictionary<char, string> FullWidthKanaToHalfWidth = new()
    {
        ['。'] = "｡", ['「'] = "｢", ['」'] = "｣", ['、'] = "､", ['・'] = "･",
        ['ヲ'] = "ｦ", ['ァ'] = "ｧ", ['ィ'] = "ｨ", ['ゥ'] = "ｩ", ['ェ'] = "ｪ", ['ォ'] = "ｫ",
        ['ャ'] = "ｬ", ['ュ'] = "ｭ", ['ョ'] = "ｮ", ['ッ'] = "ｯ", ['ー'] = "ｰ",
        ['ア'] = "ｱ", ['イ'] = "ｲ", ['ウ'] = "ｳ", ['エ'] = "ｴ", ['オ'] = "ｵ",
        ['カ'] = "ｶ", ['キ'] = "ｷ", ['ク'] = "ｸ", ['ケ'] = "ｹ", ['コ'] = "ｺ",
        ['サ'] = "ｻ", ['シ'] = "ｼ", ['ス'] = "ｽ", ['セ'] = "ｾ", ['ソ'] = "ｿ",
        ['タ'] = "ﾀ", ['チ'] = "ﾁ", ['ツ'] = "ﾂ", ['テ'] = "ﾃ", ['ト'] = "ﾄ",
        ['ナ'] = "ﾅ", ['ニ'] = "ﾆ", ['ヌ'] = "ﾇ", ['ネ'] = "ﾈ", ['ノ'] = "ﾉ",
        ['ハ'] = "ﾊ", ['ヒ'] = "ﾋ", ['フ'] = "ﾌ", ['ヘ'] = "ﾍ", ['ホ'] = "ﾎ",
        ['マ'] = "ﾏ", ['ミ'] = "ﾐ", ['ム'] = "ﾑ", ['メ'] = "ﾒ", ['モ'] = "ﾓ",
        ['ヤ'] = "ﾔ", ['ユ'] = "ﾕ", ['ヨ'] = "ﾖ",
        ['ラ'] = "ﾗ", ['リ'] = "ﾘ", ['ル'] = "ﾙ", ['レ'] = "ﾚ", ['ロ'] = "ﾛ",
        ['ワ'] = "ﾜ", ['ン'] = "ﾝ", ['゛'] = "ﾞ", ['゜'] = "ﾟ",
        ['ガ'] = "ｶﾞ", ['ギ'] = "ｷﾞ", ['グ'] = "ｸﾞ", ['ゲ'] = "ｹﾞ", ['ゴ'] = "ｺﾞ",
        ['ザ'] = "ｻﾞ", ['ジ'] = "ｼﾞ", ['ズ'] = "ｽﾞ", ['ゼ'] = "ｾﾞ", ['ゾ'] = "ｿﾞ",
        ['ダ'] = "ﾀﾞ", ['ヂ'] = "ﾁﾞ", ['ヅ'] = "ﾂﾞ", ['デ'] = "ﾃﾞ", ['ド'] = "ﾄﾞ",
        ['バ'] = "ﾊﾞ", ['ビ'] = "ﾋﾞ", ['ブ'] = "ﾌﾞ", ['ベ'] = "ﾍﾞ", ['ボ'] = "ﾎﾞ",
        ['パ'] = "ﾊﾟ", ['ピ'] = "ﾋﾟ", ['プ'] = "ﾌﾟ", ['ペ'] = "ﾍﾟ", ['ポ'] = "ﾎﾟ",
        ['ヴ'] = "ｳﾞ"
    };

    private static readonly Dictionary<string, string> HalfWidthKanaToFullWidth =
        FullWidthKanaToHalfWidth.ToDictionary(pair => pair.Value, pair => pair.Key.ToString(), StringComparer.Ordinal);

    private static char ExcelAnsiCodeToChar(int code) => code switch
    {
        128 => '\u20AC',
        130 => '\u201A',
        131 => '\u0192',
        132 => '\u201E',
        133 => '\u2026',
        134 => '\u2020',
        135 => '\u2021',
        136 => '\u02C6',
        137 => '\u2030',
        138 => '\u0160',
        139 => '\u2039',
        140 => '\u0152',
        142 => '\u017D',
        145 => '\u2018',
        146 => '\u2019',
        147 => '\u201C',
        148 => '\u201D',
        149 => '\u2022',
        150 => '\u2013',
        151 => '\u2014',
        152 => '\u02DC',
        153 => '\u2122',
        154 => '\u0161',
        155 => '\u203A',
        156 => '\u0153',
        158 => '\u017E',
        159 => '\u0178',
        _ => (char)code
    };

    private static int CharToExcelAnsiCode(char ch) => ch switch
    {
        '\u20AC' => 128,
        '\u201A' => 130,
        '\u0192' => 131,
        '\u201E' => 132,
        '\u2026' => 133,
        '\u2020' => 134,
        '\u2021' => 135,
        '\u02C6' => 136,
        '\u2030' => 137,
        '\u0160' => 138,
        '\u2039' => 139,
        '\u0152' => 140,
        '\u017D' => 142,
        '\u2018' => 145,
        '\u2019' => 146,
        '\u201C' => 147,
        '\u201D' => 148,
        '\u2022' => 149,
        '\u2013' => 150,
        '\u2014' => 151,
        '\u02DC' => 152,
        '\u2122' => 153,
        '\u0161' => 154,
        '\u203A' => 155,
        '\u0153' => 156,
        '\u017E' => 158,
        '\u0178' => 159,
        _ => ch
    };
}
