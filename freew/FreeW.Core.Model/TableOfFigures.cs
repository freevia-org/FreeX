namespace FreeW.Core.Model;

/// <summary>
/// Pure, WPF-free generation of a document Table of Figures (or Table of Tables) from its captions
/// (see <see cref="Captions"/>). Lives in the model project so it is unit-testable without any UI,
/// mirroring <see cref="TableOfContents"/> and <see cref="DocumentIndex"/>.
/// <para>
/// <see cref="Build"/> produces a heading followed by one styled result paragraph per caption of the
/// requested <see cref="CaptionLabel"/>. The result paragraphs are owned by one native Word
/// <c>TOC \c "Label"</c> spanning field so Word can update their text and page references. The
/// dedicated style ids (<see cref="HeadingStyleId"/> and <see cref="EntryStyleId"/>) also let them:
/// </para>
/// <list type="bullet">
/// <item>render with distinct formatting once <see cref="EnsureStyles"/> has registered them;</item>
/// <item>round-trip through docx with their native field ownership intact; and</item>
/// <item>act as a marker so a "refresh" can locate and replace a previously inserted region via
/// <see cref="IsTableOfFiguresParagraph"/>.</item>
/// </list>
/// </summary>
public static class TableOfFigures
{
    /// <summary>Style id of the table-of-figures heading paragraph.</summary>
    public const string HeadingStyleId = "TableOfFiguresHeading";

    /// <summary>Style id carried by each generated table-of-figures entry paragraph.</summary>
    public const string EntryStyleId = "TableOfFiguresEntry";

    /// <summary>The heading text for a table of the given <paramref name="label"/>'s captions.</summary>
    public static string HeadingText(CaptionLabel label) => label switch
    {
        CaptionLabel.Figure => "Table of Figures",
        CaptionLabel.Table => "Table of Tables",
        CaptionLabel.Equation => "Table of Equations",
        _ => "Table of " + Captions.LabelText(label) + "s"
    };

    /// <summary>The heading text for a table of captions with a built-in or custom label.</summary>
    public static string HeadingText(string labelText)
    {
        var label = Captions.NormalizeLabelText(labelText);
        if (string.Equals(label, Captions.FigureLabelText, StringComparison.OrdinalIgnoreCase))
            return "Table of Figures";
        if (string.Equals(label, Captions.TableLabelText, StringComparison.OrdinalIgnoreCase))
            return "Table of Tables";
        if (string.Equals(label, Captions.EquationLabelText, StringComparison.OrdinalIgnoreCase))
            return "Table of Equations";
        return "Table of " + PluralizeLabel(label);
    }

    /// <summary>
    /// Builds the table-of-figures paragraphs for <paramref name="document"/>: a heading
    /// (<see cref="HeadingStyleId"/>, text from <see cref="HeadingText"/>) followed by one paragraph per
    /// caption of <paramref name="label"/> found in document order. Each entry carries the caption text,
    /// a dotted right tab, the caption page label, the <see cref="EntryStyleId"/> style, and shared native
    /// field ownership. A document with no matching captions yields just the heading paragraph.
    /// Deterministic and side-effect free; it never mutates <paramref name="document"/>.
    /// </summary>
    public static IReadOnlyList<Paragraph> Build(
        TextDocument document,
        CaptionLabel label = CaptionLabel.Figure,
        Func<int, string?>? pageTextOf = null)
    {
        return BuildCore(
            document,
            Captions.LabelText(label),
            pageTextOf is null ? null : (blockIndex, _) => pageTextOf(blockIndex));
    }

    /// <summary>
    /// Builds the table-of-figures paragraphs while exposing a recursive table-cell address to the
    /// page resolver. Top-level captions receive a null address.
    /// </summary>
    public static IReadOnlyList<Paragraph> BuildWithTableAddresses(
        TextDocument document,
        CaptionLabel label,
        Func<int, TableParagraphAddress?, string?>? pageTextOf)
    {
        return BuildCore(document, Captions.LabelText(label), pageTextOf);
    }

    /// <summary>
    /// Builds the table-of-figures paragraphs for a built-in or custom caption label.
    /// </summary>
    public static IReadOnlyList<Paragraph> Build(
        TextDocument document,
        string labelText,
        Func<int, string?>? pageTextOf = null)
    {
        return BuildCore(
            document,
            labelText,
            pageTextOf is null ? null : (blockIndex, _) => pageTextOf(blockIndex));
    }

