using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace FreeP.App.Compositor.MathLayout;

// ── OMML → MathNode parser (Theme 27) ────────────────────────────────────────
//
// Parses raw OMML XML (the m:oMath element, or the a14:m / mc:AlternateContent
// wrapper that contains it) into a MathNode tree.
//
// Namespace: http://schemas.openxmlformats.org/officeDocument/2006/math
//
// Only the common constructs are handled explicitly; anything else collapses
// into a MathNode.Unknown with flattened m:t text.

public static class OmmlParser
{
    private static readonly XNamespace M  = "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private static readonly XNamespace A14 = "http://schemas.microsoft.com/office/drawing/2010/main";
    private static readonly XNamespace MC  = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>
    /// PowerPoint's default equation font when an equation does not author an
    /// explicit <c>m:mathFont</c>. Generic parser callers can still choose
    /// different document defaults through <see cref="Parse"/>.
    /// </summary>
    public static MathNode.MathProperties PowerPointDocumentDefaults { get; } =
        new(MathFontFamily: "Cambria Math");

    public static MathNode ParsePowerPoint(string rawXml, string fallbackText) =>
        Parse(rawXml, fallbackText, PowerPointDocumentDefaults);

    public static MathNode ParsePowerPoint(
        string rawXml,
        string fallbackText,
        MathNode.MathProperties? authoredDefaults) =>
        Parse(rawXml, fallbackText, PowerPointDocumentDefaults.Overlay(authoredDefaults));

    // ── Public entry point ────────────────────────────────────────────────

    /// <summary>
    /// Parses the raw OMML XML string stored on a <c>MathRunInfo.RawXml</c> into a
    /// <see cref="MathNode"/> tree.  Returns a <see cref="MathNode.Unknown"/> with the
    /// plain-text fallback if parsing fails.
    /// </summary>
    public static MathNode Parse(
        string rawXml,
        string fallbackText,
        MathNode.MathProperties? documentDefaults = null)
    {
        if (string.IsNullOrWhiteSpace(rawXml))
            return new MathNode.Unknown(fallbackText);

        try
        {
            var root = XElement.Parse(rawXml);
            // Locate the math root regardless of the wrapper form.
            var mathRoot = LocateMathRoot(root);
            if (mathRoot is null)
                return new MathNode.Unknown(fallbackText);

            var inheritedProperties = (documentDefaults ?? new MathNode.MathProperties())
                .Overlay(ParseInheritedMathProperties(mathRoot))
                .Overlay(ParseMathProperties(mathRoot.Element(M + "mathPr")));

            return mathRoot.Name == M + "oMathPara"
                ? ParseMathParagraph(mathRoot, inheritedProperties)
                : WrapWithInheritedProperties(ParseRow(mathRoot, inheritedProperties), inheritedProperties);
        }
        catch
        {
            return new MathNode.Unknown(fallbackText);
        }
    }

    // ── Locate m:oMath ────────────────────────────────────────────────────

    private static XElement? LocateMathRoot(XElement root)
    {
        if (root.Name == M + "oMathPara") return root;

        // Direct m:oMath root (e.g. the element was already the oMath).
        if (root.Name == M + "oMath") return root;

        // a14:m wrapper
        if (root.Name == A14 + "m")
        {
            // The a14:m element itself contains m:oMath as a child (or m:oMathPara).
            return root.Element(M + "oMathPara")
                ?? root.Element(M + "oMath")
                ?? root.Descendants(M + "oMathPara").FirstOrDefault()
                ?? root.Descendants(M + "oMath").FirstOrDefault();
        }

        // mc:AlternateContent wrapper — use mc:Choice
        if (root.Name == MC + "AlternateContent")
        {
            var choice = root.Element(MC + "Choice");
            return choice?.Descendants(M + "oMathPara").FirstOrDefault()
                ?? choice?.Descendants(M + "oMath").FirstOrDefault()
                ?? root.Descendants(M + "oMathPara").FirstOrDefault()
                ?? root.Descendants(M + "oMath").FirstOrDefault();
        }

        // Fallback: search descendants
        return root.Descendants(M + "oMathPara").FirstOrDefault()
            ?? root.Descendants(M + "oMath").FirstOrDefault();
    }

    private static MathNode ParseMathParagraph(
        XElement paragraph,
        MathNode.MathProperties inheritedProperties)
    {
        var paragraphProperties = paragraph.Element(M + "oMathParaPr");
        // ECMA-376 defines brkBin/brkBinSub under m:mathPr. Some authored
        // payloads place them beside jc in oMathParaPr, so accept both forms.
        var mathProperties = paragraph.Element(M + "mathPr");
        var resolvedProperties = inheritedProperties.Overlay(ParseMathProperties(mathProperties));
        var oMathNodes = paragraph.Elements(M + "oMath")
            .Select(oMath => ParseRow(
                oMath,
                resolvedProperties.Overlay(ParseMathProperties(oMath.Element(M + "mathPr")))))
            .ToArray();
        var alignmentPointIndices = oMathNodes
            .Select(GetAlignmentPointIndex)
            .ToArray();

        var content = oMathNodes.Length switch
        {
            0 => ParseRow(paragraph, resolvedProperties),
            1 => oMathNodes[0],
            _ => new MathNode.EqArray(
                oMathNodes,
                alignmentPointIndices,
                alignRowsLeft: true)
        };

        return new MathNode.MathParagraph(
            content,
            ParseMathParagraphJustification(
                paragraphProperties,
                resolvedProperties.DisplayDefaults == true
                    ? resolvedProperties.DefaultJustification
                    : null),
            ParseMathParagraphBinaryBreak(paragraphProperties, resolvedProperties),
            ParseMathParagraphBinarySubtraction(paragraphProperties, resolvedProperties),
            resolvedProperties.MathFontFamily,
            resolvedProperties.SmallFraction,
            resolvedProperties.DisplayDefaults == true ? resolvedProperties.LeftMarginTwips : null,
            resolvedProperties.DisplayDefaults == true ? resolvedProperties.RightMarginTwips : null,
            resolvedProperties.WrapIndentTwips,
            resolvedProperties.WrapRight,
            wrapPropertiesEnabled: resolvedProperties.DisplayDefaults == true,
            interSpacingTwips: resolvedProperties.InterSpacingTwips,
            usesInterEquationSpacing: oMathNodes.Length > 1);
    }

