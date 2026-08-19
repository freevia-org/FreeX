using System.Globalization;
using Free.Shared.Opc;

namespace FreeW.Core.Model;

/// <summary>
/// Pure, WPF-free recomputation of a <em>complex</em> Word field's result (the <c>w:fldChar</c>/
/// <c>w:instrText</c> construct carried by <see cref="Run.ComplexField"/>). This is the model-side engine
/// behind F9 / "Update Field": given a field instruction and the current document state it returns the
/// field's fresh result text. It complements <see cref="Run.ComplexField"/> (which only round-trips the
/// raw instruction and a cached result) by actually re-evaluating the instruction.
/// <para>
/// The engine resolves literal formula plus reference/numbering field families FreeW already models but previously could
/// not refresh:
/// </para>
/// <list type="bullet">
/// <item><c>REF bookmark</c> — the text of the bookmarked paragraph (Word's cross-reference "Text").</item>
/// <item><c>PAGEREF bookmark</c> — the page the bookmarked paragraph sits on, via a caller-supplied page
/// map (the model has no pagination of its own); falls back to "1" when no page is known.</item>
/// <item><c>SEQ name</c> — the running counter for that sequence name (the basis of captions like
/// "Figure 1"/"Table 2"), counting how many earlier SEQ fields of the same name precede this one, with
/// support for the <c>\c</c> (repeat current), <c>\r N</c> (reset to N), <c>\s N</c> (restart after
/// a heading), <c>\n</c> (next number), <c>\h</c> (hide) and numeric result-picture switches.</item>
/// <item><c>STYLEREF 1</c> / <c>STYLEREF "Heading 1"</c> — the nearest preceding body paragraph using the
/// requested heading style, or the next matching paragraph when none precedes the field; with the
/// <c>\n</c> switch, that paragraph's outline number (e.g. "1.2") instead of its text.</item>
/// </list>
/// <para>
/// Lives in the model project so it is fully unit-testable without any UI. Recomputing a nested field also
/// refreshes that inner field's cached result/metadata on the owning run so a subsequent save retains the
/// nested WordprocessingML sequence rather than flattening it.
/// </para>
/// </summary>
public static class ComplexFieldEngine
{
    private const string AttachedTemplateRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/attachedTemplate";
    private const string SettingsRelationshipsPartName = "/word/_rels/settings.xml.rels";

    /// <summary>
    /// True when <paramref name="field"/> is a field family this engine can recompute
    /// (<c>=</c>, <c>REF</c>, <c>PAGEREF</c>, <c>SEQ</c>, <c>CITATION</c>, <c>STYLEREF</c>, <c>IF</c>,
    /// <c>DOCPROPERTY</c>, <c>DOCVARIABLE</c>, <c>CREATEDATE</c>, <c>SAVEDATE</c>, <c>LASTSAVEDBY</c>,
    /// <c>TEMPLATE</c>, <c>NUMWORDS</c>, <c>NUMCHARS</c>, <c>REVNUM</c>, <c>EDITTIME</c>, or
    /// <c>PRINTDATE</c>).
    /// Other keywords
    /// (PAGE/DATE/AUTHOR/…) are resolved elsewhere or left to their cached value, so the caller can
    /// cheaply skip them.
    /// </summary>
    public static bool CanRecompute(ComplexField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return CanRecomputeKeyword(field.Keyword)
            || field.NestedFields?.Any(nested => !nested.Field.IsLocked && CanRecompute(nested.Field)) == true;
    }

    private static bool CanRecomputeKeyword(string keyword) =>
        keyword is "=" or "REF" or "PAGEREF" or "SEQ" or "CITATION" or "STYLEREF" or "IF"
            or "DOCPROPERTY" or "DOCVARIABLE" or "CREATEDATE" or "SAVEDATE" or "LASTSAVEDBY"
            or "TEMPLATE" or "NUMWORDS" or "NUMCHARS" or "REVNUM" or "EDITTIME" or "PRINTDATE";

    /// <summary>
    /// Recomputes the result text of the complex field carried by the run at
    /// (<paramref name="blockIndex"/>, <paramref name="runIndex"/>) in <paramref name="document"/>, against
    /// the document's current bookmarks (REF/PAGEREF) and sequence counters (SEQ). Returns the run's
    /// existing <see cref="Run.Text"/> unchanged for fields this engine does not handle, for unresolvable
    /// references/style lookups, or for an empty instruction — so an F9 pass never blanks a field it cannot
    /// evaluate.
    /// </summary>
    /// <param name="document">The document whose current state the field resolves against.</param>
    /// <param name="blockIndex">Index of the field run's paragraph in <see cref="TextDocument.Blocks"/>.</param>
    /// <param name="runIndex">Index of the field run within its paragraph's <see cref="Paragraph.Runs"/>.</param>
    /// <param name="pageOf">
    /// Optional page-number resolver mapping a target body block index to its 1-based page (for PAGEREF).
    /// Null — or a null return — falls back to "1", since the pure model has no pagination.
    /// </param>
    /// <param name="pageTextOf">
    /// Optional formatted page-text resolver. A non-empty result is authoritative over
    /// <paramref name="pageOf"/> for section restarts and non-decimal page formats.
    /// </param>
    public static string Recompute(
        TextDocument document,
        int blockIndex,
        int runIndex,
        Func<int, int?>? pageOf = null,
        Func<int, string?>? pageTextOf = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (blockIndex < 0 || blockIndex >= document.Blocks.Count)
            return string.Empty;
        if (document.Blocks[blockIndex] is not Paragraph paragraph)
            return string.Empty;
        if (runIndex < 0 || runIndex >= paragraph.Runs.Count)
            return string.Empty;

        var run = paragraph.Runs[runIndex];
        return Recompute(document, blockIndex, run, pageOf, pageTextOf);
    }

