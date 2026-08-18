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
            new(HeadingText(label)) { StyleId = HeadingStyleIdFor(label) }
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

            paragraphs.Add(CreateEntryParagraph(paragraph.PlainText, pageText, entryRightTabStopPt, label));
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
        double entryRightTabStopPt,
        string labelText)
    {
        var paragraph = new Paragraph
        {
            StyleId = EntryStyleIdFor(labelText),
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
    /// <see cref="Build"/> for ANY caption label (the base heading/entry style ids, or one of the
    /// per-label variants produced by <see cref="HeadingStyleIdFor"/>/<see cref="EntryStyleIdFor"/>).
    /// Used to recognise a previously inserted region of any label so it can be identified as
    /// "some table of figures/tables/equations region", not tied to one specific label.
    /// </summary>
    public static bool IsTableOfFiguresStyleId(string? styleId)
    {
        if (string.IsNullOrEmpty(styleId))
            return false;
        return string.Equals(styleId, HeadingStyleId, StringComparison.Ordinal)
            || string.Equals(styleId, EntryStyleId, StringComparison.Ordinal)
            || styleId.StartsWith(HeadingStyleId + LabelStyleSuffixPrefix, StringComparison.Ordinal)
            || styleId.StartsWith(EntryStyleId + LabelStyleSuffixPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// True only when <paramref name="styleId"/> is the heading or entry style produced for the
    /// specific <paramref name="labelText"/> (see <see cref="HeadingStyleIdFor"/>/<see cref="EntryStyleIdFor"/>).
    /// Unlike <see cref="IsTableOfFiguresStyleId(string?)"/>, this does not match a table built for a
    /// different caption label.
    /// </summary>
    public static bool IsTableOfFiguresStyleId(string? styleId, string? labelText)
    {
        if (string.IsNullOrEmpty(styleId))
            return false;
        return string.Equals(styleId, HeadingStyleIdFor(labelText), StringComparison.Ordinal)
            || string.Equals(styleId, EntryStyleIdFor(labelText), StringComparison.Ordinal);
    }

    /// <summary>
    /// True when <paramref name="block"/> is owned by a native caption-table field of ANY label or
    /// carries a table-of-figures style of ANY label (see <see cref="IsTableOfFiguresStyleId(string?)"/>).
    /// Use this only to test "is this some generated caption table region at all" -- to scope a refresh
    /// to one label's own region (Figure vs Table vs Equation vs a custom label), use the
    /// <see cref="IsTableOfFiguresParagraph(Block, string?)"/> overload instead, since two regions built
    /// for different labels otherwise satisfy this predicate identically.
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

    /// <summary>
    /// True only when <paramref name="block"/> belongs to the generated table-of-figures region for the
    /// specific <paramref name="labelText"/> (defaults to <see cref="Captions.FigureLabelText"/> when
    /// null/blank). A native caption-table field must carry the matching <c>\c</c>/<c>\a</c> switch value;
    /// a styled paragraph must carry the heading/entry style id scoped to this label. This is what a
    /// refresh must use to locate its own region without also matching -- and deleting -- a sibling
    /// Table of Figures/Tables/Equations built for a different label.
    /// </summary>
    public static bool IsTableOfFiguresParagraph(Block block, string? labelText)
    {
        if (block is not Paragraph paragraph)
            return false;

        var nativeFields = paragraph.Runs
            .Select(run => run.ComplexField)
            .Prepend(paragraph.SpanningFieldOwner)
            .Where(field => field is { Keyword: "TOC" })
            .ToArray();
        return nativeFields.Length > 0
            ? nativeFields.Any(field => TryGetNativeLabel(field, out var nativeLabel) && LabelsMatch(nativeLabel, labelText))
            : IsTableOfFiguresStyleId(paragraph.StyleId, labelText);
    }

    private static bool LabelsMatch(string nativeLabel, string? requestedLabel) =>
        string.Equals(
            Captions.NormalizeLabelText(nativeLabel),
            EffectiveLabel(requestedLabel),
            StringComparison.OrdinalIgnoreCase);

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
    /// The heading style id for the generated table of a specific caption label -- <see cref="HeadingStyleId"/>
    /// itself for the default <see cref="Captions.FigureLabelText"/> label (so existing Figure-only
    /// documents and callers keep resolving the same constant), and a label-scoped variant for every
    /// other label so a Table of Tables/Equations/custom-label region is never confused with a
    /// differently-labelled one (see <see cref="IsTableOfFiguresParagraph(Block, string?)"/>).
    /// </summary>
    public static string HeadingStyleIdFor(string? labelText) =>
        HeadingStyleId + LabelStyleSuffix(labelText);

    /// <summary>The entry style id for the generated table of a specific caption label. See <see cref="HeadingStyleIdFor"/>.</summary>
    public static string EntryStyleIdFor(string? labelText) =>
        EntryStyleId + LabelStyleSuffix(labelText);

    /// <summary>
    /// Registers the table-of-figures styles (<see cref="HeadingStyleIdFor"/> and <see cref="EntryStyleIdFor"/>
    /// for <paramref name="labelText"/>) in <paramref name="document"/>'s style catalog if they are not
    /// already present, so the inserted paragraphs resolve their formatting. Idempotent — existing styles
    /// are left untouched. <paramref name="labelText"/> defaults to <see cref="Captions.FigureLabelText"/>.
    /// </summary>
    public static void EnsureStyles(TextDocument document, string? labelText = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var headingStyleId = HeadingStyleIdFor(labelText);
        var entryStyleId = EntryStyleIdFor(labelText);
        var nameSuffix = IsDefaultLabel(labelText) ? string.Empty : $" ({EffectiveLabel(labelText)})";

        document.Styles.TryAdd(headingStyleId, new DocumentStyle
        {
            Id = headingStyleId,
            Name = "Table of Figures Heading" + nameSuffix,
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 16, ColorHex = "#2F5496" },
            Paragraph = new ParagraphFormatting { SpaceBeforePt = 12, SpaceAfterPt = 6 }
        });

        document.Styles.TryAdd(entryStyleId, new DocumentStyle
        {
            Id = entryStyleId,
            Name = "Table of Figures Entry" + nameSuffix,
            BasedOnStyleId = "Normal",
            Paragraph = new ParagraphFormatting { SpaceAfterPt = 2 }
        });
    }

    private const string LabelStyleSuffixPrefix = "_l_";

    private static bool IsDefaultLabel(string? labelText) =>
        string.Equals(EffectiveLabel(labelText), Captions.FigureLabelText, StringComparison.OrdinalIgnoreCase);

    private static string EffectiveLabel(string? labelText)
    {
        var trimmed = labelText?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? Captions.FigureLabelText : trimmed;
    }

    private static string LabelStyleSuffix(string? labelText)
    {
        if (IsDefaultLabel(labelText))
            return string.Empty;

        var bytes = System.Text.Encoding.UTF8.GetBytes(EffectiveLabel(labelText).ToUpperInvariant());
        return LabelStyleSuffixPrefix + Convert.ToHexString(bytes);
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
