using System.Text;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Bounded, renderer-neutral RTF writer used for cross-application clipboard interoperability.
/// It intentionally writes only fields understood by <see cref="ExternalRichTextClipboardPlanner"/>.
/// </summary>
internal static class ExternalRichTextClipboardRtfWriter
{
    private const long EmuPerPoint = 12_700;

    public static byte[] Serialize(InCanvasRichClipboardPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var fonts = CollectFonts(payload.Body);
        var colors = CollectColors(payload.Body);
        var fontIndexes = fonts
            .Select((font, index) => (font, index))
            .ToDictionary(item => item.font, item => item.index, StringComparer.OrdinalIgnoreCase);
        var colorIndexes = colors
            .Select((color, index) => (color, index: index + 1))
            .ToDictionary(item => item.color, item => item.index);

        var output = new StringBuilder();
        output.Append(@"{\rtf1\ansi\deff0\uc1");
        output.Append(@"{\fonttbl");
        for (int index = 0; index < fonts.Count; index++)
        {
            output.Append(@"{\f").Append(index).Append(@"\fnil ");
            AppendText(output, fonts[index]);
            output.Append(';').Append('}');
        }
        output.Append('}');

        output.Append(@"{\colortbl ;");
        foreach (var color in colors)
        {
            output.Append(@"\red").Append(color.R)
                .Append(@"\green").Append(color.G)
                .Append(@"\blue").Append(color.B).Append(';');
        }
        output.Append('}');

        var paragraphs = payload.Body.Paragraphs;
        if (paragraphs.Count == 0)
            output.Append(@"\pard\par");
        else
        {
            for (int paragraphIndex = 0; paragraphIndex < paragraphs.Count; paragraphIndex++)
            {
                var paragraph = paragraphs[paragraphIndex];
                AppendParagraphStart(output, paragraph);
                foreach (var run in paragraph.Runs)
                    AppendRun(output, run, fontIndexes, colorIndexes);

                if (paragraphIndex + 1 < paragraphs.Count)
                    output.Append(@"\par");
            }
        }

        output.Append('}');
        return Encoding.ASCII.GetBytes(output.ToString());
    }

    private static List<string> CollectFonts(TextBody body)
    {
        var fonts = new List<string> { "Arial" };
        foreach (var font in body.Paragraphs
                     .SelectMany(paragraph => paragraph.Runs)
                     .Select(run => run.FontFamily)
                     .Where(font => !string.IsNullOrWhiteSpace(font)))
        {
            if (!fonts.Contains(font!, StringComparer.OrdinalIgnoreCase))
                fonts.Add(font!);
        }
        return fonts;
    }

    private static List<SrgbColor> CollectColors(TextBody body)
    {
        var colors = new List<SrgbColor>();
        foreach (var color in body.Paragraphs
                     .SelectMany(paragraph => paragraph.Runs)
                     .SelectMany(run => new[]
                     {
                         run.Color?.Resolved,
                         run.TextFill is ShapeFill.Solid solid ? solid.Color.Resolved : null,
                     })
                     .Where(color => color is not null)
                     .Select(color => color!.Value))
        {
            if (!colors.Contains(color))
                colors.Add(color);
        }
        return colors;
    }

    private static void AppendParagraphStart(StringBuilder output, Paragraph paragraph)
    {
        output.Append(@"\pard");
        switch (paragraph.Align)
        {
            case TextAlign.Center: output.Append(@"\qc"); break;
            case TextAlign.Right: output.Append(@"\qr"); break;
            case TextAlign.Justify:
            case TextAlign.Distributed: output.Append(@"\qj"); break;
            default: output.Append(@"\ql"); break;
        }

        if (paragraph.RightToLeft == true)
            output.Append(@"\rtlpar");
        else if (paragraph.RightToLeft == false)
            output.Append(@"\ltrpar");

        if (paragraph.MarginLeftEmu is { } margin)
            output.Append(@"\li").Append(ToTwips(margin));
        if (paragraph.IndentEmu is { } indent)
            output.Append(@"\fi").Append(ToTwips(indent));
        if (paragraph.SpaceBeforePt is { } before)
            output.Append(@"\sb").Append(ToTwips(before));
        if (paragraph.SpaceAfterPt is { } after)
            output.Append(@"\sa").Append(ToTwips(after));
        foreach (var tab in paragraph.TabStops)
            output.Append(@"\tx").Append(ToTwips(tab.PositionEmu));

        AppendBullet(output, paragraph);
        output.Append(' ');
    }

