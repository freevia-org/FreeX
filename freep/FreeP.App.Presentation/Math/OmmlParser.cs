using System;
using System.Collections.Generic;
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

    // ── Public entry point ────────────────────────────────────────────────

    /// <summary>
    /// Parses the raw OMML XML string stored on a <c>MathRunInfo.RawXml</c> into a
    /// <see cref="MathNode"/> tree.  Returns a <see cref="MathNode.Unknown"/> with the
    /// plain-text fallback if parsing fails.
    /// </summary>
    public static MathNode Parse(string rawXml, string fallbackText)
    {
        if (string.IsNullOrWhiteSpace(rawXml))
            return new MathNode.Unknown(fallbackText);

        try
        {
            var root = XElement.Parse(rawXml);
            // Locate the m:oMath element regardless of the wrapper form.
            var oMath = LocateOmath(root);
            if (oMath is null)
                return new MathNode.Unknown(fallbackText);

            return ParseRow(oMath);
        }
        catch
        {
            return new MathNode.Unknown(fallbackText);
        }
    }

    // ── Locate m:oMath ────────────────────────────────────────────────────

    private static XElement? LocateOmath(XElement root)
    {
        // Direct m:oMath root (e.g. the element was already the oMath).
        if (root.Name == M + "oMath") return root;

        // a14:m wrapper
        if (root.Name == A14 + "m")
        {
            // The a14:m element itself contains m:oMath as a child (or m:oMathPara).
            return root.Element(M + "oMathPara")?.Element(M + "oMath")
                ?? root.Element(M + "oMath")
                ?? root.Descendants(M + "oMath").FirstOrDefault();
        }

        // mc:AlternateContent wrapper — use mc:Choice
        if (root.Name == MC + "AlternateContent")
        {
            var choice = root.Element(MC + "Choice");
            return choice?.Descendants(M + "oMath").FirstOrDefault()
                ?? root.Descendants(M + "oMath").FirstOrDefault();
        }

        // Fallback: search descendants
        return root.Descendants(M + "oMath").FirstOrDefault();
    }

    // ── Row: parse a list of child elements into a Row or single node ─────

    private static MathNode ParseRow(XElement container)
    {
        var children = new List<MathNode>();
        foreach (var child in container.Elements())
        {
            var node = ParseElement(child);
            if (node is not null)
                children.Add(node);
        }

        return children.Count == 1
            ? children[0]
            : new MathNode.Row(children);
    }

    // ── Dispatcher ────────────────────────────────────────────────────────

    private static MathNode? ParseElement(XElement el)
    {
        var localName = el.Name.LocalName;
        var ns = el.Name.Namespace;

        if (ns != M) return null;   // ignore non-math namespace elements

        return localName switch
        {
            "r"        => ParseRun(el),
            "f"        => ParseFrac(el),
            "sSup"     => ParseSup(el),
            "sSub"     => ParseSub(el),
            "sSubSup"  => ParseSubSup(el),
            "rad"      => ParseRad(el),
            "nary"     => ParseNary(el),
            "func"     => ParseFunc(el),
            "d"        => ParseDelim(el),
            "acc"      => ParseAcc(el),
            "bar"      => ParseBar(el),
            "groupChr" => ParseGroupChr(el),
            "m"        => ParseMatrix(el),
            "oMathPara"=> ParseRow(el),
            _          => ParseUnknown(el)
        };
    }

    // ── m:r run ──────────────────────────────────────────────────────────

    private static MathNode ParseRun(XElement rEl)
    {
        var text = rEl.Element(M + "t")?.Value ?? string.Empty;
        var rPr  = rEl.Element(M + "rPr");

        // m:nor present → normal/upright (not italic)
        bool isItalic = rPr?.Element(M + "nor") is null;

        // m:sty attribute: "b", "bi", "i" (italic), "p" (plain/upright)
        // "p" or "b" → not italic
        var sty = rPr?.Element(M + "sty")?.Attribute(M + "val")?.Value
               ?? rPr?.Attribute(M + "sty")?.Value;
        if (sty is "p" or "b") isItalic = false;
        else if (sty is "i" or "bi") isItalic = true;

        return new MathNode.Run(text, isItalic);
    }

    // ── m:f fraction ──────────────────────────────────────────────────────

    private static MathNode ParseFrac(XElement fEl)
    {
        var numEl = fEl.Element(M + "num") ?? fEl;
        var denEl = fEl.Element(M + "den") ?? fEl;
        return new MathNode.Frac(
            ParseRow(numEl),
            ParseRow(denEl));
    }

    // ── m:sSup superscript ────────────────────────────────────────────────

    private static MathNode ParseSup(XElement el)
    {
        var eEl   = el.Element(M + "e")   ?? el;
        var supEl = el.Element(M + "sup") ?? el;
        return new MathNode.Sup(ParseRow(eEl), ParseRow(supEl));
    }

    // ── m:sSub subscript ─────────────────────────────────────────────────

    private static MathNode ParseSub(XElement el)
    {
        var eEl   = el.Element(M + "e")   ?? el;
        var subEl = el.Element(M + "sub") ?? el;
        return new MathNode.Sub(ParseRow(eEl), ParseRow(subEl));
    }

    // ── m:sSubSup ─────────────────────────────────────────────────────────

    private static MathNode ParseSubSup(XElement el)
    {
        var eEl   = el.Element(M + "e")   ?? el;
        var subEl = el.Element(M + "sub") ?? el;
        var supEl = el.Element(M + "sup") ?? el;
        return new MathNode.SubSup(
            ParseRow(eEl),
            ParseRow(subEl),
            ParseRow(supEl));
    }

    // ── m:rad radical ─────────────────────────────────────────────────────

    private static MathNode ParseRad(XElement el)
    {
        // m:deg is optional; when absent or has m:argPr with m:degHide=1 → square root
        MathNode? degree = null;
        var degEl = el.Element(M + "deg");
        if (degEl is not null)
        {
            // degHide means the degree is hidden (plain v)
            var radPr = el.Element(M + "radPr");
            bool degHide = radPr?.Element(M + "degHide")?.Attribute(M + "val")?.Value is "1" or "true";
            if (!degHide)
                degree = ParseRow(degEl);
        }

        var eEl = el.Element(M + "e") ?? el;
        return new MathNode.Rad(degree, ParseRow(eEl));
    }

    // ── m:nary ────────────────────────────────────────────────────────────

    private static MathNode ParseNary(XElement el)
    {
        var naryPr = el.Element(M + "naryPr");

        // m:chr is the operator character; default "?"
        var chrEl = naryPr?.Element(M + "chr");
        string opChar = chrEl?.Attribute(M + "val")?.Value
                     ?? chrEl?.Value
                     ?? "?";

        // m:limLoc: "undOvr" = limits above/below (??); "subSup" = as scripts (?)
        var limLoc = naryPr?.Element(M + "limLoc")?.Attribute(M + "val")?.Value
                  ?? naryPr?.Element(M + "limLoc")?.Value
                  ?? "undOvr";
        bool aboveBelow = limLoc != "subSup";

        MathNode? subLimit = null, supLimit = null;
        var subEl = el.Element(M + "sub");
        var supEl = el.Element(M + "sup");
        if (subEl is not null) subLimit = ParseRow(subEl);
        if (supEl is not null) supLimit = ParseRow(supEl);

        var eEl = el.Element(M + "e") ?? el;
        return new MathNode.Nary(opChar, aboveBelow, subLimit, supLimit, ParseRow(eEl));
    }

    // ── m:func ────────────────────────────────────────────────────────────

    private static MathNode ParseFunc(XElement el)
    {
        var fNameEl = el.Element(M + "fName") ?? el;
        var eEl     = el.Element(M + "e")     ?? el;
        return new MathNode.Func(ParseRow(fNameEl), ParseRow(eEl));
    }

    // ── m:d delimiter ─────────────────────────────────────────────────────

    private static MathNode ParseDelim(XElement el)
    {
        var dPr = el.Element(M + "dPr");

        string begChr = dPr?.Element(M + "begChr")?.Attribute(M + "val")?.Value
                     ?? dPr?.Element(M + "begChr")?.Value
                     ?? "(";
        string endChr = dPr?.Element(M + "endChr")?.Attribute(M + "val")?.Value
                     ?? dPr?.Element(M + "endChr")?.Value
                     ?? ")";

        // If begChr / endChr attribute is an empty string, use no bracket.
        // OOXML uses val="" to mean "no bracket" for cases like |…|.
        if (begChr == "" && dPr?.Element(M + "begChr") is not null)
            begChr = "|"; // default to | when explicitly set to empty (fence)

        var elements = new List<MathNode>();
        foreach (var eEl in el.Elements(M + "e"))
            elements.Add(ParseRow(eEl));

        return new MathNode.Delim(begChr, endChr, elements);
    }

    // ── m:acc accent ──────────────────────────────────────────────────────

    private static MathNode ParseAcc(XElement el)
    {
        var accPr = el.Element(M + "accPr");
        string accChar = accPr?.Element(M + "chr")?.Attribute(M + "val")?.Value
                      ?? accPr?.Element(M + "chr")?.Value
                      ?? "^"; // combining circumflex / hat

        var eEl = el.Element(M + "e") ?? el;
        return new MathNode.Acc(accChar, ParseRow(eEl));
    }

    // ── m:bar ─────────────────────────────────────────────────────────────

    private static MathNode ParseBar(XElement el)
    {
        var barPr = el.Element(M + "barPr");
        // m:pos: "top" (default) = overline, "bot" = underline
        bool isOver = barPr?.Element(M + "pos")?.Attribute(M + "val")?.Value is not "bot";
        var eEl = el.Element(M + "e") ?? el;
        return new MathNode.Bar(ParseRow(eEl), isOver);
    }

    // ── m:groupChr ────────────────────────────────────────────────────────

    private static MathNode ParseGroupChr(XElement el)
    {
        var grpPr = el.Element(M + "groupChrPr");
        string grpChar = grpPr?.Element(M + "chr")?.Attribute(M + "val")?.Value
                      ?? grpPr?.Element(M + "chr")?.Value
                      ?? "?"; // ? over-brace
        bool isAbove = grpPr?.Element(M + "pos")?.Attribute(M + "val")?.Value is not "bot";
        var eEl = el.Element(M + "e") ?? el;
        return new MathNode.GroupChr(grpChar, ParseRow(eEl), isAbove);
    }

    // ── m:m matrix ────────────────────────────────────────────────────────

    private static MathNode ParseMatrix(XElement el)
    {
        var rows = new List<IReadOnlyList<MathNode>>();
        foreach (var mrEl in el.Elements(M + "mr"))
        {
            var cells = new List<MathNode>();
            foreach (var eEl in mrEl.Elements(M + "e"))
                cells.Add(ParseRow(eEl));
            rows.Add(cells);
        }
        return new MathNode.Matrix(rows);
    }

    // ── Unknown / fallback ────────────────────────────────────────────────

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