    /// <summary>
    /// Builds the table-of-figures paragraphs for a built-in or custom label while exposing a recursive
    /// table-cell address to the page resolver. Top-level captions receive a null address.
    /// </summary>
    public static IReadOnlyList<Paragraph> BuildWithTableAddresses(
        TextDocument document,
        string labelText,
        Func<int, TableParagraphAddress?, string?>? pageTextOf)
    {
        return BuildCore(document, labelText, pageTextOf);
    }

    private static IReadOnlyList<Paragraph> BuildCore(
        TextDocument document,
        string labelText,
        Func<int, TableParagraphAddress?, string?>? pageTextOf)
    {
        ArgumentNullException.ThrowIfNull(document);
        var label = Captions.NormalizeLabelText(labelText);

        var paragraphs = new List<Paragraph>
        {
            new(HeadingText(label)) { StyleId = HeadingStyleId }
        };

        var entryRightTabStopPt = Math.Max(
            0,
            document.Page.WidthPt - document.Page.MarginLeftPt - document.Page.MarginRightPt);
        foreach (var (blockIndex, paragraph, tableParagraph) in DocumentBodyParagraphs.Enumerate(document))
        {
            if (!Captions.IsCaptionOf(paragraph, label))
                continue;

            var pageText = pageTextOf?.Invoke(blockIndex, tableParagraph);
            if (string.IsNullOrEmpty(pageText))
            {
                pageText = (CrossReferences.ExplicitPageNumberAtBlock(document, blockIndex) ?? 1)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            paragraphs.Add(CreateEntryParagraph(paragraph.PlainText, pageText, entryRightTabStopPt));
        }

        if (paragraphs.Count > 1)
        {
            var field = new ComplexField(NativeFieldInstructionFor(label));
            for (var index = 1; index < paragraphs.Count; index++)
                paragraphs[index].SpanningFieldOwner = field;
            paragraphs[1].SpanningFieldStart = field;
            paragraphs[^1].EndsSpanningField = true;
        }

        return paragraphs;
    }

    /// <summary>The native Word Table-of-Figures field instruction for a caption label.</summary>
    public static string NativeFieldInstructionFor(string labelText) =>
        $" TOC \\c \"{Captions.EscapeFieldArgument(Captions.NormalizeLabelText(labelText))}\" ";

    private static Paragraph CreateEntryParagraph(
        string captionText,
        string pageText,
        double entryRightTabStopPt)
    {
        var paragraph = new Paragraph
        {
            StyleId = EntryStyleId,
            Formatting = ParagraphFormatting.Default with
            {
                TabStops =
                [
                    new TabStop(
                        entryRightTabStopPt,
                        TabStopAlignment.Right,
                        TabLeader.Dots)
                ]
            }
        };
        paragraph.Runs.Add(new Run(captionText));
        paragraph.Runs.Add(new Run("\t"));
        paragraph.Runs.Add(new Run(pageText));
        return paragraph;
    }

    /// <summary>
    /// Infers the caption label from native field ownership first, then from a generated table heading,
    /// so Update Fields preserves the user's selected label.
    /// </summary>
    public static string? ExistingLabelText(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var paragraph in document.Blocks.OfType<Paragraph>())
        {
            if (TryGetNativeLabel(paragraph.SpanningFieldStart, out var nativeLabel)
                || TryGetNativeLabel(paragraph.SpanningFieldOwner, out nativeLabel))
                return nativeLabel;
            foreach (var run in paragraph.Runs)
                if (TryGetNativeLabel(run.ComplexField, out nativeLabel))
                    return nativeLabel;
        }

        foreach (var block in document.Blocks)
        {
            if (block is not Paragraph paragraph)
                continue;

            if (!string.Equals(paragraph.StyleId, HeadingStyleId, StringComparison.Ordinal))
                continue;

            var heading = paragraph.PlainText.Trim();
            if (string.Equals(heading, "Table of Figures", StringComparison.Ordinal))
                return Captions.FigureLabelText;
            if (string.Equals(heading, "Table of Tables", StringComparison.Ordinal))
                return Captions.TableLabelText;
            if (string.Equals(heading, "Table of Equations", StringComparison.Ordinal))
                return Captions.EquationLabelText;

            const string prefix = "Table of ";
            if (heading.StartsWith(prefix, StringComparison.Ordinal) && heading.Length > prefix.Length)
                return SingularizeHeadingLabel(heading[prefix.Length..]);
        }

        return null;
    }