    private static void AppendBullet(StringBuilder output, Paragraph paragraph)
    {
        int level = Math.Clamp(paragraph.Level, 0, 8);
        int indent = 720 * (level + 1);
        switch (paragraph.BulletKind)
        {
            case BulletKind.Char when !string.IsNullOrEmpty(paragraph.BulletChar):
                output.Append(@"\pn\pnlvlblt\pnf0\pnindent360\fi-")
                    .Append(Math.Min(indent / 2, 3_600))
                    .Append(@"\li").Append(Math.Min(indent, 7_200))
                    .Append(@"{\pntxtb ");
                AppendText(output, paragraph.BulletChar!);
                output.Append("}");
                break;
            case BulletKind.Auto:
                output.Append(@"\pn\pnlvlbody\pndec\pnstart")
                    .Append(Math.Max(1, paragraph.AutoNumStartAt))
                    .Append(@"{\pntxta .}\fi-")
                    .Append(Math.Min(indent / 2, 3_600))
                    .Append(@"\li").Append(Math.Min(indent, 7_200));
                break;
        }
    }

    private static void AppendRun(
        StringBuilder output,
        Run run,
        IReadOnlyDictionary<string, int> fontIndexes,
        IReadOnlyDictionary<SrgbColor, int> colorIndexes)
    {
        output.Append(@"\plain");
        if (run.FontFamily is { Length: > 0 }
            && fontIndexes.TryGetValue(run.FontFamily, out var fontIndex))
        {
            output.Append(@"\f").Append(fontIndex);
        }
        if (run.FontSizePt is { } size && size > 0)
            output.Append(@"\fs").Append(Math.Clamp((int)Math.Round(size * 2), 2, 65_520));
        if (run.Color?.Resolved is { } color && colorIndexes.TryGetValue(color, out var colorIndex))
            output.Append(@"\cf").Append(colorIndex);
        if (run.TextFill is ShapeFill.Solid textFill
            && colorIndexes.TryGetValue(textFill.Color.Resolved, out var textFillIndex))
        {
            output.Append(@"\highlight").Append(textFillIndex);
        }
        if (run.Bold) output.Append(@"\b");
        if (run.Italic) output.Append(@"\i");
        if (run.Underline) output.Append(@"\ul");
        if (run.Strikethrough) output.Append(@"\strike");
        if (run.Caps == RunTextCaps.All) output.Append(@"\caps");
        if (run.Caps == RunTextCaps.Small) output.Append(@"\scaps");
        if (run.RightToLeft == true) output.Append(@"\rtlch");
        else if (run.RightToLeft == false) output.Append(@"\ltrch");
        if (run.BaselineOffset is { } baselineOffset and not 0)
        {
            // The model stores the DrawingML-style thousandths-of-a-percent
            // offset that the reader derives from RTF half-points. Preserve
            // that authored value instead of collapsing it to RTF's coarse
            // \super/\sub defaults.
            var fontSizePt = run.FontSizePt is > 0 ? run.FontSizePt.Value : 12.0;
            var halfPoints = (int)Math.Clamp(
                Math.Round(Math.Abs(baselineOffset) * fontSizePt / 50_000.0),
                1,
                32_760);
            output.Append(baselineOffset > 0 ? @"\up" : @"\dn").Append(halfPoints);
        }
        output.Append(' ');

        if (run.Hyperlink?.Url is { Length: > 0 } url)
        {
            output.Append(@"{\field{\*\fldinst HYPERLINK """);
            AppendFieldInstruction(output, url);
            output.Append(@"""}{\fldrslt ");
            AppendText(output, run.Text);
            output.Append("}}");
        }
        else
        {
            AppendText(output, run.Text);
        }
    }

    private static void AppendFieldInstruction(StringBuilder output, string value)
    {
        foreach (var character in value)
        {
            if (character is '\\' or '"')
                output.Append('\\');
            AppendAsciiOrUnicode(output, character);
        }
    }

    private static void AppendText(StringBuilder output, string value)
    {
        foreach (var character in value)
            AppendAsciiOrUnicode(output, character);
    }

    private static void AppendAsciiOrUnicode(StringBuilder output, char character)
    {
        switch (character)
        {
            case '\\': output.Append(@"\\"); break;
            case '{': output.Append(@"\{"); break;
            case '}': output.Append(@"\}"); break;
            case '\n': output.Append(@"\line "); break;
            case '\r': break;
            case '\t': output.Append(@"\tab "); break;
            default:
                if (character is >= (char)0x20 and <= (char)0x7E)
                    output.Append(character);
                else
                    output.Append(@"\u").Append((short)character).Append('?');
                break;
        }
    }

    private static int ToTwips(long emu) =>
        (int)Math.Clamp(Math.Round(emu / (double)EmuPerPoint * 20), -100_000, 100_000);

    private static int ToTwips(double points) =>
        (int)Math.Clamp(Math.Round(points * 20), -100_000, 100_000);
}
