using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Shell;

/// <summary>
/// Plans the feature-loss warning shown before a save/export writes bytes.
/// </summary>
public static class DocumentSaveCompatibilityPlanner
{
    public const string DefaultTitle = "Confirm compatibility save";
    public const string ContinueButtonText = "Continue";
    public const string CancelButtonText = "Cancel";

    public static DocumentSaveCompatibilityPlan Build(TextDocument document, DocumentSaveTarget target)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);

        var format = target.Format ?? ResolveTargetFormat(target);
        var profile = TargetProfile.From(format, target.Adapter, target.Path);
        var evidence = DocumentSaveFeatureEvidence.From(document);
        var warnings = BuildWarnings(profile, evidence).ToArray();

        if (warnings.Length == 0)
            return DocumentSaveCompatibilityPlan.NoWarning(profile.TargetLabel);

        return DocumentSaveCompatibilityPlan.Warning(
            profile.TargetLabel,
            BuildMessage(profile, warnings),
            warnings);
    }

    private static FileFormatDescriptor? ResolveTargetFormat(DocumentSaveTarget target)
    {
        var extension = DocumentFileFormatResolver.NormalizeExtension(
            FilePathPolicy.GetExtensionOrEmpty(target.Path));
        return target.Adapter.Formats.FirstOrDefault(format =>
            string.Equals(
                DocumentFileFormatResolver.NormalizeExtension(format.Extension),
                extension,
                StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<DocumentSaveCompatibilityWarning> BuildWarnings(
        TargetProfile profile,
        DocumentSaveFeatureEvidence evidence)
    {
        if (!profile.CanSave)
        {
            yield return Warning(
                DocumentSaveCompatibilityWarningKind.UnsupportedTarget,
                $"{profile.TargetLabel} is not a writable FreeW format.",
                "Choose another target such as Word Document (*.docx).");
            yield break;
        }

        if (evidence.HasMacroProject && !profile.PreservesMacros)
        {
            yield return Warning(
                DocumentSaveCompatibilityWarningKind.MacroProject,
                "VBA macro project parts will not be written to this target.",
                "Use a macro-enabled Word document or template (*.docm, *.dotm) to keep preserved macro bytes.");
        }

        if (profile.Kind == TargetCompatibilityKind.NativeOoxml)
            yield break;

        if (profile.AlwaysWarn)
        {
            yield return Warning(
                profile.Kind == TargetCompatibilityKind.PlainText
                    ? DocumentSaveCompatibilityWarningKind.TextOnlyTarget
                    : DocumentSaveCompatibilityWarningKind.CompatibilityTarget,
                TargetWarningSummary(profile),
                TargetWarningDetail(profile));
        }

        if (evidence.HasPreservedPackageParts && !profile.PreservesOoxmlPackageParts)
        {
            yield return Warning(
                DocumentSaveCompatibilityWarningKind.PreservedPackageParts,
                "Preserved OOXML package parts will be dropped.",
                "Custom XML, web settings, glossary parts, preserved numbering, embedded font package data, and unmodelled drawing parts only round-trip through OOXML targets.");
        }

        if ((evidence.HasComments && !profile.PreservesComments) ||
            (evidence.HasRevisionsOrProtection && !profile.PreservesReviewAndProtection))
        {
            yield return Warning(
                DocumentSaveCompatibilityWarningKind.ReviewAndProtection,
                "Comments, tracked changes, or protection settings may be removed.",
                "Resolve review markup or save as a Word document before writing this compatibility copy.");
        }

        if (evidence.HasFieldsCitationsBookmarksOrCrossReferences && !profile.PreservesFieldsAndReferences)
        {
            yield return Warning(
                DocumentSaveCompatibilityWarningKind.FieldsAndReferences,
                "Fields, citations, bookmarks, or cross-references may become static text or be removed.",
                "Generated results may remain visible, but editable Word field/reference metadata is not guaranteed in this format.");
        }

        if (evidence.HasFootnotesOrEndnotes && !profile.PreservesFootnotesAndEndnotes)
        {
            yield return Warning(
                DocumentSaveCompatibilityWarningKind.FootnotesAndEndnotes,
                "Footnotes or endnotes may be removed or flattened.",
                "Use a Word document or OpenDocument Text target when note structure must stay editable.");
        }

        if (evidence.HasTables && !profile.PreservesTables)
        {
            yield return Warning(
                DocumentSaveCompatibilityWarningKind.Tables,
                "Tables may be removed or reduced to plain text.",
                "Cell structure, merged cells, table styles, and layout options are not preserved by this target.");
        }

        if (evidence.HasDrawingsChartsSmartArtOrImages && !profile.PreservesDrawingsChartsSmartArtAndImages)
        {
            yield return Warning(
                DocumentSaveCompatibilityWarningKind.DrawingsChartsSmartArtAndImages,
                "Images, drawings, charts, SmartArt, or embedded objects may be removed or simplified.",
                "Visual objects that FreeW can keep in OOXML may not have an equivalent representation in this writer.");
        }

        if (evidence.HasContentControls && !profile.PreservesContentControls)
        {
            yield return Warning(
                DocumentSaveCompatibilityWarningKind.ContentControls,
                "Content controls may be converted to ordinary text.",
                "Checkbox, date, rich-text, combo-box, and drop-down metadata may no longer be editable as controls.");
        }

        if (evidence.HasHeadersOrFooters && !profile.PreservesHeadersAndFooters)
        {
            yield return Warning(
                DocumentSaveCompatibilityWarningKind.HeadersAndFooters,
                "Headers and footers may be removed.",
                "Page-number fields and first/even-page header or footer variants need a Word-compatible target.");
        }

        if (evidence.HasRichFormatting && !profile.PreservesRichFormatting)
        {
            yield return Warning(
                DocumentSaveCompatibilityWarningKind.RichFormatting,
                "Rich formatting may be simplified.",
                "Styles, lists, spacing, page setup, borders, shading, language, and advanced typography may not round-trip.");
        }
    }

    private static DocumentSaveCompatibilityWarning Warning(
        DocumentSaveCompatibilityWarningKind kind,
        string summary,
        string detail) =>
        new(kind, summary, detail);

    private static string BuildMessage(
        TargetProfile profile,
        IReadOnlyList<DocumentSaveCompatibilityWarning> warnings)
    {
        var lines = new List<string>
        {
            $"Saving as {profile.TargetLabel} may remove or simplify document features.",
            string.Empty,
        };

        foreach (var warning in warnings)
            lines.Add("- " + warning.Summary);

        lines.Add(string.Empty);
        lines.Add("Choose Continue to write this file anyway, or Cancel to choose another format.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string TargetWarningSummary(TargetProfile profile) =>
        profile.Kind switch
        {
            TargetCompatibilityKind.PlainText => "Plain text keeps only characters and paragraph breaks.",
            TargetCompatibilityKind.Word2003Xml => "Word 2003 XML writes only the older modeled WordML subset.",
            TargetCompatibilityKind.LegacyWordBinary => "Word 97-2003 binary output is a legacy compatibility writer.",
            TargetCompatibilityKind.Web => "Web page formats are document-to-HTML conversions, not full Word round-trips.",
            TargetCompatibilityKind.Rtf => "Rich Text Format is an interchange format with a supported subset.",
            TargetCompatibilityKind.OpenDocument => "OpenDocument Text may skip Word-specific constructs.",
            TargetCompatibilityKind.FlatOpcXml => "Flat OPC Word XML reframes the document outside the native package.",
            _ => $"{profile.TargetLabel} has limited FreeW round-trip support.",
        };

    private static string TargetWarningDetail(TargetProfile profile) =>
        profile.Kind switch
        {
            TargetCompatibilityKind.PlainText => "Formatting, images, tables, comments, fields, and document structure are not written.",
            TargetCompatibilityKind.Word2003Xml => "The writer keeps paragraphs, basic formatting, tables, and page geometry; modern Word features are skipped.",
            TargetCompatibilityKind.LegacyWordBinary => "This writer keeps the document text stream and cannot carry modern Word feature metadata.",
            TargetCompatibilityKind.Web => "HTML output keeps visible web content but does not preserve Word-only editing metadata.",
            TargetCompatibilityKind.Rtf => "RTF output keeps text, basic formatting, lists, and tables while omitting newer Word features.",
            TargetCompatibilityKind.OpenDocument => "ODT output keeps the modeled ODF subset and skips unsupported Word-specific objects.",
            TargetCompatibilityKind.FlatOpcXml => "The output is editable XML, but it is not the ordinary native .docx package.",
            _ => "Review the warning details before continuing.",
        };

    private sealed record TargetProfile(
        TargetCompatibilityKind Kind,
        string TargetLabel,
        bool CanSave,
        bool AlwaysWarn,
        bool PreservesMacros,
        bool PreservesOoxmlPackageParts,
        bool PreservesReviewAndProtection,
        bool PreservesComments,
        bool PreservesFieldsAndReferences,
        bool PreservesFootnotesAndEndnotes,
        bool PreservesTables,
        bool PreservesDrawingsChartsSmartArtAndImages,
        bool PreservesContentControls,
        bool PreservesHeadersAndFooters,
        bool PreservesRichFormatting)
    {
        public static TargetProfile From(FileFormatDescriptor? format, IDocumentFileAdapter adapter, string path)
        {
            var extension = DocumentFileFormatResolver.NormalizeExtension(
                format?.Extension ?? FilePathPolicy.GetExtensionOrEmpty(path));
            var formatName = format?.FormatName ?? adapter.FormatName;
            var kind = Classify(format, adapter, extension, formatName);
            var canSave = format?.CanSave ?? true;

            bool IsMacroOoxml() =>
                string.Equals(extension, ".docm", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".dotm", StringComparison.OrdinalIgnoreCase);

            return kind switch
            {
                TargetCompatibilityKind.NativeOoxml => new(
                    kind,
                    Label(formatName, extension),
                    canSave,
                    AlwaysWarn: false,
                    PreservesMacros: IsMacroOoxml(),
                    PreservesOoxmlPackageParts: true,
                    PreservesReviewAndProtection: true,
                    PreservesComments: true,
                    PreservesFieldsAndReferences: true,
                    PreservesFootnotesAndEndnotes: true,
                    PreservesTables: true,
                    PreservesDrawingsChartsSmartArtAndImages: true,
                    PreservesContentControls: true,
                    PreservesHeadersAndFooters: true,
                    PreservesRichFormatting: true),

                TargetCompatibilityKind.FlatOpcXml => new(
                    kind,
                    Label(formatName, extension),
                    canSave,
                    AlwaysWarn: false,
                    PreservesMacros: false,
                    PreservesOoxmlPackageParts: true,
                    PreservesReviewAndProtection: true,
                    PreservesComments: true,
                    PreservesFieldsAndReferences: true,
                    PreservesFootnotesAndEndnotes: true,
                    PreservesTables: true,
                    PreservesDrawingsChartsSmartArtAndImages: true,
                    PreservesContentControls: true,
                    PreservesHeadersAndFooters: true,
                    PreservesRichFormatting: true),

                TargetCompatibilityKind.OpenDocument => new(
                    kind,
                    Label(formatName, extension),
                    canSave,
                    AlwaysWarn: true,
                    PreservesMacros: false,
                    PreservesOoxmlPackageParts: false,
                    PreservesReviewAndProtection: false,
                    PreservesComments: true,
                    PreservesFieldsAndReferences: false,
                    PreservesFootnotesAndEndnotes: true,
                    PreservesTables: true,
                    PreservesDrawingsChartsSmartArtAndImages: false,
                    PreservesContentControls: false,
                    PreservesHeadersAndFooters: false,
                    PreservesRichFormatting: true),

                TargetCompatibilityKind.Rtf => new(
                    kind,
                    Label(formatName, extension),
                    canSave,
                    AlwaysWarn: true,
                    PreservesMacros: false,
                    PreservesOoxmlPackageParts: false,
                    PreservesReviewAndProtection: false,
                    PreservesComments: false,
                    PreservesFieldsAndReferences: false,
                    PreservesFootnotesAndEndnotes: false,
                    PreservesTables: true,
                    PreservesDrawingsChartsSmartArtAndImages: false,
                    PreservesContentControls: false,
                    PreservesHeadersAndFooters: false,
                    PreservesRichFormatting: true),

                TargetCompatibilityKind.Web => new(
                    kind,
                    Label(formatName, extension),
                    canSave,
                    AlwaysWarn: true,
                    PreservesMacros: false,
                    PreservesOoxmlPackageParts: false,
                    PreservesReviewAndProtection: false,
                    PreservesComments: false,
                    PreservesFieldsAndReferences: false,
                    PreservesFootnotesAndEndnotes: true,
                    PreservesTables: true,
                    PreservesDrawingsChartsSmartArtAndImages: true,
                    PreservesContentControls: false,
                    PreservesHeadersAndFooters: false,
                    PreservesRichFormatting: true),

                TargetCompatibilityKind.Word2003Xml => new(
                    kind,
                    Label(formatName, extension),
                    canSave,
                    AlwaysWarn: true,
                    PreservesMacros: false,
                    PreservesOoxmlPackageParts: false,
                    PreservesReviewAndProtection: false,
                    PreservesComments: false,
                    PreservesFieldsAndReferences: false,
                    PreservesFootnotesAndEndnotes: false,
                    PreservesTables: true,
                    PreservesDrawingsChartsSmartArtAndImages: false,
                    PreservesContentControls: false,
                    PreservesHeadersAndFooters: false,
                    PreservesRichFormatting: true),

                TargetCompatibilityKind.LegacyWordBinary => new(
                    kind,
                    Label(formatName, extension),
                    canSave,
                    AlwaysWarn: true,
                    PreservesMacros: false,
                    PreservesOoxmlPackageParts: false,
                    PreservesReviewAndProtection: false,
                    PreservesComments: false,
                    PreservesFieldsAndReferences: false,
                    PreservesFootnotesAndEndnotes: false,
                    PreservesTables: false,
                    PreservesDrawingsChartsSmartArtAndImages: false,
                    PreservesContentControls: false,
                    PreservesHeadersAndFooters: false,
                    PreservesRichFormatting: false),

                TargetCompatibilityKind.PlainText => new(
                    kind,
                    Label(formatName, extension),
                    canSave,
                    AlwaysWarn: true,
                    PreservesMacros: false,
                    PreservesOoxmlPackageParts: false,
                    PreservesReviewAndProtection: false,
                    PreservesComments: false,
                    PreservesFieldsAndReferences: false,
                    PreservesFootnotesAndEndnotes: false,
                    PreservesTables: false,
                    PreservesDrawingsChartsSmartArtAndImages: false,
                    PreservesContentControls: false,
                    PreservesHeadersAndFooters: false,
                    PreservesRichFormatting: false),

                _ => new(
                    kind,
                    Label(formatName, extension),
                    canSave,
                    AlwaysWarn: true,
                    PreservesMacros: false,
                    PreservesOoxmlPackageParts: false,
                    PreservesReviewAndProtection: false,
                    PreservesComments: false,
                    PreservesFieldsAndReferences: false,
                    PreservesFootnotesAndEndnotes: false,
                    PreservesTables: false,
                    PreservesDrawingsChartsSmartArtAndImages: false,
                    PreservesContentControls: false,
                    PreservesHeadersAndFooters: false,
                    PreservesRichFormatting: false),
            };
        }

        private static TargetCompatibilityKind Classify(
            FileFormatDescriptor? format,
            IDocumentFileAdapter adapter,
            string extension,
            string formatName)
        {
            if (format?.CanSave == false)
                return TargetCompatibilityKind.Unsupported;
            if (format?.IsLegacy == true)
                return TargetCompatibilityKind.LegacyWordBinary;
            if (adapter is PlainTextFileAdapter ||
                extension is ".txt" or ".text" or ".log" ||
                formatName.Contains("Plain text", StringComparison.OrdinalIgnoreCase) ||
                formatName.Contains("Log file", StringComparison.OrdinalIgnoreCase))
            {
                return TargetCompatibilityKind.PlainText;
            }

            if (adapter is DocxFileAdapter || extension is ".docx" or ".docm" or ".dotx" or ".dotm")
                return TargetCompatibilityKind.NativeOoxml;
            if (adapter is WordXmlFileAdapter && formatName.Contains("Word XML", StringComparison.OrdinalIgnoreCase))
                return TargetCompatibilityKind.FlatOpcXml;
            if (adapter is Wordml2003FileAdapter ||
                formatName.Contains("Word 2003 XML", StringComparison.OrdinalIgnoreCase))
            {
                return TargetCompatibilityKind.Word2003Xml;
            }

            if (adapter is HtmlFileAdapter or MhtmlFileAdapter ||
                formatName.Contains("Web Page", StringComparison.OrdinalIgnoreCase) ||
                formatName.Contains("MHTML", StringComparison.OrdinalIgnoreCase))
            {
                return TargetCompatibilityKind.Web;
            }

            if (adapter is RtfFileAdapter || extension == ".rtf")
                return TargetCompatibilityKind.Rtf;
            if (adapter is OdtFileAdapter || extension is ".odt" or ".ott")
                return TargetCompatibilityKind.OpenDocument;

            return TargetCompatibilityKind.UnknownCompatibility;
        }

        private static string Label(string formatName, string extension) =>
            string.IsNullOrWhiteSpace(extension)
                ? formatName
                : $"{formatName} (*{extension})";
    }

    private enum TargetCompatibilityKind
    {
        NativeOoxml,
        FlatOpcXml,
        Word2003Xml,
        Web,
        Rtf,
        OpenDocument,
        LegacyWordBinary,
        PlainText,
        Unsupported,
        UnknownCompatibility,
    }

    private sealed record DocumentSaveFeatureEvidence(
        bool HasMacroProject,
        bool HasPreservedPackageParts,
        bool HasComments,
        bool HasRevisionsOrProtection,
        bool HasFieldsCitationsBookmarksOrCrossReferences,
        bool HasFootnotesOrEndnotes,
        bool HasTables,
        bool HasDrawingsChartsSmartArtOrImages,
        bool HasContentControls,
        bool HasHeadersOrFooters,
        bool HasRichFormatting)
    {
        public static DocumentSaveFeatureEvidence From(TextDocument document)
        {
            var paragraphs = EnumerateAllParagraphs(document).ToArray();
            var runs = paragraphs.SelectMany(paragraph => paragraph.Runs).ToArray();

            var hasMacroProject = document.Preserved.Parts.Any(part => IsMacroPart(part.PartName));
            var hasPreservedPackageParts =
                !document.Preserved.IsEmpty ||
                document.Preserved.ContentTypeDefaults.Count > 0 ||
                document.EmbeddedFonts.Count > 0;
            var hasComments = document.Comments.Count > 0 || runs.Any(run => run.CommentId.HasValue);
            var hasRevisions = TrackChanges.HasRevisions(document) ||
                paragraphs.Any(paragraph => paragraph.ParagraphFormatRevision is not null) ||
                runs.Any(run => run.Revision != RevisionKind.None || run.FormatRevision is not null);
            var hasProtection = document.Protection.IsProtected || document.MarkedAsFinal;
            var hasFieldsReferences = document.Sources.Count > 0 ||
                document.Citations.Count > 0 ||
                document.IndexEntries.Count > 0 ||
                paragraphs.Any(paragraph => paragraph.BookmarkNames.Count > 0 || paragraph.PreservedNumbering is not null) ||
                runs.Any(run =>
                    run.FieldKind != RunFieldKind.None ||
                    run.TableFormula is not null ||
                    run.CrossReference is not null ||
                    run.ComplexField is not null ||
                    !string.IsNullOrWhiteSpace(run.HyperlinkAnchor));
            var hasNotes = document.Footnotes.Count > 0 ||
                document.Endnotes.Count > 0 ||
                !document.FootnoteNumbering.IsDefault ||
                !document.EndnoteNumbering.IsDefault ||
                runs.Any(run => run.FootnoteId.HasValue || run.EndnoteId.HasValue);
            var hasTables = document.Blocks.OfType<Table>().Any();
            var hasDrawings = runs.Any(run =>
                run.Image is not null ||
                run.Equation is not null ||
                run.Shape is not null ||
                run.WordArt is not null ||
                run.Chart is not null ||
                run.EmbeddedObject is not null ||
                run.SmartArt is not null ||
                run.PreservedDrawing is not null ||
                run.DrawingGroup is not null);
            var hasContentControls = document.Blocks.Any(block => block.BlockContentControl is not null) ||
                runs.Any(run => run.Control is not null);
            var hasHeadersFooters = document.Sections.Any(section => !section.HeadersFooters.IsEmpty);
            var hasRichFormatting =
                paragraphs.Any(HasRichParagraphFormatting) ||
                runs.Any(run => HasRichRunFormatting(run.Formatting)) ||
                !document.MultiLevelList.NumberFormats.SequenceEqual(MultiLevelListFormat.DecimalNumberFormats);

            return new DocumentSaveFeatureEvidence(
                hasMacroProject,
                hasPreservedPackageParts,
                hasComments,
                hasRevisions || hasProtection,
                hasFieldsReferences,
                hasNotes,
                hasTables,
                hasDrawings,
                hasContentControls,
                hasHeadersFooters,
                hasRichFormatting);
        }

        private static bool IsMacroPart(string partName) =>
            partName.Equals("/word/vbaProject.bin", StringComparison.OrdinalIgnoreCase) ||
            partName.Equals("/word/vbaData.xml", StringComparison.OrdinalIgnoreCase) ||
            partName.Equals("/word/_rels/vbaProject.bin.rels", StringComparison.OrdinalIgnoreCase);

        private static IEnumerable<Paragraph> EnumerateAllParagraphs(TextDocument document)
        {
            foreach (var paragraph in EnumerateBodyParagraphs(document.Blocks))
                yield return paragraph;

            foreach (var section in document.Sections)
            {
                foreach (var paragraph in EnumerateHeaderFooterParagraphs(section.HeadersFooters))
                    yield return paragraph;
            }

            foreach (var note in document.Footnotes.Values)
                foreach (var paragraph in note.Content)
                    yield return paragraph;

            foreach (var note in document.Endnotes.Values)
                foreach (var paragraph in note.Content)
                    yield return paragraph;

            foreach (var comment in document.Comments.Values.SelectMany(comment => comment.ThreadInOrder()))
                foreach (var paragraph in comment.Content)
                    yield return paragraph;
        }

        private static IEnumerable<Paragraph> EnumerateBodyParagraphs(IEnumerable<Block> blocks)
        {
            foreach (var block in blocks)
            {
                if (block is Paragraph paragraph)
                {
                    yield return paragraph;
                }
                else if (block is Table table)
                {
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var cellParagraph in cell.Paragraphs)
                                yield return cellParagraph;
                        }
                    }
                }
            }
        }

        private static IEnumerable<Paragraph> EnumerateHeaderFooterParagraphs(SectionHeadersFooters headersFooters)
        {
            foreach (var headerFooter in new[]
                     {
                         headersFooters.Header,
                         headersFooters.Footer,
                         headersFooters.EvenHeader,
                         headersFooters.EvenFooter,
                         headersFooters.FirstHeader,
                         headersFooters.FirstFooter,
                     })
            {
                if (headerFooter is null)
                    continue;

                foreach (var paragraph in headerFooter.Paragraphs)
                    yield return paragraph;
            }
        }

        private static bool HasRichParagraphFormatting(Paragraph paragraph) =>
            paragraph.StyleId is not null ||
            paragraph.Formatting != ParagraphFormatting.Default ||
            paragraph.SectionBreak is not null;

        private static bool HasRichRunFormatting(RunFormatting formatting) =>
            formatting != RunFormatting.Default;
    }
}

public sealed record DocumentSaveCompatibilityPlan(
    bool RequiresConfirmation,
    string TargetLabel,
    string Title,
    string Message,
    string ContinueButtonText,
    string CancelButtonText,
    IReadOnlyList<DocumentSaveCompatibilityWarning> Warnings)
{
    public static DocumentSaveCompatibilityPlan NoWarning(string targetLabel) =>
        new(
            RequiresConfirmation: false,
            targetLabel,
            string.Empty,
            string.Empty,
            DocumentSaveCompatibilityPlanner.ContinueButtonText,
            DocumentSaveCompatibilityPlanner.CancelButtonText,
            []);

    public static DocumentSaveCompatibilityPlan Warning(
        string targetLabel,
        string message,
        IReadOnlyList<DocumentSaveCompatibilityWarning> warnings) =>
        new(
            RequiresConfirmation: true,
            targetLabel,
            DocumentSaveCompatibilityPlanner.DefaultTitle,
            message,
            DocumentSaveCompatibilityPlanner.ContinueButtonText,
            DocumentSaveCompatibilityPlanner.CancelButtonText,
            warnings);
}

public sealed record DocumentSaveCompatibilityWarning(
    DocumentSaveCompatibilityWarningKind Kind,
    string Summary,
    string Detail);

public enum DocumentSaveCompatibilityWarningKind
{
    CompatibilityTarget,
    UnsupportedTarget,
    MacroProject,
    PreservedPackageParts,
    ReviewAndProtection,
    FieldsAndReferences,
    FootnotesAndEndnotes,
    Tables,
    DrawingsChartsSmartArtAndImages,
    ContentControls,
    HeadersAndFooters,
    RichFormatting,
    TextOnlyTarget,
}