    /// <summary>
    /// True when <paramref name="styleId"/> is one of the table-of-figures styles produced by
    /// <see cref="Build"/> (the heading style or the entry style). Used to recognise a previously inserted
    /// region so a refresh can remove it.
    /// </summary>
    public static bool IsTableOfFiguresStyleId(string? styleId)
    {
        if (string.IsNullOrEmpty(styleId))
            return false;
        return string.Equals(styleId, HeadingStyleId, StringComparison.Ordinal)
            || string.Equals(styleId, EntryStyleId, StringComparison.Ordinal);
    }

    /// <summary>
    /// True when <paramref name="block"/> is owned by a native caption-table field or carries a
    /// table-of-figures style (see <see cref="IsTableOfFiguresStyleId"/>).
    /// </summary>
    public static bool IsTableOfFiguresParagraph(Block block)
    {
        if (block is not Paragraph paragraph)
            return false;

        var nativeFields = paragraph.Runs
            .Select(run => run.ComplexField)
            .Prepend(paragraph.SpanningFieldOwner)
            .Where(field => field is { Keyword: "TOC" })
            .ToArray();
        return nativeFields.Length > 0
            ? nativeFields.Any(field => TryGetNativeLabel(field, out _))
            : IsTableOfFiguresStyleId(paragraph.StyleId);
    }

    /// <summary>True when <paramref name="field"/> is a native TOC-based table of captions.</summary>
    public static bool TryGetNativeLabel(ComplexField? field, out string label)
    {
        label = string.Empty;
        if (field is not { Keyword: "TOC" })
            return false;

        var value = ComplexFieldEngine.SwitchValue(field.Instruction, 'c')
            ?? ComplexFieldEngine.SwitchValue(field.Instruction, 'a');
        if (string.IsNullOrWhiteSpace(value))
            return false;

        label = value;
        return true;
    }

    /// <summary>
    /// Registers the table-of-figures styles (<see cref="HeadingStyleId"/> and <see cref="EntryStyleId"/>)
    /// in <paramref name="document"/>'s style catalog if they are not already present, so the inserted
    /// paragraphs resolve their formatting. Idempotent — existing styles are left untouched.
    /// </summary>
    public static void EnsureStyles(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Styles.TryAdd(HeadingStyleId, new DocumentStyle
        {
            Id = HeadingStyleId,
            Name = "Table of Figures Heading",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 16, ColorHex = "#2F5496" },
            Paragraph = new ParagraphFormatting { SpaceBeforePt = 12, SpaceAfterPt = 6 }
        });

        document.Styles.TryAdd(EntryStyleId, new DocumentStyle
        {
            Id = EntryStyleId,
            Name = "Table of Figures Entry",
            BasedOnStyleId = "Normal",
            Paragraph = new ParagraphFormatting { SpaceAfterPt = 2 }
        });
    }

    private static string PluralizeLabel(string label)
    {
        if (label.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            return label;
        if (label.EndsWith("y", StringComparison.OrdinalIgnoreCase)
            && label.Length > 1
            && !"aeiou".Contains(char.ToLowerInvariant(label[^2])))
            return label[..^1] + "ies";
        if (label.EndsWith("ch", StringComparison.OrdinalIgnoreCase)
            || label.EndsWith("sh", StringComparison.OrdinalIgnoreCase)
            || label.EndsWith("x", StringComparison.OrdinalIgnoreCase)
            || label.EndsWith("z", StringComparison.OrdinalIgnoreCase))
            return label + "es";
        return label + "s";
    }

    private static string SingularizeHeadingLabel(string label)
    {
        if (label.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && label.Length > 3)
            return label[..^3] + "y";
        if (label.EndsWith("es", StringComparison.OrdinalIgnoreCase)
            && (label.EndsWith("ches", StringComparison.OrdinalIgnoreCase)
                || label.EndsWith("shes", StringComparison.OrdinalIgnoreCase)
                || label.EndsWith("xes", StringComparison.OrdinalIgnoreCase)
                || label.EndsWith("zes", StringComparison.OrdinalIgnoreCase)))
            return label[..^2];
        if (label.EndsWith("s", StringComparison.OrdinalIgnoreCase) && label.Length > 1)
            return label[..^1];
        return label;
    }
}