    private static MathNode.MathProperties ParseInheritedMathProperties(XElement mathRoot)
    {
        var inheritedProperties = new MathNode.MathProperties();
        foreach (var ancestor in mathRoot.Ancestors().Reverse())
        {
            var properties = ancestor.Element(M + "mathPr");
            if (properties is not null)
                inheritedProperties = inheritedProperties.Overlay(ParseMathProperties(properties));
        }

        return inheritedProperties;
    }

    private static MathNode.MathProperties ParseMathProperties(XElement? mathProperties)
    {
        if (mathProperties is null)
            return new MathNode.MathProperties();

        var mathFont = ReadVal(mathProperties.Element(M + "mathFont"))?.Trim();
        return new MathNode.MathProperties(
            ParseBinaryBreakOverride(mathProperties),
            ParseBinarySubtractionOverride(mathProperties),
            string.IsNullOrWhiteSpace(mathFont) ? null : mathFont,
            ParseSmallFractionOverride(mathProperties),
            ParseDefaultJustificationOverride(mathProperties),
            ParseLimitLocationOverride(
                mathProperties.Element(M + "intLim"),
                MathNode.MathLimitLocation.SubSup),
            ParseLimitLocationOverride(
                mathProperties.Element(M + "naryLim"),
                MathNode.MathLimitLocation.UndOvr),
            ParseDisplayDefaultsOverride(mathProperties),
            ParseTwipsMeasureOverride(mathProperties.Element(M + "lMargin")),
            ParseTwipsMeasureOverride(mathProperties.Element(M + "rMargin")),
            ParseTwipsMeasureOverride(mathProperties.Element(M + "wrapIndent")),
            ParseOnOffOverride(mathProperties.Element(M + "wrapRight")),
            ParseInterSpacingOverride(mathProperties.Element(M + "interSp")));
    }