    /// <summary>
    /// Recomputes the complex field carried by <paramref name="run"/>. The owning top-level
    /// <paramref name="blockIndex"/> can identify either a body paragraph or a table containing the run;
    /// this lets Update Fields cover Word's complete main-document story without inventing synthetic
    /// run indexes for table-cell paragraphs.
    /// </summary>
    public static string Recompute(
        TextDocument document,
        int blockIndex,
        Run run,
        Func<int, int?>? pageOf = null,
        Func<int, string?>? pageTextOf = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(run);
        if (run.ComplexField is not { } field)
            return run.Text;

        var refreshed = RefreshNestedFields(document, blockIndex, run, field, run.Text, pageOf, pageTextOf);
        field = refreshed.Field;
        run.ComplexField = field;
        run.Text = refreshed.Result;

        var result = field.Keyword switch
        {
            "=" => ResolveFormula(field),
            "REF" => ResolveRef(document, field, run.Text),
            "PAGEREF" => ResolvePageRef(document, field, run.Text, pageOf, pageTextOf),
            "SEQ" => ResolveSeq(document, field, run),
            "CITATION" => Citations.ResolveCitationField(document, field, run.Text),
            "STYLEREF" => ResolveStyleRef(document, field, blockIndex, run.Text),
            "IF" => ResolveIf(document, FieldForIfEvaluation(field), run.Text),
            "DOCPROPERTY" => ResolveDocProperty(document, field, run.Text),
            "DOCVARIABLE" => ResolveDocVariable(document, field, run.Text),
            "CREATEDATE" => ResolveDocumentDate(document, blockIndex, document.Properties.Created, field, run),
            "SAVEDATE" => ResolveDocumentDate(document, blockIndex, document.Properties.Modified, field, run),
            "LASTSAVEDBY" => document.Properties.LastModifiedBy is { } lastSavedBy
                ? ApplyTextGeneralFormats(lastSavedBy, field.Instruction)
                : run.Text,
            "TEMPLATE" => ResolveTemplate(document, field, run.Text),
            "NUMWORDS" => WordCount.Of(document).Words.ToString(CultureInfo.InvariantCulture),
            "NUMCHARS" => WordCount.Of(document).CharactersWithoutSpaces.ToString(CultureInfo.InvariantCulture),
            "REVNUM" => ResolveRevisionNumber(document, field, run.Text),
            "EDITTIME" => ResolveEditTime(document, field, run.Text),
            "PRINTDATE" => ResolveDocumentDate(
                document,
                blockIndex,
                OpcPackageProperties.ParseW3CDtf(ResolveCoreProperty(document, "lastPrinted")),
                field,
                run),
            _ => run.Text
        };

        // Recomputing an outer field materializes a new outer result. Nested fields that belonged to the
        // old cached result no longer own any substring there; instruction-side nested fields remain live.
        if (CanRecomputeKeyword(field.Keyword)
            && field.NestedFields?.Any(nested => nested.Placement == NestedComplexFieldPlacement.Result) == true)
        {
            run.ComplexField = field with
            {
                NestedFields = field.NestedFields
                    .Where(nested => nested.Placement == NestedComplexFieldPlacement.Instruction)
                    .ToArray()
            };
        }

        return result;
    }

    private static string ResolveFormula(ComplexField field)
    {
        var instruction = field.Instruction.AsSpan().Trim();
        if (instruction.Length == 0 || instruction[0] != '=')
            return "!Syntax Error";

        var body = instruction[1..].Trim();
        var switchStart = FormulaSwitchStart(body);
        var expression = (switchStart < 0 ? body : body[..switchStart]).Trim().ToString();
        var numberFormat = SwitchValues(field.Instruction, '#').LastOrDefault();
        return TableFormulaEvaluator.EvaluateLiteralExpression(expression, numberFormat);
    }

