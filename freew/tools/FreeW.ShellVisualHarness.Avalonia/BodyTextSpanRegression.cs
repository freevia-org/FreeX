using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using Free.Shared.Drawing;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Proofing;
using FreeW.Core.Model;
using DrawingGroup = Avalonia.Media.DrawingGroup;

// Durable real-Skia lane. The ordinary xUnit host intentionally uses dummy headless drawing;
// this existing visual harness uses UseHeadlessDrawing=false and a separate UI process.
internal static class BodyTextSpanRegression
{
    internal static async Task<int> RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        var failures = new List<string>();
        var results = new List<object>();
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ShellCaptureApp).Assembly);
        await session.Dispatch(() =>
        {
            var format = RunFormatting.Default with { FontFamily = "Arial", FontSizePt = 24, ColorHex = "#172554" };
            var cases = new (string Id, string[] Pieces)[]
            {
                ("emoji", ["a😀b"]), ("combining", ["ae\u0301b"]), ("zwj", ["a👩‍💻b"]),
                ("emoji-split", ["a\uD83D", "\uDE00b"]), ("empty-style", ["a\uD83D", "", "\uDE00b"]), ("combining-split", ["ae", "\u0301b"]),
                ("zwj-split", ["a👩", "\u200d", "💻b"]),
                ("latin-context", ["AV😀To"]), ("soft-breaks", ["a😀\n\nb\n"]),
                ("crlf", ["a😀\r\n\r\nb\r\n"]), ("styled", ["a", "😀", "b"]),
                ("hyperlink-default-emoji", ["a😀b"]), ("hyperlink-combining", ["ae\u035Cb"]), ("hyperlink-zwj", ["a👩‍💻b"]),
                ("hyperlink-split", ["a\uD83D", "\uDE00b"]), ("hyperlink-mixed", ["a", "😀", "b"]),
                ("hyperlink-adjacent", ["a", "😀", "e\u035C", "👩‍💻", "b"]),
                ("comment-emoji", ["a😀b"]), ("comment-zwj", ["a👩‍💻b"]),
                ("comment-resolved", ["ae\u035Cb"]), ("comment-hidden", ["a😀b"]), ("hyperlink-comment", ["a😀b"]),
            };
            foreach (var test in cases)
                foreach (var width in new[] { 416d, 60d })
                {
                    var id = test.Id + "-" + width.ToString(CultureInfo.InvariantCulture);
                    var document = TextDocument.CreateEmpty(); document.Blocks.Clear();
                    var paragraph = new Paragraph();
                    for (var i = 0; i < test.Pieces.Length; i++)
                        paragraph.Runs.Add(new Run(test.Pieces[i], test.Id == "hyperlink-default-emoji" ? format with { ColorHex = null }
                            : test.Id == "empty-style" && i == 1
                            ? format with { Bold = true, ColorHex = "#9b2226" }
                            : test.Id == "styled" && i > 0
                            ? format with { ColorHex = i == 1 ? "#226644" : "#9b2226", Bold = i == 2 } : format));
                    var originals = paragraph.Runs.ToArray();
                    document.Blocks.Add(paragraph);
                    document.Page.WidthPt = width == 416 ? 360 : 240;
                    document.Page.HeightPt = 300;
                    document.Page.MarginLeftPt = document.Page.MarginRightPt = width == 416 ? 24 : 75;
                    document.Page.MarginTopPt = document.Page.MarginBottomPt = 24;
                    paragraph.Formatting = paragraph.Formatting with { IndentLeftPt = width == 416 ? 0 : 45 };
                    if (test.Id.StartsWith("hyperlink", StringComparison.Ordinal) || test.Id.StartsWith("comment", StringComparison.Ordinal))
                        paragraph.Formatting = paragraph.Formatting with { LineRule = LineSpacingRule.Exact, LineHeightPt = 40 };
                    // Small-page content is floored at 120 DIP; the authored 60 DIP indent
                    // gives the actual 60 DIP narrow line, without altering Unicode advances.
                    var constructor = typeof(DocumentView).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                        null, [typeof(CustomDictionaryStore)], null)!;
                    var view = (DocumentView)constructor.Invoke([new CustomDictionaryStore(null)]);
                    view.LoadDocument(document);
                    var window = new Window { Width = 700, Height = 500, Content = view };
                    try
                    {
                        window.Show(); view.Focus(); view.Measure(new Size(700, 500)); view.Arrange(new Rect(0, 0, 700, 500));
                        int? commentId = null; var commentStart = 0; var commentEnd = 0;
                        if (test.Id.StartsWith("hyperlink", StringComparison.Ordinal))
                        {
                            if (test.Id is "hyperlink-split" or "hyperlink-adjacent")
                            {
                                for (var i = 0; i < paragraph.Runs.Count; i++)
                                {
                                    if (test.Id == "hyperlink-adjacent" && (i == 0 || i == paragraph.Runs.Count - 1)) continue;
                                    paragraph.Runs[i].HyperlinkUrl = test.Id == "hyperlink-adjacent" && i == 3 ? null : "https://example.invalid/" + (test.Id == "hyperlink-adjacent" ? i : 0);
                                    paragraph.Runs[i].HyperlinkAnchor = test.Id == "hyperlink-adjacent" && i == 3 ? "SyntheticTarget" : null;
                                    paragraph.Runs[i].HyperlinkTooltip = "Synthetic target " + (test.Id == "hyperlink-adjacent" ? i : 0);
                                }
                                view.InvalidateAfterExternalMutation();
                            }
                            else
                            {
                                var stops = StringInfo.ParseCombiningCharacters(paragraph.PlainText).Append(paragraph.PlainText.Length).ToArray();
                                var start = test.Id == "hyperlink-mixed" ? stops[1] : 0;
                                var end = test.Id == "hyperlink-mixed" ? stops[2] : paragraph.PlainText.Length;
                                SelectRange(view, start, end);
                                view.InsertHyperlink(paragraph.PlainText[start..end], "https://example.invalid");
                            }
                        }
                        if (test.Id.StartsWith("comment", StringComparison.Ordinal) || test.Id == "hyperlink-comment")
                        {
                            var stops = StringInfo.ParseCombiningCharacters(paragraph.PlainText).Append(paragraph.PlainText.Length).ToArray();
                            commentStart = stops[1]; commentEnd = stops[2];
                            SelectRange(view, commentStart, commentEnd);
                            commentId = view.AddComment("Synthetic note", "Reviewer", "R");
                            Check(commentId is not null, id + ": public comment did not create a thread");
                            if (test.Id == "comment-resolved" && commentId is { } resolved) view.SetCommentResolved(resolved, true);
                            if (test.Id == "comment-hidden") view.ApplyShowMarkupComments(false);
                        }
                        if (test.Id.StartsWith("hyperlink", StringComparison.Ordinal) || test.Id.StartsWith("comment", StringComparison.Ordinal))
                            originals = paragraph.Runs.ToArray(); // Public metadata edits legitimately replace source runs before rendering.
                        Check(paragraph.PlainText == string.Concat(test.Pieces), id + ": public metadata edit changed the input text");
                        view.Measure(new Size(700, 500)); view.Arrange(new Rect(0, 0, 700, 500));
                        var text = paragraph.PlainText;
                        var boundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
                        var drawing = new DrawingGroup(); using (var context = drawing.Open()) view.Render(context);
                        var painted = Flatten(drawing).OfType<GlyphRunDrawing>().Where(glyph => glyph.Foreground is ISolidColorBrush brush
                            && paragraph.Runs.Any(run => run.Text.Length > 0 && DisplayColor(run) == brush.Color)).ToArray();
                        var glyphs = painted.Select(glyph => glyph.GlyphRun!).ToArray();
                        Check(glyphs.All(glyph => IsValid(glyph.Characters.Span.ToString())), id + ": isolated surrogate sent to painted glyph run");
                        var styles = new List<ValueSpan<TextRunProperties>>(); var offset = 0;
                        RunFormatting? previousStyle = null;
                        foreach (var run in paragraph.Runs)
                        {
                            if (run.Text.Length == 0) continue;
                            var linked = run.HyperlinkUrl is { Length: > 0 } || run.HyperlinkAnchor is { Length: > 0 };
                            var f = run.Formatting with { ColorHex = DisplayColor(run).ToString(), Underline = run.Formatting.Underline || linked };
                            var properties = new GenericTextRunProperties(new Typeface(DocumentFontFamilyResolver.Resolve(f.FontFamily),
                                f.Italic ? FontStyle.Italic : FontStyle.Normal, f.Bold ? FontWeight.Bold : FontWeight.Normal),
                                (f.FontSizePt ?? 11) * 4 / 3, textDecorations: f.Underline ? TextDecorations.Underline : null,
                                foregroundBrush: Brush.Parse(f.ColorHex!));
                            if (previousStyle == f && styles.Count > 0)
                                styles[^1] = new(styles[^1].Start, styles[^1].Length + run.Text.Length, styles[^1].Value);
                            else styles.Add(new(offset, run.Text.Length, properties));
                            offset += run.Text.Length; previousStyle = f;
                        }
                        // Ordinary constructor is independent of the product's source-aware line
                        // helper. Explicit control cases use safe TextEndOfLine below, retaining
                        // raw CRLF length rather than passing no-glyph strings to the shaper.
                        using var reference = text.Any(IsBreak)
                            ? new TextLayout(new SafeSource(text, styles), new GenericTextParagraphProperties(styles[0].Value,
                                textWrapping: TextWrapping.Wrap), maxWidth: width, maxLines: 32)
                            : new TextLayout(text, styles[0].Value.Typeface, styles[0].Value.FontRenderingEmSize,
                                styles[0].Value.ForegroundBrush, textWrapping: TextWrapping.Wrap,
                                maxWidth: width, maxLines: 32, textStyleOverrides: styles);
                        var expected = reference.TextLines.SelectMany(line => line.TextRuns.OfType<ShapedTextRun>())
                            .Select(run => Signature(run.GlyphRun)).ToArray();
                        var actual = glyphs.Select(Signature).ToArray();
                        Check(actual.SequenceEqual(expected), id + ": actual painted glyphs differ from independent whole-span reference");
                        var expectedColors = reference.TextLines.SelectMany(line => line.TextRuns.OfType<ShapedTextRun>())
                            .Select(run => (run.Properties.ForegroundBrush as ISolidColorBrush)?.Color).ToArray();
                        Check(painted.Select(glyph => (glyph.Foreground as ISolidColorBrush)?.Color).SequenceEqual(expectedColors),
                            id + ": painted glyph colours differ from independent display-style reference");
                        var referenceDrawing = new DrawingGroup(); using (var context = referenceDrawing.Open()) reference.Draw(context, new Point());
                        var expectedUnderlines = PaintLines(referenceDrawing, paragraph.Runs.Where(run => run.Text.Length > 0).Select(DisplayColor).ToArray()).ToArray();
                        var actualUnderlines = PaintLines(drawing, paragraph.Runs.Where(run => run.Text.Length > 0).Select(DisplayColor).ToArray()).ToArray();
                        if (test.Id.StartsWith("hyperlink", StringComparison.Ordinal)) Check(expectedUnderlines.Length > 0, id + ": underline reference did not paint any lines");
                        Check(actualUnderlines.Length == expectedUnderlines.Length, id + ": hyperlink underline fragments differ from independent reference");
                        for (var underline = 0; underline < Math.Min(actualUnderlines.Length, expectedUnderlines.Length); underline++)
                            Check(Math.Abs(actualUnderlines[underline].Width - expectedUnderlines[underline].Width) < 0.01
                                && actualUnderlines[underline].Color == expectedUnderlines[underline].Color,
                                id + ": hyperlink underline width/colour differs from independent reference");
                        if (commentId is not null)
                        {
                            var tintColor = test.Id == "comment-resolved" ? Color.FromArgb(0x1F, 0x9E, 0x9E, 0x9E) : Color.FromArgb(0x33, 0xFF, 0xC1, 0x07);
                            var tintBounds = Flatten(drawing).OfType<GeometryDrawing>()
                                .Where(item => item.Brush is ISolidColorBrush brush && brush.Color == tintColor).Select(item => item.Geometry!.Bounds).ToArray();
                            var expectedTint = reference.TextLines.SelectMany(line =>
                            {
                                var start = Math.Max(commentStart, line.FirstTextSourceIndex);
                                var end = Math.Min(commentEnd, line.FirstTextSourceIndex + line.Length);
                                return end > start && test.Id != "comment-hidden" ? line.GetTextBounds(start, end - start) : [];
                            }).Where(bounds => bounds.Rectangle.Width > 0).ToArray();
                            Check(tintBounds.Length == expectedTint.Length, id + ": comment tint fragments differ from independent shaped range");
                            for (var tint = 0; tint < Math.Min(tintBounds.Length, expectedTint.Length); tint++)
                                Check(Math.Abs(tintBounds[tint].Width - expectedTint[tint].Rectangle.Width) < 0.01,
                                    id + ": comment tint width differs from independent shaped range");
                        }
                        for (var frame = 0; frame < 3; frame++)
                        {
                            // Allocate unrelated formatting/ruler glyphs between paints. Narrow
                            // wrapped runs must retain their original pooled shaping buffers.
                            using var noise = new TextLayout("12345 AV To", new Typeface("Arial"), 9 + frame);
                            _ = noise.Width;
                            var repeat = new DrawingGroup(); using (var context = repeat.Open()) view.Render(context);
                            var signature = Flatten(repeat).OfType<GlyphRunDrawing>().Where(glyph => glyph.Foreground is ISolidColorBrush brush
                                && paragraph.Runs.Any(run => run.Text.Length > 0 && DisplayColor(run) == brush.Color)).Select(glyph => Signature(glyph.GlyphRun!));
                            Check(signature.SequenceEqual(expected), id + ": retained shaped buffers changed after unrelated formatting");
                        }
                        Send(view, Key.Home, KeyModifiers.Control);
                        var x = view.CaretLeft; var y = view.CaretTop;
                        var line = reference.TextLines[0];
                        var illegal = new List<int>();
                        var hit = typeof(DocumentView).GetMethod("TryHitTest", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        for (var sample = 0; sample <= 32; sample++)
                        {
                            object?[] arguments = [new Point(x + line.WidthIncludingTrailingWhitespace * sample / 32, y + line.Height / 2), null];
                            if ((bool)hit.Invoke(view, arguments)!)
                            {
                                var value = (int)arguments[1]!.GetType().GetProperty("Offset")!.GetValue(arguments[1])!;
                                if (!boundaries.Contains(value)) illegal.Add(value);
                            }
                            else Check(false, id + ": actual first-line pointer was not handled");
                        }
                        Check(illegal.Count == 0, id + ": pointer returned non-grapheme UTF16 offsets: " + string.Join(',', illegal));
                        for (var index = 1; index < boundaries.Length; index++) Send(view, Key.Right);
                        if (reference.TextLines.Count == 1)
                            Check(Math.Abs((view.CaretLeft - x) - reference.WidthIncludingTrailingWhitespace) < 0.01,
                                id + ": end caret does not follow rendered whole-span advance");
                        Send(view, Key.Home, KeyModifiers.Control); Send(view, Key.Right); Send(view, Key.Right, KeyModifiers.Shift);
                        Check(view.SelectedText == text[boundaries[1]..boundaries[2]], id + ": selected UTF16 text changed");
                        var selectedDrawing = new DrawingGroup();
                        using (var context = selectedDrawing.Open()) view.Render(context);
                        var selectedBounds = Flatten(selectedDrawing).OfType<GeometryDrawing>()
                            .Where(item => item.Brush is ISolidColorBrush brush && brush.Color == Color.FromArgb(0x55, 0x33, 0x99, 0xFF))
                            .Select(item => item.Geometry!.Bounds).Where(bounds => bounds.Width > 0).ToArray();
                        var referenceBounds = reference.TextLines.SelectMany(item =>
                        {
                            var selectedStart = Math.Max(boundaries[1], item.FirstTextSourceIndex);
                            var selectedEnd = Math.Min(boundaries[2], item.FirstTextSourceIndex + item.Length);
                            return selectedEnd > selectedStart ? item.GetTextBounds(selectedStart, selectedEnd - selectedStart) : [];
                        }).Select(item => item.Rectangle).Where(bounds => bounds.Width > 0).ToArray();
                        Check(selectedBounds.Length == referenceBounds.Length, id + ": actual selection fragments differ from shaped reference");
                        for (var selectedIndex = 0; selectedIndex < Math.Min(selectedBounds.Length, referenceBounds.Length); selectedIndex++)
                            Check(Math.Abs(selectedBounds[selectedIndex].Width - referenceBounds[selectedIndex].Width) < 0.01
                                && Math.Abs(selectedBounds[selectedIndex].X - x - referenceBounds[selectedIndex].X) < 0.01,
                                id + ": selection geometry differs from rendered shaped span");
                        var exported = view.BuildPdfContent();
                        var pdfPieces = exported.Pages.SelectMany(page => page.Ops).OfType<Free.Shared.Pdf.PdfText>().Select(op => op.Text).ToArray();
                        if (test.Id.StartsWith("hyperlink", StringComparison.Ordinal) || test.Id.StartsWith("comment", StringComparison.Ordinal))
                            VerifyMetadataPdfLinks(id, test.Id, string.Concat(test.Pieces), reference, document, exported, Check);
                        var pdfText = string.Concat(pdfPieces);
                        Check(pdfPieces.All(IsValid), id + ": individual PDF text primitive split a Unicode scalar");
                        Check(IsValid(pdfText), id + ": PDF adapter emitted isolated surrogate text");
                        Check(pdfText == string.Concat(text.Where(character => !IsBreak(character))), id + ": PDF adapter lost logical text");
                        Check(paragraph.Runs.SequenceEqual(originals) && paragraph.PlainText == text, id + ": layout changed source run identities/text");
                        using (var bitmap = new RenderTargetBitmap(new PixelSize(700, 500), new Vector(96, 96)))
                        { bitmap.Render(view); bitmap.Save(Path.Combine(output, id + "-actual.png")); }
                        using (var bitmap = new RenderTargetBitmap(new PixelSize(700, 500), new Vector(96, 96)))
                        {
                            var exactAdvance = test.Id.StartsWith("hyperlink", StringComparison.Ordinal) || test.Id.StartsWith("comment", StringComparison.Ordinal)
                                ? 40d * 4 / 3 : (double?)null;
                            var control = new ReferenceControl(reference, exactAdvance) { Width = 700, Height = 500 };
                            control.Measure(new Size(700, 500)); control.Arrange(new Rect(0, 0, 700, 500));
                            bitmap.Render(control); bitmap.Save(Path.Combine(output, id + "-reference.png"));
                        }
                        results.Add(new { Id = id, Utf16 = string.Join(' ', text.Select(character => ((int)character).ToString("X4"))), Boundaries = boundaries,
                            ActualGlyphs = actual, ReferenceGlyphs = expected, IllegalHits = illegal, ActualEndX = view.CaretLeft,
                            ReferenceWidth = reference.WidthIncludingTrailingWhitespace, ReferenceLineCount = reference.TextLines.Count });
                    }
                    finally { window.Close(); }
                }
            // Tracking oracle starts from independently shaped, UNTRACKED complete text. It
            // compares retained ink positions within each raw-source grapheme and the inter-element
            // advance separately; the framework's per-glyph LetterSpacing is not the oracle.
            foreach (var sample in new[] { ("emoji", "a😀b"), ("noncomposed", "ae\u035Cb"), ("zwj", "a👩‍💻b"), ("linked-emoji", "a😀b") })
                foreach (var spacing in new[] { 2d, -2d, -100d })
                    foreach (var width in new[] { 416d, 60d })
                    {
                        var id = "tracked-" + sample.Item1 + "-" + spacing.ToString(CultureInfo.InvariantCulture) + "-" + width;
                        var text = sample.Item2;
                        using var reference = new TextLayout(text, new Typeface(DocumentFontFamilyResolver.Resolve(format.FontFamily)), 32,
                            Brush.Parse(format.ColorHex!), textWrapping: TextWrapping.NoWrap);
                        var expected = GlyphEntries(reference.TextLines.SelectMany(line => line.TextRuns.OfType<ShapedTextRun>()).Select(run => run.GlyphRun)).ToArray();
                        var paragraph = new Paragraph(); paragraph.Runs.Add(new Run(text, format with { CharacterSpacingPt = spacing }));
                        if (sample.Item1 == "linked-emoji") paragraph.Runs[0].HyperlinkUrl = "https://tracked.invalid";
                        var source = paragraph.Runs[0];
                        var document = TextDocument.CreateEmpty(); document.Blocks.Clear(); document.Blocks.Add(paragraph);
                        document.Page.WidthPt = width == 416 ? 360 : 240; document.Page.HeightPt = 300;
                        document.Page.MarginLeftPt = document.Page.MarginRightPt = width == 416 ? 24 : 75;
                        document.Page.MarginTopPt = document.Page.MarginBottomPt = 24;
                        paragraph.Formatting = paragraph.Formatting with { IndentLeftPt = width == 416 ? 0 : 45 };
                        var constructor = typeof(DocumentView).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                            null, [typeof(CustomDictionaryStore)], null)!;
                        var view = (DocumentView)constructor.Invoke([new CustomDictionaryStore(null)]);
                        view.LoadDocument(document);
                        var window = new Window { Width = 700, Height = 500, Content = view };
                        try
                        {
                            window.Show(); view.Focus(); view.Measure(new Size(700, 500)); view.Arrange(new Rect(0, 0, 700, 500));
                            var drawing = new DrawingGroup(); using (var context = drawing.Open()) view.Render(context);
                            var glyphRuns = Flatten(drawing).OfType<GlyphRunDrawing>()
                                .Where(item => item.Foreground is ISolidColorBrush brush && brush.Color == Color.Parse(format.ColorHex!))
                                .Select(item => item.GlyphRun!).ToArray();
                            var actual = GlyphEntries(glyphRuns).ToArray();
                            Check(actual.All(item => item.Advance >= 0), id + ": condensation created negative glyph advance");
                            Check(glyphRuns.All(run => IsValid(run.Characters.Span.ToString())), id + ": tracked painter submitted surrogate halves");
                            Check(actual.Select(item => (item.Font, item.Glyph)).SequenceEqual(expected.Select(item => (item.Font, item.Glyph))),
                                id + ": tracking changed contextual/fallback glyph choices");
                            var stops = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
                            double added = 0;
                            for (var index = 0; index + 1 < stops.Length; index++)
                            {
                                var start = stops[index]; var end = stops[index + 1];
                                var normalElement = expected.Where(item => item.Source >= start && item.Source < end).ToArray();
                                var trackedElement = actual.Where(item => item.Source >= start && item.Source < end).ToArray();
                                Check(normalElement.Length == trackedElement.Length, id + ": source element glyph membership changed");
                                if (width == 416 && normalElement.Length > 0 && normalElement.Length == trackedElement.Length)
                                {
                                    for (var glyph = 0; glyph < normalElement.Length; glyph++)
                                        Check(Math.Abs(trackedElement[glyph].X - normalElement[glyph].X - added) < 0.01
                                            && Math.Abs(trackedElement[glyph].Y - normalElement[glyph].Y) < 0.01,
                                            id + ": tracking moved ink inside a grapheme (including zero-advance mark)");
                                }
                                var delta = FreeW.App.Presentation.DocumentView.RunCharacterSpacingPlanner.IsSpacedElement(text[start..end])
                                    ? Math.Max(-normalElement.Sum(item => item.Advance), spacing * 4 / 3) : 0;
                                added += delta;
                            }
                            if (sample.Item1 == "noncomposed")
                                Check(expected.Any(item => item.Source >= 1 && item.Source < 3 && Math.Abs(item.Advance) < 0.00001),
                                    id + ": reference must exercise a real noncomposed zero-advance mark");
                            Send(view, Key.Home, KeyModifiers.Control);
                            var x = view.CaretLeft; var y = view.CaretTop;
                            var hit = typeof(DocumentView).GetMethod("TryHitTest", BindingFlags.Instance | BindingFlags.NonPublic)!;
                            for (var fraction = 0; fraction <= 32; fraction++)
                            {
                                object?[] arguments = [new Point(x + width * fraction / 32, y + reference.TextLines[0].Height / 2), null];
                                Check((bool)hit.Invoke(view, arguments)!, id + ": tracked pointer not handled");
                                var offset = (int)arguments[1]!.GetType().GetProperty("Offset")!.GetValue(arguments[1])!;
                                Check(stops.Contains(offset), id + ": tracked pointer returned an internal grapheme offset");
                            }
                            if (width == 416)
                            {
                                Send(view, Key.End, KeyModifiers.Control);
                                Check(Math.Abs(view.CaretLeft - x - reference.WidthIncludingTrailingWhitespace - added) < 0.01,
                                    id + ": tracking total advance is not once per spaced text element");
                            }
                            var pdf = view.BuildPdfContent().Pages.SelectMany(page => page.Ops).OfType<Free.Shared.Pdf.PdfText>().ToArray();
                            var elements = stops.Zip(stops.Skip(1), (start, end) => text[start..end]).ToArray();
                            Check(pdf.Select(item => item.Text).SequenceEqual(elements), id + ": tracked PDF did not keep complete source graphemes");
                            Send(view, Key.Home, KeyModifiers.Control); var firstX = view.CaretLeft;
                            for (var index = 0; index < pdf.Length && index < stops.Length - 1; index++)
                            {
                                if (index > 0) Send(view, Key.Right);
                                Check(Math.Abs((pdf[index].X - pdf[0].X) * 4 / 3 - (view.CaretLeft - firstX)) < 0.01,
                                    id + ": tracked PDF X differs from actual backend insertion geometry");
                                var family = actual.FirstOrDefault(item => item.Source >= stops[index] && item.Source < stops[index + 1])?.Font;
                                Check(pdf[index].FontFamily == family, id + ": tracked PDF lost resolved fallback-family provenance");
                            }
                            Check(paragraph.Runs.Count == 1 && ReferenceEquals(paragraph.Runs[0], source) && paragraph.PlainText == text,
                                id + ": tracking mutated source identity/UTF16");
                            using var image = new RenderTargetBitmap(new PixelSize(700, 500), new Vector(96, 96));
                            image.Render(view); image.Save(Path.Combine(output, id + "-actual.png"));
                            results.Add(new { Id = id, SourceRanges = stops.Zip(stops.Skip(1), (start, end) => new { Start = start, End = end }),
                                TrackingPt = spacing, NormalGlyphs = expected, ActualGlyphs = actual, AddedAdvance = added });
                        }
                        finally { window.Close(); }
                    }
        }, CancellationToken.None);
        var application = typeof(DocumentView).Assembly;
        File.WriteAllText(Path.Combine(output, "text-span-regression.json"), JsonSerializer.Serialize(new { Result = failures.Count == 0 ? "passed" : "failed",
            ApplicationAssembly = new { Path = application.Location, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(application.Location))).ToLowerInvariant(),
                InformationalVersion = application.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion },
            Renderer = "Avalonia.Skia; UseHeadlessDrawing=false", Cases = results, Failures = failures,
            PdfQualification = "Logical Unicode text and independently referenced soft-metadata annotation geometry/style checked; shared PDF complex shaping is not covered." }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var failure in failures) Console.Error.WriteLine(failure);
        Console.WriteLine($"Real Skia text-span cases: {results.Count}; failures: {failures.Count}");
        return failures.Count == 0 ? 0 : 1;

        void Check(bool value, string message) { if (!value) failures.Add(message); }
    }

    private sealed record GlyphEntry(int Source, string Font, ushort Glyph, double Advance, double X, double Y);

    private static void VerifyMetadataPdfLinks(string id, string fixture, string text, TextLayout reference,
        TextDocument document, Free.Shared.Pdf.PdfContentDocument exported, Action<bool, string> check)
    {
        var stops = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        var ranges = new List<(int Start, int End, string? Uri, string? Destination, string? Tooltip)>();
        if (fixture == "hyperlink-adjacent")
        {
            ranges.Add((1, 3, "https://example.invalid/1", null, "Synthetic target 1"));
            ranges.Add((3, 5, "https://example.invalid/2", null, "Synthetic target 2"));
            ranges.Add((5, 10, null, "SyntheticTarget", "Synthetic target 3"));
        }
        else if (fixture == "hyperlink-split") ranges.Add((0, text.Length, "https://example.invalid/0", null, "Synthetic target 0"));
        else if (fixture == "hyperlink-mixed") ranges.Add((stops[1], stops[2], "https://example.invalid", null, null));
        else if (fixture.StartsWith("hyperlink", StringComparison.Ordinal)) ranges.Add((0, text.Length, "https://example.invalid", null, null));
        var expected = new List<Free.Shared.Pdf.PdfLinkOverlay>();
        for (var lineIndex = 0; lineIndex < reference.TextLines.Count; lineIndex++)
        {
            var line = reference.TextLines[lineIndex];
            foreach (var range in ranges)
            {
                var start = Math.Max(range.Start, line.FirstTextSourceIndex);
                var end = Math.Min(range.End, Math.Min(text.Length, line.FirstTextSourceIndex + line.Length));
                while (end > start && IsBreak(text[end - 1])) end--;
                if (end <= start) continue;
                // The complete independent layout supplies glyph/fallback geometry. Query only
                // the source interval owned by this line, never an out-of-line metadata range.
                var bounds = line.GetTextBounds(start, end - start).Select(item => item.Rectangle).Where(rect => rect.Width > 0).ToArray();
                if (bounds.Length == 0) continue;
                var left = bounds.Min(rect => rect.Left); var right = bounds.Max(rect => rect.Right);
                var leftHit = line.GetDistanceFromCharacterHit(new CharacterHit(start));
                var previousStop = stops.Last(stop => stop < end);
                var rightHit = line.GetDistanceFromCharacterHit(new CharacterHit(previousStop, end - previousStop));
                check(Math.Abs(left - leftHit) < 0.01 && Math.Abs(right - rightHit) < 0.01,
                    id + ": independent bounds include text outside the legal semantic interval");
                var paragraph = (Paragraph)document.Blocks[0];
                // New metadata fixtures author exact 40pt spacing. Page margins/indent and
                // reference line ordinal determine the transform without product caret helpers.
                expected.Add(new Free.Shared.Pdf.PdfLinkOverlay(document.Page.MarginLeftPt + paragraph.Formatting.IndentLeftPt + left * 3 / 4,
                    document.Page.MarginTopPt + lineIndex * 40, (right - left) * 3 / 4, 40,
                    range.Uri, range.Tooltip, range.Destination));
            }
        }
        var actual = exported.Pages.SelectMany(page => page.LinkOverlays ?? []).ToArray();
        check(actual.Length == expected.Count, id + ": PDF semantic link fragment count differs from authored/reference ranges");
        for (var index = 0; index < Math.Min(actual.Length, expected.Count); index++)
        {
            var observed = actual[index]; var target = expected[index];
            check(observed.Uri == target.Uri && observed.DestinationName == target.DestinationName && observed.Tooltip == target.Tooltip,
                id + ": PDF target or ScreenTip differs from authored semantic range");
            check(Math.Abs(observed.X - target.X) < 0.01 && Math.Abs(observed.Y - target.Y) < 0.01
                && Math.Abs(observed.Width - target.Width) < 0.01 && Math.Abs(observed.Height - target.Height) < 0.01,
                id + ": PDF annotation rectangle differs from independent layout/model transform");
        }
    }

    private static Color DisplayColor(Run run) => Color.Parse(string.IsNullOrWhiteSpace(run.Formatting.ColorHex)
        ? run.HyperlinkUrl is { Length: > 0 } || run.HyperlinkAnchor is { Length: > 0 } ? "#0563C1" : "#000000"
        : run.Formatting.ColorHex);

    private static IEnumerable<(double Width, Color Color)> PaintLines(DrawingGroup drawing, Color[] colors) =>
        Flatten(drawing).OfType<GeometryDrawing>()
            .Where(item => item.Pen?.Brush is ISolidColorBrush brush && colors.Contains(brush.Color) && item.Geometry is not null)
            .Select(item => (item.Geometry!.Bounds.Width, ((ISolidColorBrush)item.Pen!.Brush!).Color));

    private static void SelectRange(DocumentView view, int start, int end)
    {
        // Production keyboard/public surfaces also exist in the published pre-fix DLL;
        // conditional host-test selection helpers do not belong in this durable lane.
        var text = ((Paragraph)view.Document.Blocks[0]).PlainText;
        var stops = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        if (!stops.Contains(start) || !stops.Contains(end) || end < start)
            throw new ArgumentException("The synthetic metadata range must use legal grapheme stops.");
        Send(view, Key.Home, KeyModifiers.Control);
        foreach (var _ in stops.Where(stop => stop > 0 && stop <= start)) Send(view, Key.Right);
        foreach (var _ in stops.Where(stop => stop > start && stop <= end)) Send(view, Key.Right, KeyModifiers.Shift);
        if (view.SelectedText != text[start..end])
            throw new InvalidOperationException("Production keyboard did not establish the synthetic metadata range.");
    }
    private static IEnumerable<GlyphEntry> GlyphEntries(IEnumerable<GlyphRun> runs)
    {
        double x = 0; var source = 0;
        foreach (var run in runs)
        {
            var origin = run.GlyphInfos.Count > 0 ? run.GlyphInfos[0].GlyphCluster : 0;
            foreach (var glyph in run.GlyphInfos)
            {
                yield return new GlyphEntry(source + glyph.GlyphCluster - origin, run.GlyphTypeface.FamilyName,
                    glyph.GlyphIndex, glyph.GlyphAdvance, x + glyph.GlyphOffset.X, glyph.GlyphOffset.Y);
                x += glyph.GlyphAdvance;
            }
            source += run.Characters.Length;
        }
    }

    private sealed class ReferenceControl(TextLayout layout, double? exactAdvance = null) : Control
    {
        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Brushes.White, new Rect(0, 0, 700, 500));
            if (exactAdvance is { } advance)
                for (var index = 0; index < layout.TextLines.Count; index++)
                    layout.TextLines[index].Draw(context, new Point(20, 20 + index * advance));
            else layout.Draw(context, new Point(20, 20));
        }
    }

    private static string Signature(GlyphRun glyph) => glyph.Characters.Span.ToString() + "|" + glyph.GlyphTypeface.FamilyName + "|"
        + string.Join(';', glyph.GlyphInfos.Select(info => $"{info.GlyphIndex}:{info.GlyphCluster}:{info.GlyphAdvance:R}"));
    private static void Send(DocumentView view, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
    private static IEnumerable<Drawing> Flatten(DrawingGroup group) => group.Children.SelectMany(drawing =>
        drawing is DrawingGroup child ? Flatten(child) : [drawing]);
    private static bool IsValid(string text)
    {
        for (var index = 0; index < text.Length; index++)
            if (char.IsHighSurrogate(text[index])) { if (++index >= text.Length || !char.IsLowSurrogate(text[index])) return false; }
            else if (char.IsLowSurrogate(text[index])) return false;
        return true;
    }
    private static bool IsBreak(char character) => character is '\r' or '\n' or '\v' or '\u2028' or '\u2029';
    private sealed class SafeSource(string text, IReadOnlyList<ValueSpan<TextRunProperties>> spans) : ITextSource
    {
        public TextRun? GetTextRun(int index)
        {
            if (index >= text.Length) return index == text.Length && IsBreak(text[^1]) ? new TextEndOfParagraph(1) : null;
            var span = spans.First(value => index >= value.Start && index < value.Start + value.Length);
            if (IsBreak(text[index])) return new End(text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1, span.Value);
            var end = index + 1; while (end < span.Start + span.Length && !IsBreak(text[end])) end++;
            return new TextCharacters(text.AsMemory(index, end - index), span.Value);
        }
        private sealed class End(int length, TextRunProperties properties) : TextEndOfLine(length)
        { public override TextRunProperties Properties => properties; }
    }
}