    private static int? ParseTwipsMeasureOverride(XElement? element)
    {
        if (element is null)
            return null;

        var value = ReadVal(element)?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return 1440;

        // Invalid ST_TwipsMeasure values use the no-margin fallback while still
        // overriding a lower-precedence authored value.
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var twips)
            && twips >= 0
            ? twips
            : 0;
    }

    private static int? ParseInterSpacingOverride(XElement? element)
    {
        if (element is null)
            return null;

        var value = ReadVal(element)?.Trim();
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var twips)
            && twips >= 0
            ? twips
            : null;
    }

    private static MathNode.MathLimitLocation? ParseLimitLocationOverride(
        XElement? element,
        MathNode.MathLimitLocation valuelessDefault)
    {
        if (element is null)
            return null;

        var value = ReadVal(element)?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return valuelessDefault;

        return value.Equals("undOvr", StringComparison.OrdinalIgnoreCase)
            ? MathNode.MathLimitLocation.UndOvr
            : value.Equals("subSup", StringComparison.OrdinalIgnoreCase)
                ? MathNode.MathLimitLocation.SubSup
                // ST_LimLoc has no other legal values. Use the property's
                // documented fallback for malformed authored values.
                : valuelessDefault;
    }

    private static bool? ParseSmallFractionOverride(XElement mathProperties)
    {
        var element = mathProperties.Element(M + "smallFrac");
        if (element is null)
            return null;

        var value = element.Attribute(M + "val")?.Value
            ?? element.Attribute("val")?.Value
            ?? element.Value;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return value.Trim().ToLowerInvariant() switch
        {
            "0" or "false" or "off" => false,
            _ => true
        };
    }

    private static MathNode.MathParagraphJustification? ParseDefaultJustificationOverride(
        XElement mathProperties)
    {
        var element = mathProperties.Element(M + "defJc");
        if (element is null)
            return null;

        // ECMA-376 makes centerGroup the default for both an omitted val and
        // a val-less m:defJc. Invalid authored values are also kept on the
        // safe, existing centered behavior rather than changing layout.
        var value = ReadVal(element);
        return ParseJustificationValue(
            value,
            MathNode.MathParagraphJustification.CenterGroup);
    }

    private static bool? ParseDisplayDefaultsOverride(XElement mathProperties) =>
        ParseOnOffOverride(mathProperties.Element(M + "dispDef"));

    private static bool? ParseOnOffOverride(XElement? element)
    {
        if (element is null)
            return null;

        var value = ReadVal(element);
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return value.Trim().ToLowerInvariant() switch
        {
            "0" or "false" or "off" => false,
            // CT_OnOff uses true for a val-less element. Keep malformed
            // authored values on the same conservative fallback as the
            // parser's existing OMML binary properties.
            _ => true,
        };
    }

    private static MathNode.MathParagraphBinaryBreak? ParseBinaryBreakOverride(XElement mathProperties)
    {
        var element = mathProperties.Element(M + "brkBin");
        if (element is null)
            return null;

        return ReadVal(element) switch
        {
            "after" => MathNode.MathParagraphBinaryBreak.After,
            "repeat" => MathNode.MathParagraphBinaryBreak.Repeat,
            _ => MathNode.MathParagraphBinaryBreak.Before
        };
    }

    private static MathNode.MathParagraphBinarySubtraction? ParseBinarySubtractionOverride(XElement mathProperties)
    {
        var element = mathProperties.Element(M + "brkBinSub");
        if (element is null)
            return null;

        return ReadVal(element) switch
        {
            "+-" or "plusMinus" => MathNode.MathParagraphBinarySubtraction.PlusMinus,
            "-+" or "minusPlus" => MathNode.MathParagraphBinarySubtraction.MinusPlus,
            _ => MathNode.MathParagraphBinarySubtraction.MinusMinus
        };
    }

    private static MathNode WrapWithInheritedProperties(
        MathNode content,
        MathNode.MathProperties properties) =>
        properties.HasValues
            ? new MathNode.MathRoot(content, properties)
            : content;

    private static MathNode.MathParagraphJustification ParseMathParagraphJustification(
        XElement? paragraphProperties,
        MathNode.MathParagraphJustification? defaultJustification)
    {
        var element = paragraphProperties?.Element(M + "jc");
        if (element is null)
            return defaultJustification ?? MathNode.MathParagraphJustification.CenterGroup;

        var value = ReadVal(element);
        return string.IsNullOrWhiteSpace(value)
            ? MathNode.MathParagraphJustification.CenterGroup
            : ParseJustificationValue(value, MathNode.MathParagraphJustification.Center);
    }

    private static MathNode.MathParagraphJustification ParseJustificationValue(
        string? value,
        MathNode.MathParagraphJustification fallback)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "left" => MathNode.MathParagraphJustification.Left,
            "right" => MathNode.MathParagraphJustification.Right,
            "center" or "centre" => MathNode.MathParagraphJustification.Center,
            "centergroup" or "center-group" or "centre-group" or "centregroup" =>
                MathNode.MathParagraphJustification.CenterGroup,
            _ => fallback
        };
    }

    private static MathNode.MathParagraphBinaryBreak ParseMathParagraphBinaryBreak(
        XElement? paragraphProperties,
        MathNode.MathProperties mathProperties)
    {
        var element = paragraphProperties?.Element(M + "brkBin");
        if (element is not null)
        {
            return ReadVal(element) switch
            {
                "after" => MathNode.MathParagraphBinaryBreak.After,
                "repeat" => MathNode.MathParagraphBinaryBreak.Repeat,
                _ => MathNode.MathParagraphBinaryBreak.Before
            };
        }

        return mathProperties.BinaryBreak ?? MathNode.MathParagraphBinaryBreak.Before;
    }

    private static MathNode.MathParagraphBinarySubtraction ParseMathParagraphBinarySubtraction(
        XElement? paragraphProperties,
        MathNode.MathProperties mathProperties)
    {
        var element = paragraphProperties?.Element(M + "brkBinSub");
        if (element is not null)
        {
            return ReadVal(element) switch
            {
                "+-" or "plusMinus" => MathNode.MathParagraphBinarySubtraction.PlusMinus,
                "-+" or "minusPlus" => MathNode.MathParagraphBinarySubtraction.MinusPlus,
                _ => MathNode.MathParagraphBinarySubtraction.MinusMinus
            };
        }

        return mathProperties.BinarySubtraction ?? MathNode.MathParagraphBinarySubtraction.MinusMinus;
    }

    // ── Row: parse a list of child elements into a Row or single node ─────

    private static MathNode ParseRow(
        XElement container,
        MathNode.MathProperties inheritedProperties)
    {
        var children = new List<MathNode>();
        List<MathNode>? rows = null;
        List<int?>? alignmentPointIndices = null;
        int? currentAlignmentPointIndex = null;
        int? argumentSizeAdjustment = null;

        foreach (var child in container.Elements())
        {
            // Property containers are consumed by the root/paragraph resolver;
            // they are not renderable math nodes when a row is parsed.
            if (child.Name == M + "mathPr" || child.Name == M + "oMathParaPr")
                continue;

            if (TryReadArgumentSize(child, out var childArgumentSizeAdjustment))
            {
                argumentSizeAdjustment = childArgumentSizeAdjustment;
                continue;
            }

            if (TryReadManualBreak(child, out var breakAlignmentPointIndex))
            {
                if (children.Count > 0)
                {
                    rows ??= new List<MathNode>();
                    alignmentPointIndices ??= new List<int?>();
                    rows.Add(CreateRow(children));
                    alignmentPointIndices.Add(currentAlignmentPointIndex);
                    children.Clear();
                }

                currentAlignmentPointIndex = breakAlignmentPointIndex;
            }

            var node = ParseElement(child, inheritedProperties);
            if (node is not null)
                children.Add(node);
        }

        if (rows is not null)
        {
            rows.Add(CreateRow(children));
            alignmentPointIndices!.Add(currentAlignmentPointIndex);
            return ApplyArgumentSize(
                new MathNode.EqArray(rows, alignmentPointIndices),
                argumentSizeAdjustment);
        }

        return ApplyArgumentSize(CreateRow(children), argumentSizeAdjustment);
    }

    // ── Dispatcher ────────────────────────────────────────────────────────

    private static MathNode? ParseElement(
        XElement el,
        MathNode.MathProperties inheritedProperties)
    {
        var localName = el.Name.LocalName;
        var ns = el.Name.Namespace;

        if (ns != M) return null;   // ignore non-math namespace elements

        return localName switch
        {
            "r"        => ParseRun(el),
            "f"        => ParseFrac(el, inheritedProperties),
            "sSup"     => ParseSup(el, inheritedProperties),
            "sSub"     => ParseSub(el, inheritedProperties),
            "sSubSup"  => ParseSubSup(el, inheritedProperties),
            "sPre"     => ParsePreSubSup(el, inheritedProperties),
            "rad"      => ParseRad(el, inheritedProperties),
            "nary"     => ParseNary(el, inheritedProperties),
            "limLow"   => ParseLimit(el, false, inheritedProperties),
            "limUpp"   => ParseLimit(el, true, inheritedProperties),
            "func"     => ParseFunc(el, inheritedProperties),
            "d"        => ParseDelim(el, inheritedProperties),
            "acc"      => ParseAcc(el, inheritedProperties),
            "bar"      => ParseBar(el, inheritedProperties),
            "box"      => ParseBox(el, inheritedProperties),
            "phant"    => ParsePhantom(el, inheritedProperties),
            "borderBox"=> ParseBorderBox(el, inheritedProperties),
            "groupChr" => ParseGroupChr(el, inheritedProperties),
            "m"        => ParseMatrix(el, inheritedProperties),
            "eqArr"    => ParseEqArray(el, inheritedProperties),
            "aln"      => null,
            "argPr"    => null,
            "brk"      => null,
            "oMathPara"=> ParseMathParagraph(el, inheritedProperties),
            _          => ParseUnknown(el)
        };
    }

    // ── m:r run ──────────────────────────────────────────────────────────

    private static MathNode ParseRun(XElement rEl)
    {
        var text = string.Concat(rEl.Elements(M + "t").Select(static t => t.Value));
        var rPr  = rEl.Element(M + "rPr");

        // m:nor is a CT_OnOff: presence alone doesn't mean "on". Its m:val must be
        // read — absent val (bare <m:nor/>), "1", "true", or "on" mean normal/upright;
        // "0", "false", or "off" mean NOT normal, i.e. keep the default italic style.
        var norEl = rPr?.Element(M + "nor");
        bool isItalic = true; // default: italic (no m:nor element at all)
        if (norEl is not null)
        {
            var norVal = norEl.Attribute(M + "val")?.Value;
            bool norOn = norVal is null or "1" or "true" or "on";
            isItalic = !norOn;
        }

        // m:sty attribute: "b", "bi", "i" (italic), "p" (plain/upright)
        // "p" or "b" → not italic
        var sty = rPr?.Element(M + "sty")?.Attribute(M + "val")?.Value
               ?? rPr?.Element(M + "sty")?.Attribute("val")?.Value
               ?? rPr?.Element(M + "sty")?.Value
               ?? rPr?.Attribute(M + "sty")?.Value;
        var hasExplicitStyle = sty is not null;
        var isBold = false;
        if (sty is "p" or "b") isItalic = false;
        else if (sty is "i" or "bi") isItalic = true;
        if (sty is "b" or "bi") isBold = true;

        var alphabet = ParseMathAlphabet(ReadVal(rPr?.Element(M + "scr")));
        var isLiteral = IsOnOffOn(rPr?.Element(M + "lit"));
        var isAlignmentPoint = IsOnOffOn(rPr?.Element(M + "aln"));
        if (isLiteral && !hasExplicitStyle && alphabet is MathNode.MathAlphabet.Default or MathNode.MathAlphabet.Roman)
            isItalic = false;

        return new MathNode.Run(text, isItalic, isBold, alphabet, isLiteral, isAlignmentPoint);
    }

    private static MathNode.MathAlphabet ParseMathAlphabet(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant().Replace("_", "-");
        return normalized switch
        {
            "roman" => MathNode.MathAlphabet.Roman,
            "script" => MathNode.MathAlphabet.Script,
            "fraktur" => MathNode.MathAlphabet.Fraktur,
            "double-struck" or "doublestruck" or "double-struck-italic" => MathNode.MathAlphabet.DoubleStruck,
            "sans-serif" or "sansserif" => MathNode.MathAlphabet.SansSerif,
            "monospace" => MathNode.MathAlphabet.Monospace,
            _ => MathNode.MathAlphabet.Default
        };
    }

    // ── m:f fraction ──────────────────────────────────────────────────────

    private static MathNode ParseFrac(XElement fEl, MathNode.MathProperties inheritedProperties)
    {
        var numEl = fEl.Element(M + "num") ?? fEl;
        var denEl = fEl.Element(M + "den") ?? fEl;

        // Per ECMA-376 §22.1.2.34 (CT_FPr) / §22.1.2.35 (ST_FType): m:type is "bar"
        // (default), "skw" (skewed), "lin" (linear), or "noBar" (stacked, no bar).
        // Absent m:fPr, absent m:type, or an unrecognized value all default to Bar.
        var fPr = fEl.Element(M + "fPr");
        var typeVal = fPr?.Element(M + "type")?.Attribute(M + "val")?.Value
                   ?? fPr?.Element(M + "type")?.Value;
        var fracType = typeVal switch
        {
            "skw"   => MathNode.FracType.Skewed,
            "lin"   => MathNode.FracType.Linear,
            "noBar" => MathNode.FracType.NoBar,
            _       => MathNode.FracType.Bar
        };

        return new MathNode.Frac(
            ParseRow(numEl, inheritedProperties),
            ParseRow(denEl, inheritedProperties),
            fracType);
    }

    // ── m:sSup superscript ────────────────────────────────────────────────

    private static MathNode ParseSup(XElement el, MathNode.MathProperties inheritedProperties)
    {
        var eEl   = el.Element(M + "e")   ?? el;
        var supEl = el.Element(M + "sup") ?? el;
        return new MathNode.Sup(ParseRow(eEl, inheritedProperties), ParseRow(supEl, inheritedProperties));
    }

    // ── m:sSub subscript ─────────────────────────────────────────────────

    private static MathNode ParseSub(XElement el, MathNode.MathProperties inheritedProperties)
    {
        var eEl   = el.Element(M + "e")   ?? el;
        var subEl = el.Element(M + "sub") ?? el;
        return new MathNode.Sub(ParseRow(eEl, inheritedProperties), ParseRow(subEl, inheritedProperties));
    }

    // ── m:sSubSup ─────────────────────────────────────────────────────────

    private static MathNode ParseSubSup(XElement el, MathNode.MathProperties inheritedProperties)
    {
        var subSupPr = el.Element(M + "sSubSupPr");
        var eEl   = el.Element(M + "e")   ?? el;
        var subEl = el.Element(M + "sub") ?? el;
        var supEl = el.Element(M + "sup") ?? el;
        return new MathNode.SubSup(
            ParseRow(eEl, inheritedProperties),
            ParseRow(subEl, inheritedProperties),
            ParseRow(supEl, inheritedProperties),
            alignScripts: IsOnOffOn(subSupPr?.Element(M + "alnScr")));
    }

    // ── m:rad radical ─────────────────────────────────────────────────────

    private static MathNode ParsePreSubSup(XElement el, MathNode.MathProperties inheritedProperties)
    {
        var eEl = el.Element(M + "e");
        var subEl = el.Element(M + "sub");
        var supEl = el.Element(M + "sup");

        return new MathNode.PreSubSup(
            eEl is null ? new MathNode.Unknown(FlattenText(el)) : ParseRow(eEl, inheritedProperties),
            subEl is null ? new MathNode.Unknown(string.Empty) : ParseRow(subEl, inheritedProperties),
            supEl is null ? new MathNode.Unknown(string.Empty) : ParseRow(supEl, inheritedProperties));
    }

    private static MathNode ParseRad(XElement el, MathNode.MathProperties inheritedProperties)
    {
        // m:deg is optional; when absent or has m:argPr with m:degHide=1 → square root
        MathNode? degree = null;
        var degEl = el.Element(M + "deg");
        if (degEl is not null)
        {
            var radPr = el.Element(M + "radPr");
            bool degHide = IsOnOffOn(radPr?.Element(M + "degHide"));
            if (!degHide)
                degree = ParseRow(degEl, inheritedProperties);
        }

        var eEl = el.Element(M + "e") ?? el;
        return new MathNode.Rad(degree, ParseRow(eEl, inheritedProperties));
    }

    // ── m:nary ────────────────────────────────────────────────────────────

    private static MathNode ParseNary(XElement el, MathNode.MathProperties inheritedProperties)
    {
        var naryPr = el.Element(M + "naryPr");

        // m:chr is the operator character. Per ECMA-376 §22.1.2.75 (CT_Nary),
        // when m:chr is absent the default n-ary operator is the integral sign ∫ (U+222B).
        var chrEl = naryPr?.Element(M + "chr");
        string opChar = chrEl?.Attribute(M + "val")?.Value
                     ?? chrEl?.Value
                     ?? "∫"; // ∫ (integral) — CT_Nary default m:chr

        // m:limLoc: "undOvr" = limits above/below; "subSup" = as scripts.
        // Per ECMA-376 §22.1.2.66 (CT_LimLoc), the default (element absent) is "subSup".
        var localLimLoc = naryPr?.Element(M + "limLoc");
        var documentLimitLocation = IsIntegralOperator(opChar)
            ? inheritedProperties.IntegralLimitLocation ?? MathNode.MathLimitLocation.SubSup
            : inheritedProperties.NaryLimitLocation ?? MathNode.MathLimitLocation.UndOvr;
        var limitLocation = localLimLoc is not null
            ? ParseLimitLocationOverride(localLimLoc, MathNode.MathLimitLocation.SubSup)
                ?? MathNode.MathLimitLocation.SubSup
            : documentLimitLocation;
        bool aboveBelow = limitLocation == MathNode.MathLimitLocation.UndOvr;

        MathNode? subLimit = null, supLimit = null;
        bool subHidden = IsOnOffOn(naryPr?.Element(M + "subHide"));
        bool supHidden = IsOnOffOn(naryPr?.Element(M + "supHide"));
        var subEl = el.Element(M + "sub");
        var supEl = el.Element(M + "sup");
        if (subEl is not null && !subHidden) subLimit = ParseRow(subEl, inheritedProperties);
        if (supEl is not null && !supHidden) supLimit = ParseRow(supEl, inheritedProperties);

        // m:grow is CT_OnOff. For n-ary operators, absent means off; present with
        // absent val means on. The shared layout uses this to grow the operator
        // toward tall operands without renderer-local math policy.
        bool growOperator = IsOnOffOn(naryPr?.Element(M + "grow"));

        var eEl = el.Element(M + "e") ?? el;
        return new MathNode.Nary(
            opChar,
            aboveBelow,
            subLimit,
            supLimit,
            ParseRow(eEl, inheritedProperties),
            growOperator);
    }

    private static bool IsIntegralOperator(string operatorChar) =>
        operatorChar.Any(static character => character is
            '\u222B' or '\u222C' or '\u222D' or '\u222E' or '\u222F' or
            '\u2230' or '\u2231' or '\u2232' or '\u2233' or '\u2A0B' or
            '\u2A0C' or '\u2A0D' or '\u2A0E' or '\u2A0F');

    // ── m:func ────────────────────────────────────────────────────────────

    private static MathNode ParseLimit(
        XElement el,
        bool isUpper,
        MathNode.MathProperties inheritedProperties)
    {
        var eEl = el.Element(M + "e");
        var limEl = el.Element(M + "lim");

        return new MathNode.Limit(
            eEl is null ? new MathNode.Unknown(FlattenText(el)) : ParseRow(eEl, inheritedProperties),
            limEl is null ? new MathNode.Unknown(string.Empty) : ParseRow(limEl, inheritedProperties),
            isUpper);
    }

    private static MathNode ParseFunc(XElement el, MathNode.MathProperties inheritedProperties)
    {
        var fNameEl = el.Element(M + "fName") ?? el;
        var eEl     = el.Element(M + "e")     ?? el;
        return new MathNode.Func(
            NormalizeFunctionName(ParseRow(fNameEl, inheritedProperties)),
            ParseRow(eEl, inheritedProperties));
    }

    private static MathNode NormalizeFunctionName(MathNode node) =>
        node switch
        {
            MathNode.Run run => new MathNode.Run(
                run.Text,
                isItalic: false,
                isBold: run.IsBold,
                alphabet: run.Alphabet,
                isLiteral: run.IsLiteral,
                isAlignmentPoint: run.IsAlignmentPoint),
            MathNode.Row row => new MathNode.Row(row.Children.Select(NormalizeFunctionName).ToArray()),
            MathNode.Sup sup => new MathNode.Sup(
                NormalizeFunctionName(sup.Base),
                sup.Script),
            MathNode.Sub sub => new MathNode.Sub(
                NormalizeFunctionName(sub.Base),
                sub.Script),
            MathNode.SubSup subSup => new MathNode.SubSup(
                NormalizeFunctionName(subSup.Base),
                subSup.Sub,
                subSup.Sup,
                subSup.AlignScripts),
            MathNode.Limit limit => new MathNode.Limit(
                NormalizeFunctionName(limit.Base),
                limit.LimitValue,
                limit.IsUpper),
            MathNode.Box box => new MathNode.Box(
                NormalizeFunctionName(box.Base),
                box.OperatorEmulator,
                box.IsAlignmentPoint),
            MathNode.ArgSize argSize => new MathNode.ArgSize(
                NormalizeFunctionName(argSize.Base),
                argSize.Adjustment),
            _ => node
        };

    // ── m:d delimiter ─────────────────────────────────────────────────────

    private static MathNode ParseDelim(XElement el, MathNode.MathProperties inheritedProperties)
    {
        var dPr = el.Element(M + "dPr");

        // Per ECMA-376 §22.1.2.20 (CT_DPr): when m:begChr / m:endChr is ABSENT the
        // default brackets are "(" and ")". When the element is PRESENT but its
        // m:val is explicitly the empty string, that means NO bracket on that side
        // (e.g. one-sided/piecewise delimiters) — it must not be defaulted to "("/")"
        // nor overridden to "|"; it stays empty.
        var begChrEl = dPr?.Element(M + "begChr");
        string begChr = begChrEl is null
            ? "("
            : begChrEl.Attribute(M + "val")?.Value ?? begChrEl.Value;

        var endChrEl = dPr?.Element(M + "endChr");
        string endChr = endChrEl is null
            ? ")"
            : endChrEl.Attribute(M + "val")?.Value ?? endChrEl.Value;

        // Per ECMA-376 §22.1.2.20 (CT_DPr): m:sepChr is the separator glyph drawn
        // between the m:e children when there are two or more. When ABSENT the
        // default is ",". When PRESENT with an explicit empty m:val, no separator
        // glyph is drawn — same explicit-empty semantics as begChr/endChr.
        var sepChrEl = dPr?.Element(M + "sepChr");
        string sepChr = sepChrEl is null
            ? ","
            : sepChrEl.Attribute(M + "val")?.Value ?? sepChrEl.Value;

        // m:grow is CT_OnOff. Absent means on; present with false/off/0 means
        // delimiters keep normal glyph height instead of stretching to content.
        var growEl = dPr?.Element(M + "grow");
        bool grow = growEl is null || IsOnOffOn(growEl);
        var shape = ReadVal(dPr?.Element(M + "shp")) == "centered"
            ? MathNode.Delim.DelimiterShape.Centered
            : MathNode.Delim.DelimiterShape.Match;

        var elements = new List<MathNode>();
        foreach (var eEl in el.Elements(M + "e"))
            elements.Add(ParseRow(eEl, inheritedProperties));

        return new MathNode.Delim(begChr, endChr, elements, sepChr, grow, shape);
    }

    // ── m:acc accent ──────────────────────────────────────────────────────

    private static MathNode ParseAcc(XElement el, MathNode.MathProperties inheritedProperties)
    {
        var accPr = el.Element(M + "accPr");
        string accChar = accPr?.Element(M + "chr")?.Attribute(M + "val")?.Value
                      ?? accPr?.Element(M + "chr")?.Value
                      ?? "^"; // combining circumflex / hat

        var eEl = el.Element(M + "e") ?? el;
        return new MathNode.Acc(accChar, ParseRow(eEl, inheritedProperties));
    }

    // ── m:bar ─────────────────────────────────────────────────────────────

    private static MathNode ParseBar(XElement el, MathNode.MathProperties inheritedProperties)
    {
        var barPr = el.Element(M + "barPr");
        // m:pos: "top" (default) = overline, "bot" = underline
        bool isOver = barPr?.Element(M + "pos")?.Attribute(M + "val")?.Value is not "bot";
        var eEl = el.Element(M + "e") ?? el;
        return new MathNode.Bar(ParseRow(eEl, inheritedProperties), isOver);
    }

    // ── m:groupChr ────────────────────────────────────────────────────────

    private static MathNode ParseBox(XElement el, MathNode.MathProperties inheritedProperties)
    {
        var boxPr = el.Element(M + "boxPr");
        var eEl = el.Element(M + "e");
        return new MathNode.Box(
            eEl is null ? new MathNode.Unknown(FlattenText(el)) : ParseRow(eEl, inheritedProperties),
            operatorEmulator: IsOnOffOn(boxPr?.Element(M + "opEmu")),
            isAlignmentPoint: IsOnOffOn(boxPr?.Element(M + "aln")));
    }

    private static MathNode ParsePhantom(XElement el, MathNode.MathProperties inheritedProperties)
    {
        var phantomPr = el.Element(M + "phantPr");
        var eEl = el.Element(M + "e");

        var showEl = phantomPr?.Element(M + "show");
        bool show = showEl is null || IsOnOffOn(showEl);

        return new MathNode.Phantom(
            eEl is null ? new MathNode.Unknown(FlattenText(el)) : ParseRow(eEl, inheritedProperties),
            show,
            zeroWidth: IsOnOffOn(phantomPr?.Element(M + "zeroWid")),
            zeroAscent: IsOnOffOn(phantomPr?.Element(M + "zeroAsc")),
            zeroDescent: IsOnOffOn(phantomPr?.Element(M + "zeroDesc")),
            transparentSpacing: IsOnOffOn(phantomPr?.Element(M + "transp")));
    }

    private static MathNode ParseBorderBox(XElement el, MathNode.MathProperties inheritedProperties)
    {
        var borderBoxPr = el.Element(M + "borderBoxPr");
        var eEl = el.Element(M + "e");

        bool hideTop = IsOnOffOn(borderBoxPr?.Element(M + "hideTop"));
        bool hideBottom = IsOnOffOn(borderBoxPr?.Element(M + "hideBot"));
        bool hideLeft = IsOnOffOn(borderBoxPr?.Element(M + "hideLeft"));
        bool hideRight = IsOnOffOn(borderBoxPr?.Element(M + "hideRight"));
        bool strikeHorizontal = IsOnOffOn(borderBoxPr?.Element(M + "strikeH"));
        bool strikeVertical = IsOnOffOn(borderBoxPr?.Element(M + "strikeV"));
        bool strikeBottomLeftToTopRight = IsOnOffOn(borderBoxPr?.Element(M + "strikeBLTR"));
        bool strikeTopLeftToBottomRight = IsOnOffOn(borderBoxPr?.Element(M + "strikeTLBR"));

        return new MathNode.BorderBox(
            eEl is null ? new MathNode.Unknown(FlattenText(el)) : ParseRow(eEl, inheritedProperties),
            showTop: !hideTop,
            showBottom: !hideBottom,
            showLeft: !hideLeft,
            showRight: !hideRight,
            strikeHorizontal: strikeHorizontal,
            strikeVertical: strikeVertical,
            strikeBottomLeftToTopRight: strikeBottomLeftToTopRight,
            strikeTopLeftToBottomRight: strikeTopLeftToBottomRight);
    }

    private static bool IsOnOffOn(XElement? element)
    {
        if (element is null)
            return false;

        var val = element.Attribute(M + "val")?.Value
               ?? element.Attribute("val")?.Value
               ?? element.Value;

        if (string.IsNullOrWhiteSpace(val))
            return true;

        return val.Trim().ToLowerInvariant() switch
        {
            "0" or "false" or "off" => false,
            _ => true
        };
    }

    private static MathNode ParseGroupChr(XElement el, MathNode.MathProperties inheritedProperties)
    {
        var grpPr = el.Element(M + "groupChrPr");
        var pos = ReadVal(grpPr?.Element(M + "pos"));
        bool isAbove = pos is not "bot";

        string grpChar = ReadVal(grpPr?.Element(M + "chr"))
                      ?? (isAbove ? "\u23DE" : "\u23DF");
        var eEl = el.Element(M + "e") ?? el;
        return new MathNode.GroupChr(
            grpChar,
            ParseRow(eEl, inheritedProperties),
            isAbove,
            ParseGroupChrVerticalJustification(grpPr));
    }

    // ── m:m matrix ────────────────────────────────────────────────────────

    private static MathNode.GroupChr.GroupChrVerticalJustification ParseGroupChrVerticalJustification(XElement? grpPr)
    {
        var vertJc = grpPr?.Element(M + "vertJc");
        if (vertJc is null)
            return MathNode.GroupChr.GroupChrVerticalJustification.Top;

        var val = vertJc.Attribute(M + "val")?.Value
               ?? vertJc.Attribute("val")?.Value;

        if (string.IsNullOrWhiteSpace(val))
            return MathNode.GroupChr.GroupChrVerticalJustification.Bottom;

        return val.Trim().ToLowerInvariant() switch
        {
            "bot" or "bottom" => MathNode.GroupChr.GroupChrVerticalJustification.Bottom,
            _ => MathNode.GroupChr.GroupChrVerticalJustification.Top
        };
    }

    private static MathNode ParseMatrix(XElement el, MathNode.MathProperties inheritedProperties)
    {
        var mPr = el.Element(M + "mPr");
        var columnAlignments = ParseMatrixColumnAlignments(mPr);
        var rows = new List<IReadOnlyList<MathNode>>();
        foreach (var mrEl in el.Elements(M + "mr"))
        {
            var cells = new List<MathNode>();
            foreach (var eEl in mrEl.Elements(M + "e"))
                cells.Add(ParseRow(eEl, inheritedProperties));
            rows.Add(cells);
        }
        return new MathNode.Matrix(
            rows,
            columnAlignments,
            ParseMatrixBaseJustification(mPr),
            ParseMatrixSpacingRule(mPr, "rSpRule"),
            ParseMatrixIntValue(mPr, "rSp"),
            ParseMatrixSpacingRule(mPr, "cGpRule"),
            ParseMatrixIntValue(mPr, "cGp"),
            ParseMatrixIntValue(mPr, "cSp"),
            hidePlaceholders: IsOnOffOn(mPr?.Element(M + "plcHide")));
    }

    private static IReadOnlyList<MathNode.Matrix.MatrixColumnAlignment> ParseMatrixColumnAlignments(XElement? mPr)
    {
        var alignments = new List<MathNode.Matrix.MatrixColumnAlignment>();
        var mcs = mPr?.Element(M + "mcs");
        if (mcs is null)
            return alignments;

        foreach (var mc in mcs.Elements(M + "mc"))
        {
            var mcPr = mc.Element(M + "mcPr");
            var aln = mcPr?.Element(M + "aln");
            var val = aln?.Attribute(M + "val")?.Value
                   ?? aln?.Value;
            var alignment = ParseMatrixColumnAlignment(val);
            var count = ParseMatrixColumnRepeatCount(mcPr);
            for (var i = 0; i < count; i++)
                alignments.Add(alignment);
        }

        return alignments;
    }

    private static int ParseMatrixColumnRepeatCount(XElement? mcPr)
    {
        var val = ReadVal(mcPr?.Element(M + "count"));
        return int.TryParse(val, out var parsed) && parsed > 0
            ? parsed
            : 1;
    }

    private static MathNode.Matrix.MatrixBaseJustification ParseMatrixBaseJustification(XElement? mPr)
    {
        var val = ReadVal(mPr?.Element(M + "baseJc"));
        return val switch
        {
            "top" => MathNode.Matrix.MatrixBaseJustification.Top,
            "bot" or "bottom" => MathNode.Matrix.MatrixBaseJustification.Bottom,
            _ => MathNode.Matrix.MatrixBaseJustification.Center
        };
    }

    private static MathNode.Matrix.MatrixSpacingRule? ParseMatrixSpacingRule(XElement? mPr, string localName)
    {
        var val = ReadVal(mPr?.Element(M + localName));
        return val switch
        {
            "0" => MathNode.Matrix.MatrixSpacingRule.Single,
            "1" => MathNode.Matrix.MatrixSpacingRule.OneAndHalf,
            "2" => MathNode.Matrix.MatrixSpacingRule.Double,
            "3" => MathNode.Matrix.MatrixSpacingRule.Exactly,
            "4" => MathNode.Matrix.MatrixSpacingRule.Multiple,
            _ => null
        };
    }

    private static int? ParseMatrixIntValue(XElement? mPr, string localName)
    {
        var val = ReadVal(mPr?.Element(M + localName));
        return int.TryParse(val, out var parsed) && parsed >= 0
            ? parsed
            : null;
    }

    private static MathNode.Matrix.MatrixColumnAlignment ParseMatrixColumnAlignment(string? val) =>
        val switch
        {
            "left"  => MathNode.Matrix.MatrixColumnAlignment.Left,
            "right" => MathNode.Matrix.MatrixColumnAlignment.Right,
            "ctr" or "center" => MathNode.Matrix.MatrixColumnAlignment.Center,
            _       => MathNode.Matrix.MatrixColumnAlignment.Center
        };

    // ── Unknown / fallback ────────────────────────────────────────────────

    private static string? ReadVal(XElement? element) =>
        element?.Attribute(M + "val")?.Value
        ?? element?.Attribute("val")?.Value
        ?? element?.Value;

    private static MathNode CreateRow(IReadOnlyList<MathNode> children) =>
        children.Count == 1
            ? children[0]
            : new MathNode.Row(children.ToArray());

    private static bool TryReadManualBreak(XElement element, out int? alignmentPointIndex)
    {
        alignmentPointIndex = null;

        XElement? brk = element.Name == M + "brk"
            ? element
            : element.Name.LocalName switch
            {
                "r" => element.Element(M + "rPr")?.Element(M + "brk"),
                "box" => element.Element(M + "boxPr")?.Element(M + "brk"),
                _ => null
            };

        if (brk is null)
            return false;

        alignmentPointIndex = ReadAlnAt(brk);
        return true;
    }

    private static bool TryReadArgumentSize(XElement element, out int argumentSizeAdjustment)
    {
        argumentSizeAdjustment = 0;

        if (element.Name != M + "argPr")
            return false;

        var value = ReadVal(element.Element(M + "argSz"));
        if (int.TryParse(value, out var parsed))
            argumentSizeAdjustment = Math.Clamp(parsed, -2, 2);

        return true;
    }

    private static MathNode ApplyArgumentSize(MathNode node, int? argumentSizeAdjustment) =>
        argumentSizeAdjustment.HasValue && argumentSizeAdjustment.Value != 0
            ? new MathNode.ArgSize(node, argumentSizeAdjustment.Value)
            : node;

    private static int? ReadAlnAt(XElement brk)
    {
        var value = brk.Attribute(M + "alnAt")?.Value
            ?? brk.Attribute("alnAt")?.Value;

        return int.TryParse(value, out var result) && result >= 0
            ? result
            : null;
    }

    private static MathNode ParseEqArray(XElement el, MathNode.MathProperties inheritedProperties)
    {
        var eqArrPr = el.Element(M + "eqArrPr");
        var rows = new List<MathNode>();
        var alignmentPointIndices = new List<int?>();
        var alignmentPointColumns = new List<IReadOnlyList<int>>();
        foreach (var eEl in el.Elements(M + "e"))
        {
            var (row, alignmentPointIndicesForRow) = ParseEqArrayRow(eEl, inheritedProperties);
            rows.Add(row);
            alignmentPointColumns.Add(alignmentPointIndicesForRow);
            alignmentPointIndices.Add(
                alignmentPointIndicesForRow.Count > 0
                    ? alignmentPointIndicesForRow[0]
                    : null);
        }

        return new MathNode.EqArray(
            rows,
            alignmentPointIndices,
            ParseEqArrayBaseJustification(eqArrPr),
            ParseEqArraySpacingRule(eqArrPr),
            ParseEqArrayIntValue(eqArrPr, "rSp"),
            alignmentPointColumns: alignmentPointColumns);
    }

    private static MathNode.EqArray.EqArrayBaseJustification ParseEqArrayBaseJustification(XElement? eqArrPr)
    {
        var val = ReadVal(eqArrPr?.Element(M + "baseJc"));
        return val switch
        {
            "top" => MathNode.EqArray.EqArrayBaseJustification.Top,
            "bot" or "bottom" => MathNode.EqArray.EqArrayBaseJustification.Bottom,
            _ => MathNode.EqArray.EqArrayBaseJustification.Center
        };
    }

    private static MathNode.EqArray.EqArraySpacingRule? ParseEqArraySpacingRule(XElement? eqArrPr)
    {
        var val = ReadVal(eqArrPr?.Element(M + "rSpRule"));
        return val switch
        {
            "0" => MathNode.EqArray.EqArraySpacingRule.Single,
            "1" => MathNode.EqArray.EqArraySpacingRule.OneAndHalf,
            "2" => MathNode.EqArray.EqArraySpacingRule.Double,
            "3" => MathNode.EqArray.EqArraySpacingRule.Exactly,
            "4" => MathNode.EqArray.EqArraySpacingRule.Multiple,
            _ => null
        };
    }

    private static int? ParseEqArrayIntValue(XElement? eqArrPr, string localName)
    {
        var val = ReadVal(eqArrPr?.Element(M + localName));
        return int.TryParse(val, out var parsed) && parsed >= 0
            ? parsed
            : null;
    }

    private static (MathNode Row, IReadOnlyList<int> AlignmentPointIndices) ParseEqArrayRow(
        XElement eEl,
        MathNode.MathProperties inheritedProperties)
    {
        var children = new List<MathNode>();
        var alignmentPointIndices = new List<int>();

        foreach (var child in eEl.Elements())
        {
            if (IsEquationArrayAlignmentMarker(child))
            {
                alignmentPointIndices.Add(children.Count);
                continue;
            }

            var node = ParseElement(child, inheritedProperties);
            if (node is not null)
            {
                if (IsAlignmentPointNode(node))
                    alignmentPointIndices.Add(children.Count);
                children.Add(node);
            }
        }

        var row = children.Count == 1
            ? children[0]
            : new MathNode.Row(children);

        return (row, alignmentPointIndices);
    }

    private static bool IsEquationArrayAlignmentMarker(XElement element) =>
        element.Name == M + "aln";

    private static int? GetAlignmentPointIndex(MathNode node)
    {
        if (IsAlignmentPointNode(node))
            return 0;

        if (node is not MathNode.Row row)
            return null;

        for (var i = 0; i < row.Children.Count; i++)
        {
            if (IsAlignmentPointNode(row.Children[i]))
                return i;
        }

        return null;
    }

    private static bool IsAlignmentPointNode(MathNode node) =>
        node switch
        {
            MathNode.Run run => run.IsAlignmentPoint,
            MathNode.Box box => box.OperatorEmulator && box.IsAlignmentPoint,
            _ => false,
        };

    private static MathNode ParseUnknown(XElement el)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var tEl in el.Descendants(M + "t"))
            sb.Append(tEl.Value);
        return new MathNode.Unknown(sb.ToString());
    }

    // ── Helper: flatten m:t text from an element ──────────────────────────

    internal static string FlattenText(XElement el)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var tEl in el.Descendants(M + "t"))
            sb.Append(tEl.Value);
        return sb.ToString();
    }
}