    private static int FormulaSwitchStart(ReadOnlySpan<char> text)
    {
        var inQuotes = false;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '"' && (index == 0 || text[index - 1] != '\\'))
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes
                && text[index] == '\\'
                && index + 1 < text.Length
                && text[index + 1] is '#' or '*')
                return index;
        }

        return -1;
    }

    private static ComplexField FieldForIfEvaluation(ComplexField field)
    {
        if (field.NestedFields is not { Count: > 0 } nestedFields)
            return field;

        var instruction = field.Instruction;
        foreach (var nested in nestedFields
                     .Where(item => item.Placement == NestedComplexFieldPlacement.Instruction)
                     .OrderByDescending(item => item.Offset))
        {
            if (nested.Offset < 0
                || nested.Length < 0
                || nested.Offset + nested.Length > instruction.Length)
                continue;
            var operand = $"\"{nested.CachedResult.Replace("\"", "\\\"")}\"";
            instruction = string.Concat(
                instruction.AsSpan(0, nested.Offset),
                operand,
                instruction.AsSpan(nested.Offset + nested.Length));
        }

        return field with { Instruction = instruction };
    }

    private static (ComplexField Field, string Result) RefreshNestedFields(
        TextDocument document,
        int blockIndex,
        Run owner,
        ComplexField field,
        string cachedResult,
        Func<int, int?>? pageOf,
        Func<int, string?>? pageTextOf)
    {
        if (field.NestedFields is not { Count: > 0 } nestedFields)
            return (field, cachedResult);

        var instruction = field.Instruction;
        var result = cachedResult;
        var instructionDelta = 0;
        var resultDelta = 0;
        var updated = new List<NestedComplexField>(nestedFields.Count);

        foreach (var nested in nestedFields
                     .OrderBy(item => item.Placement)
                     .ThenBy(item => item.Offset))
        {
            var nestedRun = new Run(nested.CachedResult, owner.Formatting)
            {
                ComplexField = nested.Field
            };
            var nestedResult = nested.Field.IsLocked
                ? nested.CachedResult
                : Recompute(document, blockIndex, nestedRun, pageOf, pageTextOf);
            nestedRun.Text = nestedResult;

            var delta = nested.Placement == NestedComplexFieldPlacement.Instruction
                ? instructionDelta
                : resultDelta;
            var adjustedOffset = nested.Offset + delta;
            var buffer = nested.Placement == NestedComplexFieldPlacement.Instruction
                ? instruction
                : result;
            if (adjustedOffset < 0 || nested.Length < 0 || adjustedOffset + nested.Length > buffer.Length)
            {
                updated.Add(nested with
                {
                    Field = nestedRun.ComplexField ?? nested.Field,
                    CachedResult = nestedResult
                });
                continue;
            }

            buffer = string.Concat(
                buffer.AsSpan(0, adjustedOffset),
                nestedResult,
                buffer.AsSpan(adjustedOffset + nested.Length));
            var lengthDelta = nestedResult.Length - nested.Length;
            if (nested.Placement == NestedComplexFieldPlacement.Instruction)
            {
                instruction = buffer;
                instructionDelta += lengthDelta;
            }
            else
            {
                result = buffer;
                resultDelta += lengthDelta;
            }

            updated.Add(nested with
            {
                Field = nestedRun.ComplexField ?? nested.Field,
                CachedResult = nestedResult,
                Offset = adjustedOffset,
                Length = nestedResult.Length
            });
        }

        return (field with { Instruction = instruction, NestedFields = updated }, result);
    }

    private static string ResolveEditTime(TextDocument document, ComplexField field, string cached)
    {
        var value = ResolveRawExtendedProperty(document, "TotalTime");
        return int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            && minutes >= 0
                ? FormatIntegerFieldValue(minutes, field.Instruction)
                : cached;
    }

    private static string ResolveRevisionNumber(TextDocument document, ComplexField field, string cached)
    {
        var value = ResolveCoreProperty(document, "revision");
        return int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var revision)
            ? FormatIntegerFieldValue(revision, field.Instruction)
            : cached;
    }

    private static string? ResolveCoreProperty(TextDocument document, string localName) =>
        document.Preserved.OriginalCoreProperties?.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.Ordinal))
            ?.Value;

    private static string ResolveTemplate(TextDocument document, ComplexField field, string cached)
    {
        if (HasSwitch(field.Instruction, 'p'))
        {
            var path = ResolveAttachedTemplatePath(document);
            return path is null ? cached : ApplyTextGeneralFormats(path, field.Instruction);
        }

        var value = ResolveExtendedDocProperty(document, "Template");
        return value is null ? cached : ApplyTextGeneralFormats(value, field.Instruction);
    }

    private static string? ResolveAttachedTemplatePath(TextDocument document)
    {
        var word = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var relationships = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        var relationshipId = document.Preserved.OriginalSettings?
            .Element(word + "attachedTemplate")?
            .Attribute(relationships + "id")?
            .Value;
        if (string.IsNullOrWhiteSpace(relationshipId))
            return null;

        var relationshipPart = document.Preserved.Parts.FirstOrDefault(candidate =>
            candidate.PartName.Equals(SettingsRelationshipsPartName, StringComparison.OrdinalIgnoreCase));
        var relationshipXml = relationshipPart is null ? null : OpcXml.TryLoadXml(relationshipPart.Bytes);
        if (relationshipXml is null)
            return null;

        var relationship = OpcRelationships.Load(relationshipXml).FirstOrDefault(candidate =>
            candidate.Id.Equals(relationshipId, StringComparison.Ordinal)
            && candidate.Type.Equals(AttachedTemplateRelationshipType, StringComparison.Ordinal));
        if (!relationship.IsExternal || string.IsNullOrWhiteSpace(relationship.Target))
            return null;

        return FormatAttachedTemplateTarget(relationship.Target);
    }

    private static string? FormatAttachedTemplateTarget(string target)
    {
        target = target.Trim();
        try
        {
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
                return Path.IsPathFullyQualified(target) ? Uri.UnescapeDataString(target) : null;

            if (!uri.IsFile)
                return Uri.UnescapeDataString(target);

            var path = Uri.UnescapeDataString(uri.AbsolutePath);
            if (!string.IsNullOrEmpty(uri.Host))
                return $@"\\{uri.Host}\{path.TrimStart('/').Replace('/', '\\')}";

            // A Windows file URI has an extra leading slash before its drive letter. Preserve Word's
            // platform-independent display contract instead of relying on Uri.LocalPath from the host OS.
            if (path.Length >= 3 && path[0] == '/' && char.IsLetter(path[1]) && path[2] == ':')
                return path[1..].Replace('/', '\\');

            return path.Replace('/', Path.DirectorySeparatorChar);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static string ResolveDocumentDate(TextDocument document, int blockIndex, DateTimeOffset? value, ComplexField field, Run run)
    {
        if (value is null)
            return run.Text;

        var culture = ResolveFieldCulture(document, blockIndex, run);
        var localValue = value.Value.LocalDateTime;
        return WordFieldDateTimeFormatter.TryFormat(
            localValue,
            field.Instruction,
            culture,
            out var formatted)
            ? formatted
            : localValue.ToString("g", culture);
    }

    // Resolves the effective proofing-language culture for a date field's run the same way Word does:
    // the run's own direct w:lang wins, then the paragraph's based-on style chain, then the document
    // default (docDefaults/w:rPrDefault/w:rPr/w:lang) -- only falling back to the host process culture
    // when the document carries no language information at all. Word stores the language on individual
    // runs only when it differs from what the run would otherwise inherit, so CREATEDATE/SAVEDATE/
    // PRINTDATE fields whose language comes solely from the paragraph style or the document default
    // (the common case) previously fell straight through to CultureInfo.CurrentCulture.
    private static CultureInfo ResolveFieldCulture(TextDocument document, int blockIndex, Run run)
    {
        var tag = run.Formatting.LanguageTag;
        if (string.IsNullOrEmpty(tag)
            && FindOwningParagraph(document, blockIndex, run) is { } paragraph)
            tag = ResolveStyleLanguageTag(document, paragraph.StyleId);
        if (string.IsNullOrEmpty(tag))
            tag = document.DefaultRun.LanguageTag;

        if (!string.IsNullOrEmpty(tag))
        {
            try
            {
                return CultureInfo.GetCultureInfo(tag);
            }
            catch (CultureNotFoundException)
            {
                // Imported language tags can be malformed; fall through to the process culture.
            }
        }

        return CultureInfo.CurrentCulture;
    }

    // Locates the Paragraph that owns `run`, given the top-level block index the caller resolved it
    // from. The block can be either the paragraph itself (the main-document case) or a Table containing
    // it (a table-cell field), per Recompute's own contract. Header/footer/footnote/endnote/comment
    // stories carry no body block index -- DocumentFieldStories reports BodyBlockIndex = -1 for them --
    // so those fall back to a full story walk instead: a date field in a header is exactly as ordinary
    // as one in the body, and should inherit its paragraph's style chain the same way. Nested/synthetic
    // field runs (which are never actually linked into any paragraph's Runs list) resolve to null and
    // fall back to the document default only.
    private static Paragraph? FindOwningParagraph(TextDocument document, int blockIndex, Run run)
    {
        if (blockIndex >= 0 && blockIndex < document.Blocks.Count)
        {
            return document.Blocks[blockIndex] switch
            {
                Paragraph paragraph => paragraph,
                Table table => table.Rows
                    .SelectMany(row => row.Cells)
                    .SelectMany(cell => cell.Paragraphs)
                    .FirstOrDefault(paragraph => paragraph.Runs.Contains(run)),
                _ => null
            };
        }

        return DocumentFieldStories.Enumerate(document)
            .Select(story => story.Paragraph)
            .FirstOrDefault(paragraph => paragraph.Runs.Contains(run));
    }

    // Mirrors Proofing.cs's ResolveStyleNoProof: walks the paragraph style's based-on chain looking for
    // the first style that carries an explicit run-level language tag.
    private static string? ResolveStyleLanguageTag(TextDocument document, string? styleId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (!string.IsNullOrEmpty(styleId)
            && seen.Add(styleId)
            && document.Styles.TryGetValue(styleId, out var style))
        {
            if (!string.IsNullOrEmpty(style.Run.LanguageTag))
                return style.Run.LanguageTag;
            styleId = style.BasedOnStyleId;
        }

        return null;
    }

    private static string ResolveDocProperty(TextDocument document, ComplexField field, string cached)
    {
        var name = Argument(field.Instruction);
        if (name.Length == 0)
            return cached;

        var value = ResolveBuiltInDocProperty(document, name)
            ?? ResolveExtendedDocProperty(document, name)
            ?? ResolveSerializedNameValue(
                document.Preserved.OriginalCustomProperties,
                elementName: "property",
                name,
                valueAttributeName: null);
        return value is null ? cached : ApplyTextGeneralFormats(value, field.Instruction);
    }

    private static string ResolveDocVariable(TextDocument document, ComplexField field, string cached)
    {
        var name = Argument(field.Instruction);
        if (name.Length == 0)
            return cached;

        var value = ResolveSerializedNameValue(
            document.Preserved.OriginalSettings,
            elementName: "docVar",
            name,
            valueAttributeName: "val");
        return value is null ? cached : ApplyTextGeneralFormats(value, field.Instruction);
    }

    private static string? ResolveBuiltInDocProperty(TextDocument document, string name)
    {
        var normalized = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return normalized switch
        {
            "TITLE" => document.Properties.Title,
            "SUBJECT" => document.Properties.Subject,
            "AUTHOR" => document.Properties.Author,
            "KEYWORDS" => document.Properties.Keywords,
            "COMMENTS" => document.Properties.Comments,
            "LASTSAVEDBY" or "LASTAUTHOR" => document.Properties.LastModifiedBy,
            "CATEGORY" => document.Properties.Category,
            "CONTENTSTATUS" => document.Properties.ContentStatus,
            "LANGUAGE" => document.Properties.Language,
            "VERSION" => document.Properties.Version,
            "REVISION" or "REVISIONNUMBER" => ResolveCoreProperty(document, "revision"),
            _ => null
        };
    }

    private static string? ResolveExtendedDocProperty(TextDocument document, string name)
    {
        var normalized = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (normalized is not ("COMPANY" or "MANAGER" or "TEMPLATE"))
            return null;

        var part = document.Preserved.Parts.FirstOrDefault(candidate =>
            candidate.PartName.Equals(OpcPackageProperties.ExtendedPropertiesPartName, StringComparison.OrdinalIgnoreCase));
        if (part is null)
            return null;

        var properties = OpcDocumentProperties.ReadExtendedProperties(OpcXml.TryLoadXml(part.Bytes));
        return normalized switch
        {
            "COMPANY" => properties.Company,
            "MANAGER" => properties.Manager,
            "TEMPLATE" => properties.Template,
            _ => null
        };
    }

    private static string? ResolveRawExtendedProperty(TextDocument document, string localName)
    {
        var part = document.Preserved.Parts.FirstOrDefault(candidate =>
            candidate.PartName.Equals(OpcPackageProperties.ExtendedPropertiesPartName, StringComparison.OrdinalIgnoreCase));
        return part is null
            ? null
            : OpcXml.TryLoadXml(part.Bytes)?.Root?.Elements()
                .FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.Ordinal))
                ?.Value;
    }

    private static string? ResolveSerializedNameValue(
        System.Xml.Linq.XElement? root,
        string elementName,
        string name,
        string? valueAttributeName)
    {
        var element = root?.Descendants()
            .FirstOrDefault(candidate =>
                candidate.Name.LocalName.Equals(elementName, StringComparison.Ordinal)
                && candidate.Attributes().Any(attribute =>
                    attribute.Name.LocalName.Equals("name", StringComparison.Ordinal)
                    && attribute.Value.Equals(name, StringComparison.OrdinalIgnoreCase)));
        if (element is null)
            return null;

        if (valueAttributeName is not null)
        {
            return element.Attributes()
                .FirstOrDefault(attribute =>
                    attribute.Name.LocalName.Equals(valueAttributeName, StringComparison.Ordinal))
                ?.Value;
        }

        return element.Elements().FirstOrDefault()?.Value;
    }

    private static string ApplyTextGeneralFormats(string value, string instruction)
    {
        foreach (var format in SwitchValues(instruction, '*'))
        {
            value = format.ToUpperInvariant() switch
            {
                "UPPER" => value.ToUpperInvariant(),
                "LOWER" => value.ToLowerInvariant(),
                "FIRSTCAP" => CapitalizeFirstLetter(value),
                "CAPS" => CapitalizeWordInitials(value),
                _ => value
            };
        }
        return value;
    }

    private static string CapitalizeFirstLetter(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetter(chars[i]))
                continue;
            chars[i] = char.ToUpperInvariant(chars[i]);
            break;
        }
        return new string(chars);
    }

    private static string CapitalizeWordInitials(string value)
    {
        var chars = value.ToCharArray();
        var atWordStart = true;
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsLetter(chars[i]))
            {
                if (atWordStart)
                    chars[i] = char.ToUpperInvariant(chars[i]);
                atWordStart = false;
            }
            else if (!char.IsDigit(chars[i]) && chars[i] != '\'')
            {
                atWordStart = true;
            }
        }
        return new string(chars);
    }

    private static string ResolveIf(TextDocument document, ComplexField field, string cached)
    {
        if (!TryParseIf(field.Instruction, out var expression1, out var op, out var expression2,
                out var trueText, out var falseText))
            return cached;

        var left = ResolveIfOperand(document, expression1);
        var right = ResolveIfOperand(document, expression2);
        var matches = op switch
        {
            MergeConditionOperator.Equal when ContainsWildcard(right) => WildcardMatches(left, right),
            MergeConditionOperator.NotEqual when ContainsWildcard(right) => !WildcardMatches(left, right),
            _ => MergeRuleEvaluator.EvaluateCondition(left, op, right)
        };
        return matches ? trueText.Value : falseText?.Value ?? string.Empty;
    }

    private static string ResolveIfOperand(TextDocument document, IfToken operand)
    {
        if (operand.IsQuoted)
            return operand.Value;

        // Bookmarks.FindParagraph (not Bookmarks.List + a Blocks[index] cast) so a bookmark nested in a
        // table cell resolves too — List reports the containing table's block index for those, which is
        // never itself a Paragraph.
        if (Bookmarks.FindParagraph(document, operand.Value) is { } target)
            return target.PlainText.TrimEnd();

        return operand.Value;
    }

    private static bool TryParseIf(
        string instruction,
        out IfToken expression1,
        out MergeConditionOperator op,
        out IfToken expression2,
        out IfToken trueText,
        out IfToken? falseText)
    {
        expression1 = default;
        expression2 = default;
        trueText = default;
        falseText = null;
        op = default;

        var text = instruction.AsSpan().Trim();
        if (text.Length < 2 || !text[..2].Equals("IF".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || (text.Length > 2 && !char.IsWhiteSpace(text[2])))
            return false;

        var cursor = 2;
        SkipWhiteSpace(text, ref cursor);
        if (!TryReadIfToken(text, ref cursor, stopAtOperator: true, out expression1))
            return false;

        SkipWhiteSpace(text, ref cursor);
        if (!TryReadIfOperator(text, ref cursor, out op))
            return false;

        SkipWhiteSpace(text, ref cursor);
        if (!TryReadIfToken(text, ref cursor, stopAtOperator: false, out expression2))
            return false;

        SkipWhiteSpace(text, ref cursor);
        if (!TryReadIfToken(text, ref cursor, stopAtOperator: false, out trueText))
            return false;

        SkipWhiteSpace(text, ref cursor);
        if (cursor < text.Length)
        {
            if (text[cursor] == '\\')
                return TryConsumeIfRetentionSwitch(text, ref cursor);

            if (!TryReadIfToken(text, ref cursor, stopAtOperator: false, out var parsedFalseText))
                return false;
            falseText = parsedFalseText;
            SkipWhiteSpace(text, ref cursor);
            if (cursor < text.Length)
                return TryConsumeIfRetentionSwitch(text, ref cursor);
        }

        return cursor == text.Length;
    }

    private static bool TryConsumeIfRetentionSwitch(ReadOnlySpan<char> text, ref int cursor)
    {
        if (!text[cursor..].StartsWith("\\*".AsSpan(), StringComparison.Ordinal))
            return false;

        cursor += 2;
        SkipWhiteSpace(text, ref cursor);
        var start = cursor;
        while (cursor < text.Length && !char.IsWhiteSpace(text[cursor]))
            cursor++;
        var format = text[start..cursor];
        if (!format.Equals("MERGEFORMAT".AsSpan(), StringComparison.OrdinalIgnoreCase)
            && !format.Equals("CHARFORMAT".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;

        SkipWhiteSpace(text, ref cursor);
        return cursor == text.Length;
    }

    private static bool TryReadIfToken(
        ReadOnlySpan<char> text,
        ref int cursor,
        bool stopAtOperator,
        out IfToken token)
    {
        token = default;
        if (cursor >= text.Length)
            return false;

        if (text[cursor] == '"')
        {
            var value = new System.Text.StringBuilder();
            cursor++;
            while (cursor < text.Length)
            {
                if (text[cursor] == '\\' && cursor + 1 < text.Length && text[cursor + 1] == '"')
                {
                    value.Append('"');
                    cursor += 2;
                    continue;
                }

                if (text[cursor] == '"')
                {
                    cursor++;
                    token = new IfToken(value.ToString(), IsQuoted: true);
                    return true;
                }

                if (text[cursor] is '{' or '}' || char.IsControl(text[cursor]))
                    return false;
                value.Append(text[cursor++]);
            }

            return false;
        }

        var start = cursor;
        while (cursor < text.Length
               && !char.IsWhiteSpace(text[cursor])
               && (!stopAtOperator || text[cursor] is not ('=' or '<' or '>')))
        {
            if (text[cursor] is '{' or '}' || char.IsControl(text[cursor]))
                return false;
            cursor++;
        }

        if (cursor == start)
            return false;
        token = new IfToken(text[start..cursor].ToString(), IsQuoted: false);
        return true;
    }

    private static bool TryReadIfOperator(
        ReadOnlySpan<char> text,
        ref int cursor,
        out MergeConditionOperator op)
    {
        op = default;
        foreach (var candidate in new[] { "<>", ">=", "<=", "=", ">", "<" })
        {
            if (!text[cursor..].StartsWith(candidate.AsSpan(), StringComparison.Ordinal))
                continue;

            cursor += candidate.Length;
            op = candidate switch
            {
                "=" => MergeConditionOperator.Equal,
                "<>" => MergeConditionOperator.NotEqual,
                ">" => MergeConditionOperator.GreaterThan,
                "<" => MergeConditionOperator.LessThan,
                ">=" => MergeConditionOperator.GreaterThanOrEqual,
                _ => MergeConditionOperator.LessThanOrEqual
            };
            return true;
        }

        return false;
    }

    private static void SkipWhiteSpace(ReadOnlySpan<char> text, ref int cursor)
    {
        while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
            cursor++;
    }

    private static bool ContainsWildcard(string pattern) =>
        pattern.IndexOfAny(['*', '?']) >= 0;

    private static bool WildcardMatches(string value, string pattern)
    {
        var previous = new bool[value.Length + 1];
        previous[0] = true;
        foreach (var patternCharacter in pattern)
        {
            var current = new bool[value.Length + 1];
            if (patternCharacter == '*')
                current[0] = previous[0];

            for (var valueIndex = 1; valueIndex <= value.Length; valueIndex++)
            {
                current[valueIndex] = patternCharacter switch
                {
                    '*' => previous[valueIndex] || current[valueIndex - 1],
                    '?' => previous[valueIndex - 1],
                    _ => previous[valueIndex - 1]
                         && char.ToUpperInvariant(patternCharacter)
                         == char.ToUpperInvariant(value[valueIndex - 1])
                };
            }

            previous = current;
        }

        return previous[value.Length];
    }

    private readonly record struct IfToken(string Value, bool IsQuoted);

    /// <summary>
    /// The first non-switch argument of <paramref name="instruction"/> after its leading keyword — e.g.
    /// the bookmark name of <c>REF MyMark \h</c> or the sequence name of <c>SEQ Figure \* ARABIC</c>.
    /// Honours simple double-quoting. Returns "" when the field has no argument.
    /// </summary>
    public static string Argument(string instruction)
    {
        foreach (var token in Tokenize(instruction))
        {
            if (token.StartsWith('\\'))
                break; // switches start here; the identifier (if any) comes before them
            return token;
        }
        return string.Empty;
    }

    /// <summary>
    /// Replaces the first non-switch argument after a field keyword while preserving the keyword,
    /// spacing, and all following switches. Returns the original instruction when it has no argument.
    /// </summary>
    internal static string ReplaceArgument(string instruction, string replacement)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(replacement);

        var cursor = 0;
        while (cursor < instruction.Length && char.IsWhiteSpace(instruction[cursor]))
            cursor++;
        while (cursor < instruction.Length && !char.IsWhiteSpace(instruction[cursor]))
            cursor++;
        while (cursor < instruction.Length && char.IsWhiteSpace(instruction[cursor]))
            cursor++;

        if (cursor >= instruction.Length || instruction[cursor] == '\\')
            return instruction;

        var argumentStart = cursor;
        if (instruction[cursor] == '"')
        {
            cursor++;
            var closed = false;
            while (cursor < instruction.Length)
            {
                if (instruction[cursor] == '\\' && cursor + 1 < instruction.Length)
                {
                    cursor += 2;
                    continue;
                }

                if (instruction[cursor++] == '"')
                {
                    closed = true;
                    break;
                }
            }

            if (!closed)
                return instruction;
        }
        else
        {
            while (cursor < instruction.Length && !char.IsWhiteSpace(instruction[cursor]))
                cursor++;
        }

        var quoted = replacement.Any(char.IsWhiteSpace) || replacement.Contains('"', StringComparison.Ordinal);
        var serialized = quoted
            ? "\"" + replacement.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : replacement;
        return instruction[..argumentStart] + serialized + instruction[cursor..];
    }

    /// <summary>
    /// True when <paramref name="instruction"/> carries the switch letter <paramref name="letter"/>
    /// (e.g. <c>'c'</c> for SEQ's <c>\c</c>), case-insensitively. The leading keyword/argument are skipped.
    /// </summary>
    public static bool HasSwitch(string instruction, char letter)
    {
        var target = char.ToUpperInvariant(letter);
        foreach (var token in Tokenize(instruction))
        {
            if (token.Length == 2 && token[0] == '\\' && char.ToUpperInvariant(token[1]) == target)
                return true;
        }
        return false;
    }

    /// <summary>
    /// The value following the switch <paramref name="letter"/> (e.g. the <c>N</c> of SEQ's <c>\r N</c>),
    /// or null when the switch is absent or has no following value. Honours double-quoting.
    /// </summary>
    public static string? SwitchValue(string instruction, char letter)
    {
        var target = char.ToUpperInvariant(letter);
        var tokens = Tokenize(instruction).ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Length == 2 && tokens[i][0] == '\\' && char.ToUpperInvariant(tokens[i][1]) == target)
                return i + 1 < tokens.Count && !tokens[i + 1].StartsWith('\\') ? tokens[i + 1] : null;
        }
        return null;
    }

    /// <summary>
    /// Every value following repeated occurrences of switch <paramref name="letter"/>, in instruction
    /// order. This is required for Word's repeatable general-format switch (<c>\*</c>), where
    /// <c>MERGEFORMAT</c> can coexist with a text or numeric result format.
    /// </summary>
    public static IReadOnlyList<string> SwitchValues(string instruction, char letter)
    {
        var target = char.ToUpperInvariant(letter);
        var tokens = Tokenize(instruction).ToList();
        var values = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Length == 2 && tokens[i][0] == '\\' && char.ToUpperInvariant(tokens[i][1]) == target)
            {
                if (i + 1 < tokens.Count && !tokens[i + 1].StartsWith('\\'))
                    values.Add(tokens[i + 1]);
            }
        }
        return values;
    }

    // REF: the text of the paragraph that carries the referenced bookmark, trimmed of trailing blanks.
    // Unresolvable (no such bookmark) falls back to the cached text so the field never blanks.
    private static string ResolveRef(TextDocument document, ComplexField field, string cached)
    {
        var name = Argument(field.Instruction);
        if (name.Length == 0)
            return cached;
        // Bookmarks.FindParagraph (not Bookmarks.List + a Blocks[index] cast) so a bookmark nested in a
        // table cell resolves too — List reports the containing table's block index for those, which is
        // never itself a Paragraph.
        if (Bookmarks.FindParagraph(document, name) is { } target)
        {
            var text = target.PlainText.TrimEnd();
            return text.Length > 0 ? text : cached;
        }
        return cached;
    }

    // PAGEREF: the page number of the referenced bookmark's paragraph, via the shared canonical walk in
    // BookmarkPageResolution (row-aware for a table, and reaching headers/footers/footnotes/endnotes/text
    // boxes too); "1" when no page is known (the pure model has no pagination). A bookmark that resolves
    // to no target at all, or to one of those block-less stories where no page can be attributed, falls
    // back to cached text -- headers/footers/footnotes/endnotes/comments have no page of their own to
    // report, so treating "found there" the same as "not found" here preserves the field's last-known
    // cached value instead of overwriting it with a misleading "1".
    private static string ResolvePageRef(
        TextDocument document,
        ComplexField field,
        string cached,
        Func<int, int?>? pageOf,
        Func<int, string?>? pageTextOf)
    {
        var name = Argument(field.Instruction);
        if (name.Length == 0)
            return cached;

        if (BookmarkPageResolution.Find(document, name) is not { BlockIndex: >= 0 } target)
            return cached;

        return BookmarkPageResolution.ResolvePageText(document, target, pageOf, pageTextOf);
    }

    // SEQ: the running counter for this sequence name across the complete main-document story, including
    // table-cell paragraphs. \r N resets at the current field; \c repeats; \n explicitly advances; and
    // \s N resets the first matching sequence after a heading at level N or higher. Word consumes a
    // pending heading when it encounters that sequence name even if the field's own \s level does not match.
    private static string ResolveSeq(TextDocument document, ComplexField field, Run targetRun)
    {
        var name = Argument(field.Instruction);
        if (name.Length == 0)
            return targetRun.Text;
        // Word ignores \h when a recognized numeric result picture is present, but MERGEFORMAT alone does
        // not make the hidden result visible.
        var hidden = HasSwitch(field.Instruction, 'h') && SequencePicture(field.Instruction) is null;

        var value = 0;
        int? pendingHeadingLevel = null;
        foreach (var (_, paragraph, _) in DocumentBodyParagraphs.Enumerate(document))
        {
            if (HeadingLevel(document, paragraph) is { } headingLevel)
                pendingHeadingLevel = Math.Min(pendingHeadingLevel ?? headingLevel, headingLevel);

            for (var r = 0; r < paragraph.Runs.Count; r++)
            {
                if (paragraph.Runs[r].ComplexField is not { } cf
                    || cf.Keyword != "SEQ"
                    || !string.Equals(Argument(cf.Instruction), name, StringComparison.Ordinal))
                    continue;

                var restartLevel = SeqRestartLevel(cf.Instruction);
                if (restartLevel is { } level
                    && pendingHeadingLevel is { } precedingLevel
                    && precedingLevel <= level)
                {
                    value = 0;
                }

                var resetTo = SeqReset(cf.Instruction);
                var repeat = HasSwitch(cf.Instruction, 'c');
                if (resetTo is { } reset)
                    value = reset;             // \r N restarts the running value at N for this field
                else if (!repeat)
                    value++;                   // ordinary SEQ advances; \c repeats the current value

                pendingHeadingLevel = null;
                if (ReferenceEquals(paragraph.Runs[r], targetRun))
                    return hidden ? string.Empty : FormatIntegerFieldValue(value, field.Instruction);
            }
        }
        // The target field was not found among the document's SEQ fields (shouldn't happen for an in-doc
        // field): fall back to a bare first ordinal.
        return hidden ? string.Empty : FormatIntegerFieldValue(1, field.Instruction);
    }

    /// <summary>
    /// Formats an integer result using the supported Word general numeric field pictures. Page-context
    /// fields such as SECTION and SECTIONPAGES share Arabic, Roman, and alphabetic pictures with SEQ/REVNUM.
    /// </summary>
    public static string FormatIntegerFieldValue(int value, string instruction) => SequencePicture(instruction) switch
    {
        "ROMAN" => ToRoman(value),
        "roman" => ToRoman(value).ToLowerInvariant(),
        "ALPHABETIC" => ToAlphabetic(value, lower: false),
        "alphabetic" => ToAlphabetic(value, lower: true),
        _ => value.ToString(CultureInfo.InvariantCulture)
    };

    private static string? SequencePicture(string instruction)
    {
        string? picture = null;
        var tokens = Tokenize(instruction).ToList();
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            if (tokens[i] != "\\*" || tokens[i + 1].StartsWith('\\'))
                continue;

            if (tokens[i + 1] is "Arabic" or "ARABIC" or "ROMAN" or "roman" or "ALPHABETIC" or "alphabetic")
                picture = tokens[i + 1];
        }
        return picture;
    }

    private static string ToRoman(int value)
    {
        if (value is <= 0 or > 3999)
            return value.ToString(CultureInfo.InvariantCulture);

        (int Value, string Symbol)[] map =
        [
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
        ];
        var remaining = value;
        var result = new System.Text.StringBuilder();
        foreach (var (number, symbol) in map)
        {
            while (remaining >= number)
            {
                result.Append(symbol);
                remaining -= number;
            }
        }
        return result.ToString();
    }

    private static string ToAlphabetic(int value, bool lower)
    {
        if (value <= 0)
            return value.ToString(CultureInfo.InvariantCulture);

        var chars = new List<char>();
        var current = value;
        while (current > 0)
        {
            current--;
            chars.Insert(0, (char)((lower ? 'a' : 'A') + current % 26));
            current /= 26;
        }
        return new string(chars.ToArray());
    }

    // The integer reset value of a SEQ \r switch (e.g. "\r 5" → 5), or null when absent/unparseable.
    private static int? SeqReset(string instruction) =>
        SwitchValue(instruction, 'r') is { } raw
        && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : (int?)null;

    private static int? SeqRestartLevel(string instruction) =>
        SwitchValue(instruction, 's') is { } raw
        && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
        && n is >= 1 and <= 9
            ? n
            : (int?)null;

    private static int? HeadingLevel(TextDocument document, Paragraph paragraph)
    {
        if (paragraph.StyleId is not { Length: > 0 } styleId)
            return null;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (visited.Add(styleId))
        {
            if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(styleId[7..], NumberStyles.None, CultureInfo.InvariantCulture, out var level)
                && level is >= 1 and <= 9)
            {
                return level;
            }

            if (!document.Styles.TryGetValue(styleId, out var style))
                return null;
            if (style.OutlineLevel is >= 0 and <= 8)
                return style.OutlineLevel.Value + 1;
            if (style.BasedOnStyleId is not { Length: > 0 } basedOnStyleId)
                return null;

            styleId = basedOnStyleId;
        }

        return null;
    }

    // STYLEREF: nearest preceding body paragraph matching the requested style, then the first following
    // match when none precedes it. The \n switch returns that paragraph's outline number (e.g. "1.2")
    // instead of its text (e.g. Word's "Include chapter number" caption numbering, "{ STYLEREF 1 \n }").
    // Page-aware/header-footer behavior and other switches remain cached.
    private static string ResolveStyleRef(TextDocument document, ComplexField field, int blockIndex, string cached)
    {
        var argument = Argument(field.Instruction);
        if (argument.Length == 0)
            return cached;

        var headingStyleId = argument.Length == 1 && argument[0] is >= '1' and <= '9'
            ? "Heading" + argument
            : null;
        var wantsNumber = HasSwitch(field.Instruction, 'n');

        for (var b = Math.Min(blockIndex - 1, document.Blocks.Count - 1); b >= 0; b--)
        {
            if (document.Blocks[b] is not Paragraph paragraph
                || !StyleRefMatches(document, paragraph, argument, headingStyleId))
                continue;

            return StyleRefResult(document, paragraph, b, wantsNumber, cached);
        }

        for (var b = Math.Max(0, blockIndex + 1); b < document.Blocks.Count; b++)
        {
            if (document.Blocks[b] is not Paragraph paragraph
                || !StyleRefMatches(document, paragraph, argument, headingStyleId))
                continue;

            return StyleRefResult(document, paragraph, b, wantsNumber, cached);
        }

        return cached;
    }

    // The STYLEREF result for a matched paragraph: its outline number when \n was requested and the
    // paragraph carries one (reusing CrossReferences' heading-number computation, the same one that
    // backs REF's \n/\w "Insert as Heading number" switch), otherwise falling back to its plain text.
    private static string StyleRefResult(
        TextDocument document, Paragraph paragraph, int blockIndex, bool wantsNumber, string cached)
    {
        if (wantsNumber)
        {
            var number = CrossReferences.HeadingNumberAt(document, blockIndex);
            if (number.Length > 0)
                return number;
        }

        var text = paragraph.PlainText.TrimEnd();
        return text.Length > 0 ? text : cached;
    }

    private static bool StyleRefMatches(
        TextDocument document, Paragraph paragraph, string argument, string? headingStyleId)
    {
        if (paragraph.StyleId is not { Length: > 0 } styleId)
            return false;

        if (headingStyleId is not null)
            return string.Equals(styleId, headingStyleId, StringComparison.OrdinalIgnoreCase);

        if (string.Equals(styleId, argument, StringComparison.OrdinalIgnoreCase))
            return true;

        return document.Styles.TryGetValue(styleId, out var style)
            && string.Equals(style.Name, argument, StringComparison.OrdinalIgnoreCase);
    }

    internal static string? FirstArgument(string instruction) =>
        Tokenize(instruction).FirstOrDefault(token => !token.StartsWith('\\'));

    // Splits a field instruction into whitespace-separated tokens, skipping the leading keyword, honouring
    // double-quoted spans (so a quoted argument with spaces stays one token) and splitting a "\x" switch
    // letter from a following value. The leading keyword is dropped so callers see only argument/switches.
    private static IEnumerable<string> Tokenize(string instruction)
    {
        var text = instruction.Trim();
        var i = 0;
        var first = true;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            if (i >= text.Length)
                yield break;

            string token;
            if (text[i] == '"')
            {
                var value = new System.Text.StringBuilder();
                i++;
                while (i < text.Length)
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        value.Append(text[i + 1]);
                        i += 2;
                        continue;
                    }

                    if (text[i] == '"')
                    {
                        i++;
                        break;
                    }

                    value.Append(text[i]);
                    i++;
                }

                token = value.ToString();
            }
            else
            {
                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '"')
                    i++;
                token = text[start..i];
            }

            if (first)
            {
                first = false; // drop the leading keyword (REF/PAGEREF/SEQ/…)
                continue;
            }
            yield return token;
        }
    }
}
